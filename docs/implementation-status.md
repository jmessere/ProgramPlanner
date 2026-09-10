# Current Status

_Last updated: autonomous build session - all planned phases (1 through 25)
complete: foundation, domain, seed data, core services, master data UI,
Resource Plan grid, Excel export/import, dashboard/reports, scenario
duplication, Template/Team-Template/Resource Gantt views, inline validation
warnings, Global Timeline, Ctrl+Z undo, Global Search, Person Detail,
Capacity/Rollout Capacity reports, Audit History, and Database Backup/
Restore._

## Completed

* Solution scaffold: `WmsResourcePlanner.sln` with Domain / Application /
  Infrastructure / Web (Blazor Web App, Server interactivity) / Tests
  projects, wired with correct project references.
* Full domain model per SPEC.md sections 10-24, 52-53, 94: Program,
  PlanningScenario, Template, TemplatePhase, Workstream, FocusArea, Team,
  TeamTemplateAssignment, Role, TeamRole, Person, ResourcePlanLine, Site,
  TeamSiteAssignment, AuditEntry. `Team` has no authoritative `TemplateId`;
  Team<->Template is many-to-many via `TeamTemplateAssignment`.
* EF Core (SQLite) `AppDbContext` with full relationship/index/constraint
  mapping; migrations (`InitialCreate`, `AddAuditEntry`) created and
  verified against a real SQLite file (auto-applied on app startup).
* Realistic seed data (`SeedData.cs`): 1 Program, 2 scenarios
  (Baseline/Working Plan), 4 overlapping Templates with 6 lifecycle phases
  each, 9 Workstreams, 5 Focus Areas (under Automation), 11 Teams (3 Inbound
  teams on different Templates, a shared Integration Team on 3 concurrent
  Templates, Testing/Automation/Outbound teams, 4 Rollout teams), 11 Roles,
  30 People, and ~20 ResourcePlanLines covering named allocations, open
  demand, partial FTE, and 2 intentional overallocations (Jane, Fiona).
* `ResourceTransformationService` (Application.Calculations): pure,
  DB-independent monthly-matrix <-> date-range engine. Expands
  ResourcePlanLines into per-month FTE (summing overlaps within the same
  planning-row context) and consolidates monthly values back into date
  ranges, merging adjacent identical values and treating zero as blank.
  Verified to correctly span year boundaries and 60-month horizons.
* `ResourcePlanService`: CRUD + query-by-scenario/template/team/person/
  role/workstream/date-range/open-demand.
* `CapacityService`: per-person/per-month allocated vs. capacity FTE with
  Available/FullyAllocated/Overallocated status, including primary Role
  name.
* `GapAnalysisService`: aggregates open (blank-Person) demand by
  month/template/workstream/team/role.
* `RolloutCapacityService`: Rollout-team-scoped allocation/open/capacity/
  conflict report sliceable by Template/Rollout Team/Site/Role/Person/Month
  (SPEC section 51).
