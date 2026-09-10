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

## Scope not completed this session

Given the size of SPEC.md and the priority order in the build instructions
(functional depth over broad, shallow coverage), the following were
deliberately left unimplemented rather than stubbed or faked: Template/Team-
Template/Resource Gantt views (drag/resize), Excel import, inline validation
warnings, a dedicated combined Filled/Open/Total report, a grid/report
filter bar, global timeline, and undo. These are recorded honestly as FAIL/
PARTIAL in `/docs/acceptance-results.md` rather than represented as done.
