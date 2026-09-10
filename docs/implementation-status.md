# Current Status

_Last updated: autonomous build session - foundation, domain, seed data, core
services, master data UI, Resource Plan grid, Excel export, dashboard/reports,
and scenario duplication complete._

## Completed

* Solution scaffold: `WmsResourcePlanner.sln` with Domain / Application /
  Infrastructure / Web (Blazor Web App, Server interactivity) / Tests
  projects, wired with correct project references.
* Full domain model per SPEC.md sections 10-24, 52-53: Program,
  PlanningScenario, Template, TemplatePhase, Workstream, FocusArea, Team,
  TeamTemplateAssignment, Role, TeamRole, Person, ResourcePlanLine, Site,
  TeamSiteAssignment. `Team` has no authoritative `TemplateId`; Team↔Template
  is many-to-many via `TeamTemplateAssignment`.
* EF Core (SQLite) `AppDbContext` with full relationship/index/constraint
  mapping; initial migration (`InitialCreate`) created and verified against
  a real SQLite file (auto-applied on app startup).
* Realistic seed data (`SeedData.cs`): 1 Program, 2 scenarios
  (Baseline/Working Plan), 4 overlapping Templates with 6 lifecycle phases
  each, 9 Workstreams, 5 Focus Areas (under Automation), 11 Teams (3 Inbound
  teams on different Templates, a shared Integration Team on 3 concurrent
  Templates, Testing/Automation/Outbound teams, 4 Rollout teams), 11 Roles,
  30 People, and ~20 ResourcePlanLines covering named allocations, open
  demand, partial FTE, and 2 intentional overallocations (Jane, Fiona).
* `ResourceTransformationService` (Application.Calculations): pure,
  DB-independent monthly-matrix ↔ date-range engine. Expands
  ResourcePlanLines into per-month FTE (summing overlaps within the same
  planning-row context) and consolidates monthly values back into date
  ranges, merging adjacent identical values and treating zero as blank.
  Verified to correctly span year boundaries and 60-month horizons.
* `ResourcePlanService`: CRUD + query-by-scenario/template/team/person/
  role/workstream/date-range/open-demand.
* `CapacityService`: per-person/per-month allocated vs. capacity FTE with
  Available/FullyAllocated/Overallocated status.
* `GapAnalysisService`: aggregates open (blank-Person) demand by
  month/template/workstream/team/role.
* `LookupService`: case-insensitive get-or-create for Team, Person, Role,
  Workstream, FocusArea, Site (duplicate-prevention infrastructure).
* Master data UI (Blazor pages): People, Roles, Workstreams + Focus Areas,
  Sites, Teams (+ Team Detail with TeamTemplateAssignment/TeamRole editing),
  Templates (+ Template Detail with Phase CRUD and reverse team-assignment
  view). All wired to the corresponding Application services.
* `ResourcePlanGridService` + `/resource-plan` page: the primary 60-month
  editable Resource Plan grid with frozen row-header columns, add-row UI,
  and per-cell editing (re-expand → apply change → re-consolidate →
  persist).
* `/` Overview dashboard: KPI cards (active people/teams, total planned
  FTE, open FTE, overallocated people, active templates, active rollout
  teams) and supporting tables (open demand by role, overallocated
  people), backed by `CapacityService`/`GapAnalysisService`. Verified live
  against seed data (30 people, 11 teams, 17.75 planned FTE, 4 open FTE,
  2 overallocated people, 4 active templates/rollout teams).
* `/reports` Gap Analysis page (open demand table by template/workstream/
  team/role/month).
* `ExcelExportService` (Infrastructure.Excel, ClosedXML): Instructions,
  Resource Plan (frozen header/columns, real `DateTime` month headers),
  Template Plan, Reference Data, and Summary sheets, reusing the
  transformation engine. `/excel` page triggers download via JS interop
  (`wwwroot/js/app.js`).
* `ScenarioService.DuplicateAsync`: duplicates a PlanningScenario's
  ResourcePlanLines into a new named scenario (master data such as Teams/
  People/Templates/Roles is shared, not copied). Wired into DI and exposed
  via a "Duplicate Scenario" button on `/resource-plan`.
* Automated tests (xUnit, 15 tests, all passing): transformation engine
  (month splitting, overlap summing, year-boundary handling, range
  consolidation, round-trip, 60-month horizon), capacity overallocation,
  gap analysis (open-demand-only filtering), Team↔Template many-to-many
  (including concurrent/overlapping assignments), ResourcePlanLine open
  demand semantics, Excel export structure/content, scenario duplication
  (copies plan lines, shares master data).
* End-to-end verified: `dotnet run` successfully applies migrations, seeds
  data, and serves all 10+ navigation routes with HTTP 200. Dashboard KPIs
  independently spot-checked against seed data via curl.

## In Progress

* None (between phases). Gantt views (Phases 11-13) are the next planned
  work; not started.

## Remaining

* Template Gantt, Team-Template Gantt, Resource Gantt (drag/resize
  timeline editing) - Phases 11-13. Highest-effort remaining item
  (requires JS interop for drag/resize); not started.
* Validation warnings surfaced inline in grid/detail pages (overallocation,
  outside-availability, outside-team-template-dates) - Phase 14. Dashboard
  already surfaces overallocation at a summary level; no inline per-cell
  warnings yet.
* Excel import + preview + round-trip tests - Phases 16-18. Not started.
* Combined "Filled / Open / Total Need" report view per role/team
  (Acceptance Scenario D) - not implemented as a single view (data is
  independently available via CapacityService/GapAnalysisService).
* Filter bar (template/workstream/team/role/person/date) on grid/reports -
  not implemented; only scenario selection exists today.
* Rollout Capacity report (multiple simultaneous rollout teams) - not
  implemented as a dedicated view; underlying data model and seed data
  fully support the scenario.
* Global timeline (Phase 21), undo/Ctrl+Z (Phase 22), UX refinement
  (Phase 23) - not started.
* Architecture audit against SPEC.md (Phase 24) - not formally performed
  as a standalone pass, though the implementation was built directly from
  spec section-by-section.

## Known Issues

* Interactive Blazor Server circuit behavior (grid cell editing, add-row,
  detail-page editing) has been verified via unit/service tests and static
  SSR HTML checks, but not exercised through a live browser/SignalR
  session in this environment - flagged as MANUAL VERIFICATION REQUIRED
  for interactive editing flows.
* No inline validation warnings yet for overallocation/out-of-range dates
  at the point of edit (only after-the-fact via dashboard/reports).
* Excel import is not implemented; the workbook is currently export-only.

## Architecture Decisions

See `/docs/implementation-decisions.md`.

## Test Status

`dotnet test tests/WmsResourcePlanner.Tests` → 15/15 passing.

## Acceptance Status

See `/docs/acceptance-results.md` for the full Definition-of-Done and
Critical-Acceptance-Scenario assessment (17 PASS / 3 PARTIAL / 8 FAIL of 28
DoD items; primary gaps are Gantt and Excel import).

## Next Action

In priority order for a follow-up session: (1) inline validation warnings
(cheap, high value), (2) a basic Template Gantt (read-heavy, simplest of
the three Gantt views), (3) Excel import, (4) Resource Gantt drag/resize,
(5) remaining polish items (filters, combined gap report, undo, global
timeline).