* `ValidationService`: inline (non-blocking) planning warnings per
  SPEC section 84 - Overallocation, Availability, Assignment Outside
  Team-Template Assignment, Assignment Outside Team's own planned dates,
  Invalid Phase Association, and Role Mismatch (person's primary role
  differs from the line's role). Surfaced as cell-level warnings/tooltips
  on `/resource-plan`.
* `GanttService`: backs all three Gantt views (read + drag/resize-driven
  date updates for TemplatePhase, TeamTemplateAssignment, and
  ResourcePlanLine) plus `GetTimelineLinesAsync` for the Global Timeline.
  Every date mutation pushes an audit entry (`AuditService`) and an undo
  action (`UndoService`).
* `UndoService` + `<UndoBar>` component: in-memory (scoped, max depth 25)
  undo stack covering Resource Plan grid cell edits and all Gantt/timeline
  date-range mutations (move + resize), with a global Ctrl+Z/Cmd+Z
  keyboard shortcut (`wwwroot/js/gantt.js`, `window.wmsUndo`).
* `AuditService` + `/audit-history`: lightweight audit trail (SPEC section
  94) recording EntityType/EntityId/ChangeType/ChangedUtc/OldValue/NewValue
  for grid cell edits and all three Gantt-style date mutations.
* `BackupService` (Infrastructure.Data, SQLite online backup API) +
  `/backup`: Backup Database / Restore Backup (with a confirm prompt)
  for SQLite deployments per SPEC section 95.
* `LookupService`: case-insensitive get-or-create for Team, Person, Role,
  Workstream, FocusArea, Site, Template (duplicate-prevention
  infrastructure, also used by Excel import); `SearchAllAsync` powers
  Global Search (SPEC section 56) across People/Teams/Templates/
  Workstreams/Roles/Sites, embedded in `MainLayout`'s sidebar.
* Master data UI (Blazor pages): People (+ Person Detail with capacity/
  availability/allocation timeline, SPEC section 46), Roles, Workstreams +
  Focus Areas, Sites, Teams (+ Team Detail with TeamTemplateAssignment/
  TeamRole editing), Templates (+ Template Detail with Phase CRUD and
  reverse team-assignment view). All wired to the corresponding
  Application services.
* `ResourcePlanGridService` + `/resource-plan` page: the primary 60-month
  (configurable via `/settings`) editable Resource Plan grid with frozen
  row-header columns, per-cell editing (re-expand -> apply change ->
  re-consolidate -> persist), inline validation-warning highlighting, and
  an Undo bar. All leading columns (Team/Template/Workstream/Role/Person)
  are inline-editable dropdowns, each with a "+ Add new..." option that
  creates the entity on the fly via `LookupService` and re-points the
  row's lines (`UpdateRowContextAsync`, merges into an existing row if the
  new combination collides, summing FTE). An always-present blank row at
  the bottom lets a new planning row be started by simply typing an FTE
  value once Team+Role are chosen (no explicit "Add row" click). Text
  filters for Team/Template/Workstream/Role/Person. Month FTE cells
  support Excel-like Arrow-key/Enter keyboard navigation between cells
  (`wwwroot/js/gantt.js`, `window.wmsGridNav`). An optional "Show
  templates/phases overlay" toggle adds two sticky rows above the grid
  (vertically fixed while scrolling) showing each Template's and
  TemplatePhase's active months as distinct colors, for visual reference
  against the plan.
* `GanttChart.razor` (shared component) + three pages: `/gantt/templates`,
  `/gantt/teams`, `/gantt/resources` - draggable/resizable bars via a
  generic JS interop module (`wwwroot/js/gantt.js`), each with an Undo bar.
* `/global-timeline` (SPEC section 48): scenario selector, "Group by"
  dropdown (Template/Phase/Workstream/Team/Role/Person), per-dimension
  text filters, "open demand only" toggle, read-only Gantt rendering.
* `/` Overview dashboard: KPI cards (active people/teams, total planned
  FTE, open FTE, overallocated people, active templates, active rollout
  teams) and supporting tables (open demand by role, overallocated
  people), backed by `CapacityService`/`GapAnalysisService`.
* `/reports` page: Gap Analysis (open demand by template/workstream/team/
  role/month), Capacity Report (person/role/period/capacity/allocated/
  remaining/status, SPEC section 50), and Rollout Capacity (SPEC section
  51) - all three tables on one page.
* `ExcelExportService` (Infrastructure.Excel, ClosedXML): Instructions,
  Resource Plan (frozen header/columns, real `DateTime` month headers),
  Template Plan, Reference Data, and Summary sheets, reusing the
  transformation engine.
* `ExcelImportParser` (Infrastructure.Excel, ClosedXML-only, no DB) +
  `ImportService` (Application, DB access, no ClosedXML dependency):
  parses the same "Resource Plan" sheet layout, previews new/changed
  master data and rows without writing, and commits (resolves/creates
  Team/Person/Role/Workstream/Template, finds-or-creates a covering
  TeamTemplateAssignment, replaces ResourcePlanLines per row). `/excel`
  page hosts both export and import (upload -> preview -> commit) flows.
