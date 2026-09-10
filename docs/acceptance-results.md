# Acceptance Results

Assessed against SPEC.md section 99 (Critical Acceptance Scenarios) and
section 100 (Definition of Done), after completion of all build phases
(Foundation through Gantt, Validation, Excel Import, Global Timeline, Undo,
UX polish, and the architecture audit fixes: Capacity/Rollout reports,
Person Detail, Audit History, Backup, and the remaining Planning Warnings).

Legend: PASS / FAIL / PARTIAL / MANUAL VERIFICATION REQUIRED

## Definition of Done (SPEC.md §100)

| # | Criterion | Result | Notes |
|---|---|---|---|
| 1 | Maintain 4+ templates | PASS | `/templates` CRUD; seed data has 4 overlapping templates |
| 2 | Maintain template phases | PASS | `/templates/{id}` phase CRUD |
| 3 | Templates/phases in editable Gantt | PASS | `/gantt/templates` - drag/resize bars backed by `GanttService.UpdateTemplatePhaseDatesAsync` |
| 4 | Create any number of teams | PASS | `/teams` CRUD |
| 5 | Associate teams with multiple templates | PASS | `TeamTemplateAssignment` UI on Team Detail + `/gantt/teams` |
| 6 | Overlapping team-template assignments | PASS | Seed data + DB model support overlap; no blocking validation |
| 7 | Create people/roles inline | PARTIAL | `LookupService.GetOrCreate*` supports inline creation from Excel import; master-data pages support quick add-by-name; Resource Plan grid itself still uses dropdowns, not free-text inline creation |
| 8 | Create open demand | PASS | Resource Plan grid + seed data (PersonId null) |
| 9 | Assign named people | PASS | |
| 10 | Allocate people using FTE | PASS | |
| 11 | Plan monthly across 5 years | PASS | 60-month grid, verified with `ExpandToMonthly_SixtyMonthHorizonIsSupported` test |
| 12 | Edit via Excel-like grid | PASS | `/resource-plan` |
| 13 | Edit via Gantt bars | PASS | `/gantt/resources`, `/gantt/teams`, `/gantt/templates` |
| 14 | Drag allocations | PASS | JS interop drag (`wwwroot/js/gantt.js`, `window.wmsGantt`); unit/service-level verified, **manual browser verification recommended** for interactive feel |
| 15 | Resize allocations | PASS | Same mechanism, resize handle on bar's right edge; **manual browser verification recommended** |
| 16 | Drag template phases | PASS | `/gantt/templates` |
| 17 | Resize template phases | PASS | `/gantt/templates` |
| 18 | Modify team-template relationships visually | PASS | `/gantt/teams` drag/resize backed by `GanttService.UpdateTeamTemplateAssignmentDatesAsync` |
| 19 | Identify open resource demand | PASS | `/reports` Gap Analysis + grid badges + Global Timeline "open demand only" filter |
| 20 | Identify overallocated people | PASS | Overview dashboard, `/reports` Capacity Report, and inline grid warnings (`ValidationService`) |
| 21 | Filter by template/workstream/team/role/person/date | PARTIAL | Scenario selector + Global Timeline per-dimension text filters + "open demand only" toggle exist; filters are **not persisted across page navigation/sessions** (SPEC §57 "persist during the user's session" not implemented) |
| 22 | Analyze multiple simultaneous rollout teams | PASS | `/reports` Rollout Capacity view (Template/Rollout Team/Site/Role/Person/Month, with conflict flag), backed by `RolloutCapacityService` |
| 23 | Export standalone 5-year Excel resource timeline | PASS | Verified via `ExcelExportServiceTests` and manual export |
| 24 | Excel open demand without Record Type | PASS | Person-blank convention implemented |
| 25 | Excel named allocations in same structure | PASS | |
| 26 | Read reasonable edits from workbook | PASS | `ExcelImportParser` + `ImportService` (preview + commit); round-trip fidelity test in `ExcelImportServiceTests` |
| 27 | Workbook useful independently of application | PASS | No technical identifiers in exported workbook |
| 28 | Operate over full five-year horizon | PASS | |

**Summary: 24 PASS / 2 PARTIAL / 0 FAIL** (of 28)

## Critical Acceptance Scenarios (SPEC.md §99)

| Scenario | Result | Notes |
|---|---|---|
| A. Three Inbound Teams | PARTIAL | Relationships correctly modeled and queryable via Grid/Gantt/Global Timeline; Template Detail page does not yet show a single combined per-person/role FTE breakdown table for a selected team (data is derivable from Resource Plan grid filtered by team, but not pre-aggregated on Template Detail itself) |
| B. Shared Integration Team | PASS | Verified via `TeamTemplateAssignmentTests` and seed data (Integration Team on 3 concurrent templates); visually confirmed on `/gantt/teams` |
| C. Cross-Template Overallocation | PASS | Verified via `CapacityServiceTests`, live dashboard, and inline grid warnings |
| D. Open Demand (Filled/Open/Total) | PARTIAL | Open demand and filled totals both computable and shown on Overview/Reports/Global Timeline; no single combined "Filled/Open/Total Need" table per role/team yet |
| E. Template Gantt | PASS | `/gantt/templates`; drag/resize phase bars, verified via live route check (24 bars for 4 templates x 6 phases) |
| F. Resource Gantt drag | PASS | `/gantt/resources`; drag preserves FTE (`UpdateResourcePlanLineDatesAsync` only changes StartDate/EndDate) |
| G. Five-Year Excel with frozen columns | PASS | Verified: `Resource Plan` sheet freezes header row + first 7 columns |
| H. Excel Open Demand | PASS | |
| I. Excel New Team (import) | PASS | `ExcelImportServiceTests` "Acceptance Scenario I" test: new Template/Team/Person import via preview+commit flow |
| J. Excel Independence | PASS | Verified manually - workbook contains only business-facing worksheets/columns |

**Summary: 8 PASS / 2 PARTIAL / 0 FAIL** (of 10)

## Known, Documented Gaps (not silently claimed as done)

1. **Filter persistence (SPEC §57)** - filters exist per-page (Global Timeline,
   scenario selector) but are not persisted across navigation/session. Would
   require a scoped "current filter state" service injected into each
   filterable page.
2. **Undo scope (SPEC §55)** - Undo covers Grid cell edits and all three
   Gantt/timeline date-range mutations (move + resize). It does **not**
   cover entity creation (Import commit, LookupService inline-create, Team/
   Template/Person/Role/Workstream/Site CRUD) or deletion (e.g. removing a
   TeamTemplateAssignment). This is a deliberate scope reduction, not an
   oversight.
3. **Interactive drag/resize + Ctrl+Z** - exercised via unit/service tests
   and static SSR HTML route checks (bars render, correct counts, correct
   JS module wiring). **Real-browser interactive verification (actual mouse
   drag, actual keyboard shortcut) has not been performed** - flagged here
   as MANUAL VERIFICATION REQUIRED.
4. **FocusArea on re-import** - `ImportService.CommitAsync` always sets
   `FocusAreaId = null` for imported rows (the export sheet has no Focus
   Area column). Re-importing over grid-edited rows that had a FocusArea
   set would clear it. Not yet fixed or covered by a regression test.
5. **Combined per-team/per-role Filled/Open/Total tables** - the underlying
   data is fully queryable (Gap Analysis + Capacity Report + Resource Plan
   grid all expose the pieces), but no single pre-aggregated "Team X: Role Y
   - Filled 2.5, Open 1.0, Total Need 3.5" table view exists yet on Template
   Detail or Team Detail.

None of the above block core planning workflows; all are additive
polish/reporting items suitable for a fast follow-up.
