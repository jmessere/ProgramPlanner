# Current Status

_Last updated: autonomous build session, foundation + domain + core services complete._

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
* Automated tests (xUnit, 13 tests, all passing): transformation engine
  (month splitting, overlap summing, year-boundary handling, range
  consolidation, round-trip, 60-month horizon), capacity overallocation,
  gap analysis (open-demand-only filtering), Team↔Template many-to-many
  (including concurrent/overlapping assignments), ResourcePlanLine open
  demand semantics.
* End-to-end verified: `dotnet run` successfully applies migrations, seeds
  data, and serves the default Blazor page (HTTP 200) on first run.

## In Progress

* None (between phases).

## Remaining

* Lookup infrastructure (searchable/creatable dropdowns, duplicate
  prevention) - Phase 4.
* Master data UI: People, Teams, Roles, Templates, Template Phases,
  Workstreams, Focus Areas, Sites - Phase 5.
* Team↔Template assignment UI on Team/Template detail pages - Phase 6.
* Resource Plan monthly grid (60-month editable spreadsheet-style screen)
  - Phase 10.
* Template Gantt, Team-Template Gantt, Resource Gantt (drag/resize
  timeline editing) - Phases 11-13.
* Validation warnings (overallocation, outside-availability, etc.) -
  Phase 14.
* Excel export (ClosedXML workbook: Resource Plan, Template Plan,
  Reference Data, Summary) - Phase 15.
* Excel import + preview - Phases 16-17.
* Excel round-trip tests - Phase 18.
* Dashboard/reports (Overview, Gap Analysis, Person Capacity, Rollout
  Capacity) - Phase 19.
* Planning scenario duplication UI - Phase 20.
* Global timeline, undo, UX refinement, architecture audit, acceptance
  verification - Phases 21-25.

## Known Issues

* None currently blocking. UI layer (grid/Gantt) has not yet been built,
  so no in-application editing exists yet - all verification so far is at
  the domain/service/data layer.

## Architecture Decisions

See `/docs/implementation-decisions.md`.

## Test Status

`dotnet test tests/WmsResourcePlanner.Tests` → 13/13 passing.

## Next Action

Implement Phase 4 (reusable lookup/inline-create infrastructure) and Phase 5
(master data UI), then Phase 10 (Resource Plan monthly grid) to establish
the primary end-to-end planning workflow, per the scope-degradation
priority order in the build instructions.
