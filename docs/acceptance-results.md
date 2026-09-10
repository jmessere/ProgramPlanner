# Acceptance Results

Assessed against SPEC.md section 99 (Critical Acceptance Scenarios) and
section 100 (Definition of Done), at the end of this build session.

Legend: PASS / FAIL / PARTIAL / MANUAL VERIFICATION REQUIRED

## Definition of Done (SPEC.md §100)

| # | Criterion | Result | Notes |
|---|---|---|---|
| 1 | Maintain 4+ templates | PASS | `/templates` CRUD; seed data has 4 overlapping templates |
| 2 | Maintain template phases | PASS | `/templates/{id}` phase CRUD |
| 3 | Templates/phases in editable Gantt | FAIL | No Gantt UI implemented; list/table only |
| 4 | Create any number of teams | PASS | `/teams` CRUD |
| 5 | Associate teams with multiple templates | PASS | `TeamTemplateAssignment` UI on Team Detail |
| 6 | Overlapping team-template assignments | PASS | Seed data + DB model support overlap; no blocking validation |
| 7 | Create people/roles inline | PARTIAL | `LookupService.GetOrCreate*` exists; dedicated master-data pages support quick add-by-name, but the Resource Plan grid's "add row" uses dropdowns, not free-text inline creation |
| 8 | Create open demand | PASS | Resource Plan grid + seed data (PersonId null) |
| 9 | Assign named people | PASS | |
| 10 | Allocate people using FTE | PASS | |
| 11 | Plan monthly across 5 years | PASS | 60-month grid, verified with `ExpandToMonthly_SixtyMonthHorizonIsSupported` test |
| 12 | Edit via Excel-like grid | PASS | `/resource-plan` |
| 13 | Edit via Gantt bars | FAIL | Not implemented |
| 14 | Drag allocations | FAIL | Not implemented |
| 15 | Resize allocations | FAIL | Not implemented |
| 16 | Drag template phases | FAIL | Not implemented |
| 17 | Resize template phases | FAIL | Not implemented |
| 18 | Modify team-template relationships visually | FAIL | Form-based editing only, no drag/resize |
| 19 | Identify open resource demand | PASS | `/reports` gap analysis + grid badges |
| 20 | Identify overallocated people | PASS | Overview dashboard; verified live (2 overallocated people detected from seed data) |
| 21 | Filter by template/workstream/team/role/person/date | PARTIAL | Scenario selector exists; no dedicated filter bar on the grid/reports yet |
| 22 | Analyze multiple simultaneous rollout teams | PARTIAL | Data model + seed data support it (4 concurrent Rollout teams); no dedicated Rollout Capacity report |
| 23 | Export standalone 5-year Excel resource timeline | PASS | Verified via `ExcelExportServiceTests` and manual export |
| 24 | Excel open demand without Record Type | PASS | Person-blank convention implemented |
| 25 | Excel named allocations in same structure | PASS | |
| 26 | Read reasonable edits from workbook | FAIL | Excel import not implemented |
| 27 | Workbook useful independently of application | PASS | No technical identifiers in exported workbook |
| 28 | Operate over full five-year horizon | PASS | |

**Summary: 17 PASS / 3 PARTIAL / 8 FAIL** (of 28)

## Critical Acceptance Scenarios (SPEC.md §99)

| Scenario | Result | Notes |
|---|---|---|
| A. Three Inbound Teams | PARTIAL | Relationships correctly modeled and queryable; Template Detail page does not yet show a full per-person/role FTE breakdown for a selected team (only assignment list) |
| B. Shared Integration Team | PASS | Verified via `TeamTemplateAssignmentTests` and seed data (Integration Team on 3 concurrent templates) |
| C. Cross-Template Overallocation | PASS | Verified via `CapacityServiceTests` and live dashboard (Jane/Fiona both flagged overallocated) |
| D. Open Demand (Filled/Open/Total) | PARTIAL | Open demand and filled totals both computable and shown on Overview/Reports; no single combined "Filled/Open/Total Need" table per role/team yet |
| E. Template Gantt | FAIL | Not implemented |
| F. Resource Gantt drag | FAIL | Not implemented |
| G. Five-Year Excel with frozen columns | PASS | Verified: `Resource Plan` sheet freezes header row + first 7 columns |
| H. Excel Open Demand | PASS | |
| I. Excel New Team (import) | FAIL | Excel import not implemented |
| J. Excel Independence | PASS | Verified manually - workbook contains only business-facing worksheets/columns |

## Notes

All FAIL/PARTIAL items above are scoped-out remaining work (Gantt, Excel
import, combined gap reporting, filter bar), consistent with the
scope-degradation policy in the original build instructions - depth over
breadth, no placeholder/fake completions. See
`/docs/implementation-status.md` "Remaining" section for the prioritized
plan to close these gaps in a follow-up session.
