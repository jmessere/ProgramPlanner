# Implementation Decisions

Record of consequential decisions made autonomously during implementation.
Trivial coding decisions are not recorded here.

## Solution structure

Four-project layout as recommended by SPEC.md section 8:
`WmsResourcePlanner.Domain` → `WmsResourcePlanner.Application` →
`WmsResourcePlanner.Infrastructure` → `WmsResourcePlanner.Web`, plus
`WmsResourcePlanner.Tests`.

## EF Core version pinning

.NET 9 SDK is installed, but the `dotnet add package` default resolved
`Microsoft.EntityFrameworkCore.*` 10.0.x, which targets net10.0 and is
incompatible with net9.0. Pinned all EF Core packages to `9.0.9`.

## Application layer data-access abstraction

`WmsResourcePlanner.Application` depends only on `Domain`, not
`Infrastructure` (per the recommended architecture, Infrastructure should
depend on Application, not the reverse). To let Application-layer services
(ResourcePlanService, CapacityService, GapAnalysisService,
ResourceTransformationService) perform queries without depending on
Infrastructure, an `IAppDbContext` interface (DbSet properties +
SaveChangesAsync) lives in `Application.Interfaces`, and the concrete
`AppDbContext` (Infrastructure) implements it. DI registers `AppDbContext`
and exposes it as `IAppDbContext`.

## Team Type / Employee Type extensibility

SPEC.md requires these to be extensible without code changes. Implemented
as plain `string` properties (not enums) with sensible defaults, rather than
a lookup table, to minimize complexity while remaining extensible. Revisit
if UI requirements demand managed lookup lists with metadata (e.g. sort
order, color).

## ResourcePlanLine → Template relationship

SPEC.md's ResourcePlanLine fields do not include a direct `TemplateId`.
`Template` is resolved via `TeamTemplateAssignmentId` (preferred - resolves
ambiguity when a Team supports multiple Templates concurrently) or via
`TemplatePhaseId.Template` when a phase-specific assignment is used instead.
Both are optional, matching the spec's requirement that Phase not force
splitting of resource records.

## Monthly ↔ date-range transformation engine

Implemented as a pure, DB-independent class (`ResourceTransformationService`
in `Application.Calculations`) so it is:
- reusable by the Grid UI, Excel export, and Excel import (once implemented)
- fully unit-testable without EF Core

Grouping key (`ResourcePlanRowKey`) is the tuple of all non-date/FTE
dimensions on ResourcePlanLine (Scenario, Team, TeamTemplateAssignment,
TemplatePhase, Workstream, FocusArea, Role, Person). Overlapping lines
within the same row-key context are summed per month. Zero-FTE months are
treated as blank and break a consolidated range, matching SPEC.md section 66.

## Date representation

Used `DateOnly` (not `DateTime`) for all planning date fields (StartDate,
EndDate, AvailableStartDate, etc.) since resource planning is date-based,
not time-based. Audit fields (CreatedUtc/ModifiedUtc) remain `DateTime`.

## Dev database location

SQLite file at `src/WmsResourcePlanner.Web/App_Data/wmsresourceplanner.db`,
excluded from source control via `.gitignore`. Migrations + seed data run
automatically on startup (`db.Database.Migrate()` + `SeedData.SeedAsync`).

## `Program` entity name collision with top-level `Program.cs`

The Domain entity `Program` collides with the implicit top-level `Program`
class generated for `WmsResourcePlanner.Web/Program.cs`, and with
`Routes.razor`'s `typeof(Program)` usage for the Blazor router assembly.
Resolution: `WmsResourcePlanner.Domain.Entities` is **not** added to the
global `_Imports.razor`; it is imported per-page where an entity type is
needed, keeping the global top-level `Program` class unambiguous.

## Razor `@bind` + `@onchange` conflict (RZ10008)

Combining `@bind="x"` with a manual `@onchange="y"` handler on the same
element fails to compile (both lower to the same `onchange` DOM attribute).
Fixed by using `@bind:after="y"` instead, which composes correctly with
`@bind`. Applied across all master-data pages via a scripted regex
replacement; manual (non-`@bind`) `@onchange` usages were left untouched.

## Scenario duplication scope

`ScenarioService.DuplicateAsync` copies only `ResourcePlanLine` records into
the new scenario. Master data (Teams, People, Roles, Templates, Workstreams,
etc.) is shared/referenced, never duplicated, per SPEC.md's model that
scenarios are alternate resource-plan views over the same organizational
structure.