* `ScenarioService.DuplicateAsync`: duplicates a PlanningScenario's
  ResourcePlanLines into a new named scenario (master data such as Teams/
  People/Templates/Roles is shared, not copied). Wired into DI and exposed
  via a "Duplicate Scenario" button on `/resource-plan`.
* Automated tests (xUnit, 29 tests, all passing): transformation engine
  (month splitting, overlap summing, year-boundary handling, range
  consolidation, round-trip, 60-month horizon), capacity overallocation,
  rollout capacity (team-type filtering, site join, open/allocated split),
  gap analysis (open-demand-only filtering), Team<->Template many-to-many
  (including concurrent/overlapping assignments), ResourcePlanLine open
  demand semantics, Excel export structure/content, Excel import round-trip
  + new-master-data scenario, scenario duplication (copies plan lines,
  shares master data), inline validation warnings (overallocation,
  availability, team-template/phase/team-date bounds, role mismatch),
  undo (grid cell edit revert, grid row-context edit revert), configurable
  planning horizon, and Resource Plan grid row-context edits (re-pointing
  a row's lines to new Team/Role/Workstream/Person, including merge-sums-
  FTE when the new combination collides with an existing row).
* End-to-end verified: `dotnet run` successfully applies migrations, seeds
  data, and serves all navigation routes (Overview, Resource Plan, all 3
  Gantt views, Global Timeline, People + Person Detail, Teams, Templates,
  Workstreams, Roles, Sites, Reports, Excel, Audit History, Backup) with
  HTTP 200. Dashboard KPIs and new report tables independently spot-checked
  against seed data via curl.

## Remaining / Known Gaps

These are explicitly documented rather than silently omitted; see
`/docs/acceptance-results.md` "Known, Documented Gaps" for full detail:

* Undo does not cover entity creation (Import commit, LookupService
  inline-create, Team/Template/Person/Role/Workstream/Site CRUD) or
  deletion - only date-range mutations (grid FTE edits, Gantt/timeline
  move+resize) are undoable. Deliberate scope reduction.
* Real-browser interactive verification of drag/resize and Ctrl+Z has not
  been performed (only unit/service tests and static SSR HTML route
  checks) - flagged MANUAL VERIFICATION REQUIRED.
* Resource Plan grid leading columns now support "+ Add new..." inline
  creation via `LookupService` (like Excel import), not just plain
  dropdown selection of existing values.

Since closed:
* Filter persistence across page navigation/session (SPEC section 57) -
  Global Timeline filters/grouping now live in a scoped `FilterStateService`
  injected into the page, so they survive navigation for the session.
* `ImportService.CommitAsync` no longer clears `FocusAreaId` on imported
  rows - a Focus Area column was added to the export/import layout and
  wired through preview/commit; covered by a round-trip regression test.
* Combined "Filled / Open / Total Need" table per role/team (Acceptance
  Scenario D) - added to `/reports` via
  `GapAnalysisService.GetFilledOpenTotalAsync`.

## Architecture Decisions

See `/docs/implementation-decisions.md`.

## Test Status

`dotnet test tests/WmsResourcePlanner.Tests` -> 22/22 passing.

## Acceptance Status

See `/docs/acceptance-results.md` for the full Definition-of-Done and
Critical-Acceptance-Scenario assessment (25 PASS / 1 PARTIAL / 0 FAIL of 28
DoD items; 9 PASS / 1 PARTIAL / 0 FAIL of 10 Critical Acceptance Scenarios).

## Next Action

No blocking work remains. Remaining items are all additive polish/QA:
(1) extending Undo to entity creation/deletion, (2) a real-browser manual
QA pass on drag/resize and Ctrl+Z, (3) free-text inline creation on the
Resource Plan grid itself (currently Excel-import-only).