## Scope evolution across the full build

The initial session (recorded above) deliberately deferred Gantt views,
Excel import, inline validation, a combined Filled/Open/Total report, a
filter bar, global timeline, and undo. All of these except the combined
Filled/Open/Total report and the filter bar were subsequently implemented
in later phases of the same overall build effort (see below). The
"Scope not completed this session" note above is kept for historical
accuracy about the first session; current gaps are tracked in
`/docs/acceptance-results.md` "Known, Documented Gaps".

## Gantt views: shared component + generic JS interop

All three Gantt-style pages (`/gantt/templates`, `/gantt/teams`,
`/gantt/resources`) plus the read-only Global Timeline share one
`GanttChart.razor` component and one JS module (`wwwroot/js/gantt.js`,
`window.wmsGantt`). The JS module binds `mousedown`/`mousemove`/`mouseup` at
the container level (event delegation on `.gantt-bar` elements) rather than
per-bar, and calls back into Blazor via `[JSInvokable] HandleBarChanged(id,
leftPx, widthPx)`, converting pixels to `DateOnly` via `PxPerDay` and
`HorizonStart`. This keeps drag/resize logic in one place instead of
duplicating it three times. A `[JSInvokable]` method cannot share a name
with an `EventCallback` parameter on the same component (C# member-name
collision), so the JS-invokable methods are named `HandleBarChanged`/
`HandleUndoAsync` rather than matching their `On...` parameter names.

## Undo: single shared stack, scoped lifetime

`UndoService` is registered `Scoped` (per Blazor circuit) with a single
in-memory stack (`(description, Func<Task> undo)`, max depth 25) shared
across the Resource Plan grid and all three Gantt/timeline mutation
services, rather than per-feature stacks. This satisfies SPEC section 55's
minimum bar (grid changes, timeline moves, timeline resizing) with much
less code than per-feature undo, at the cost of not covering entity
creation/deletion (see gaps doc). Ctrl+Z is wired via a single global
`keydown` listener (`window.wmsUndo`) that always retargets its
`DotNetObjectReference` to the currently-rendered `<UndoBar>` instance, so
the shortcut keeps working correctly as the user navigates between pages.

## Excel import: layering mirrors export

`ExcelImportParser` lives in `Infrastructure.Excel` (ClosedXML dependency,
no DB access) and only produces plain `ImportRow` DTOs. `ImportService`
lives in `Application.Services`, has no ClosedXML dependency, and consumes
those DTOs - preserving the same Application-does-not-depend-on-
Infrastructure boundary used everywhere else. Import is two-phase:
`PreviewAsync` (read-only, flags new Team/Person/Role/Workstream/Template
without writing) then `CommitAsync` (resolves/creates master data via
`LookupService`, finds-or-creates a covering `TeamTemplateAssignment`, then
replaces `ResourcePlanLine`s for each row's context using the same
transformation engine used by the grid).

## Audit History: lightweight, generic entry shape

`AuditEntry` (SPEC section 94) is a single flat table (EntityType,
EntityId, ChangeType, ChangedUtc, OldValue, NewValue as JSON-serialized
snapshots) rather than per-entity audit tables, so one `AuditService`
handles all four required change sources: resource plan line edits, team-
template date changes, template phase date changes, and team-scoped
resource-line date changes. It is recorded alongside (not instead of) the
Undo stack - the two features are independent (Undo needs a live delegate;
Audit needs a permanent record).

## Backup: SQLite online backup API, not raw file copy

`BackupService` uses `SqliteConnection.BackupDatabase()` (SQLite's built-in
"Backup API") rather than `File.Copy` on the `.db` file, so backups are
consistent even if the app's own connection pool has open connections at
backup time. Backups are stored as timestamped files under an
`App_Data/Backups` folder alongside the live database. Restore uses the
same API in reverse and requires an explicit JS `confirm()` prompt before
proceeding (SPEC section 95: "Restore requires confirmation").

## Global Search: dedicated interactive island in an otherwise-static layout

`GlobalSearch.razor` is the only interactive component embedded directly in
`MainLayout.razor` (`@rendermode InteractiveServer` on the component
itself), while `MainLayout` and most list pages remain static SSR except
where they already needed interactivity (grids, detail pages). This lets
the search box work on every page without making the entire layout
interactive.

