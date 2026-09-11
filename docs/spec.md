# WMS Program Resource Planning Application

## Complete Coding Specification

# 1. Objective

Build a user-friendly resource planning application for a large, multi-year WMS modernization program.

The application must allow program leadership to model and understand:

* WMS templates and their schedules
* Template lifecycle phases
* Workstreams and focus areas
* Delivery teams
* Shared teams
* Dedicated rollout teams
* Team-to-template relationships
* Roles required by teams
* Named people
* Open resource demand
* Filled resource demand / named-person allocations
* Percentage/FTE allocation
* Resource capacity
* Resource gaps
* Resource overallocation
* Resource needs across at least a five-year horizon

The application should combine two primary interaction styles:

> **Excel for structured resource planning + Gantt for visual scheduling.**

The primary business question the application should answer is:

> What teams and resources do we need, where do we need them, when do we need them, who is assigned, and where do we still have gaps or capacity conflicts?

The system must require very little configuration before users can begin planning.

---

# 2. Program Context

The application is initially intended for a large WMS modernization program.

The program uses a templated deployment approach.

There are four primary templates.

Each template moves through lifecycle phases such as:

* Design
* Build
* Test
* Deployment Preparation
* Rollout
* Hypercare

Each template may have one or more teams supporting it.

Templates may overlap.

Teams may support:

* One template
* Multiple templates
* Different templates at different times
* Multiple templates simultaneously

The program will also have multiple dedicated rollout teams operating in parallel.

The planning horizon is at least five years.

The application must not hard-code WMS-specific structures in ways that prevent other configurations later.

---

# 3. Core Product Principles

## 3.1 Planning First

This is a planning tool, not a traditional enterprise CRUD application.

Prioritize:

1. Fast planning
2. Easy modification
3. Visual understanding
4. Resource-gap visibility
5. Low configuration
6. Excel interoperability
7. Reporting
8. Visual polish

---

# 4. Excel-Like Experience

Structured planning screens should feel similar to Excel.

Prioritize:

* Editable grids
* Inline editing
* Keyboard navigation
* Copy/paste
* Bulk editing
* Quick-add rows
* Sorting
* Filtering
* Grouping
* Column resizing
* Frozen columns
* Autosave
* Undo

Avoid unnecessary modal dialogs.

Avoid requiring users to navigate to separate administration pages just to create reference data.

---

# 5. Learn As the User Works

The application should allow users to create missing planning entities directly where they are needed.

Example:

A user enters:

```text
Inbound T3
```

into a Team field.

If the team does not exist, display:

```text
+ Create "Inbound T3"
```

Selecting it should:

1. Create the team.
2. Save it.
3. Select it on the current record.
4. Make it available everywhere else.

Use this pattern for:

* People
* Teams
* Roles
* Workstreams
* Focus Areas
* Sites
* Templates where appropriate
* Other extensible reference values

Lookup matching should be case-insensitive.

Prevent obvious duplicates such as:

```text
Inbound Team
INBOUND TEAM
 inbound team
```

---

# 6. Dual Planning Experience

Any important planning item with a start/end period should support two methods of editing.

## Manual

Users can enter exact values using:

* Grid cells
* Date fields
* Forms
* Monthly planning cells

## Visual

Users can manipulate timeline/Gantt bars.

Support:

* Drag entire bar to move it
* Drag left edge to change start
* Drag right edge to change end
* Click/drag an empty area to create a new range where appropriate
* Show proposed dates while dragging
* Undo modifications
* Autosave after completion

Both methods must manipulate the same underlying records.

A Gantt view is an active planning surface, not simply a report.

---

# 7. Recommended Technology

## Application

Use:

* .NET 9
* ASP.NET Core
* Blazor Web App
* Entity Framework Core
* SQLite initially

The persistence architecture must allow SQLite to be replaced by SQL Server later without redesigning the application.

## Excel

Use:

* ClosedXML

for `.xlsx` creation and reading.

## UI Components

Use grid and Gantt components capable of supporting the interaction requirements.

Prefer open-source or freely usable libraries that do not require commercial licensing.

The application is primarily desktop-browser optimized.

Tablet usability is desirable.

Phone optimization is not a primary requirement.

---

# 8. Solution Architecture

Recommended structure:

```text
WmsResourcePlanner.sln

src/
    WmsResourcePlanner.Web/
        Components/
        Pages/
        Layout/
        Services/

    WmsResourcePlanner.Application/
        Services/
        DTOs/
        Interfaces/
        Calculations/
        Timeline/

    WmsResourcePlanner.Domain/
        Entities/
        Enums/
        ValueObjects/

    WmsResourcePlanner.Infrastructure/
        Data/
        Migrations/
        Excel/
        Repositories/

tests/
    WmsResourcePlanner.Tests/
        ResourcePlanning/
        Allocation/
        Demand/
        Capacity/
        Timeline/
        Excel/
```

Keep domain calculations independent of Blazor components.

---

# 9. Program Domain

Conceptually support:

```text
Program

 ├── Planning Scenarios

 ├── Templates
 │     └── Phases

 ├── Workstreams
 │     └── Focus Areas

 ├── Teams
 │     └── Template Assignments

 ├── Roles

 ├── People

 ├── Sites

 └── Resource Plan
       ├── Open Demand
       └── Named Allocations
```

---

# 10. Program Entity

Fields:

```text
Id
Name
Description
StartDate
EndDate
CreatedUtc
ModifiedUtc
```

The initial release may assume one active program.

The data model should support multiple programs later.

---

# 11. Planning Scenarios

Support alternative resource plans without duplicating master data.

Entity:

`PlanningScenario`

Fields:

```text
Id
ProgramId
Name
Description
IsBaseline
Active
CreatedUtc
ModifiedUtc
```

Initial scenarios:

```text
Baseline
Working Plan
```

Scenario-specific information includes resource planning lines.

Master data remains shared.

Provide:

`Duplicate Scenario`

This duplicates resource planning information but not:

* People
* Teams
* Templates
* Roles
* Workstreams

---

# 12. Templates

Entity:

`Template`

Fields:

```text
Id
ProgramId
Name
Description
StartDate
EndDate
SortOrder
Status
Notes
CreatedUtc
ModifiedUtc
```

Initial seed data:

```text
Template 1
Template 2
Template 3
Template 4
```

Templates may overlap.

Do not assume sequential execution.

---

# 13. Template Phases

Entity:

`TemplatePhase`

Fields:

```text
Id
TemplateId
Name
StartDate
EndDate
SortOrder
Notes
CreatedUtc
ModifiedUtc
```

Default phases:

```text
Design
Build
Test
Deployment Preparation
Rollout
Hypercare
```

Users must be able to:

* Rename phases
* Add phases
* Delete phases
* Change dates manually
* Move phases visually
* Resize phases visually

---

# 14. Workstreams

Entity:

`Workstream`

Fields:

```text
Id
ProgramId
Name
Description
StartDate nullable
EndDate nullable
Active
Notes
CreatedUtc
ModifiedUtc
```

Possible initial examples:

```text
Inbound
Outbound
Inventory
Automation
YMS
Reporting
Printing / Labels
Integrations
Portal
```

These are examples, not hard-coded restrictions.

---

# 15. Focus Areas

Entity:

`FocusArea`

Fields:

```text
Id
ProgramId
WorkstreamId nullable
Name
Description
StartDate nullable
EndDate nullable
Active
Notes
CreatedUtc
ModifiedUtc
```

Example:

```text
Automation
    AutoStore
    Voice
    Conveyors
    Pick & Apply
    Packing
```

Focus Area may exist without a Workstream.

---

# 16. Teams

Entity:

`Team`

Fields:

```text
Id
ProgramId
Name
TeamType
WorkstreamId nullable
FocusAreaId nullable
StartDate nullable
EndDate nullable
Active
Notes
CreatedUtc
ModifiedUtc
```

Default Team Types:

```text
Template Build
Functional
Technical
Integration
Testing
Rollout
Hypercare
Program
Other
```

Team Type must be extensible.

A Team must NOT have one authoritative `TemplateId`.

---

# 17. Team ↔ Template Many-to-Many Relationship

Teams and Templates have a time-bound many-to-many relationship.

Entity:

`TeamTemplateAssignment`

Fields:

```text
Id
TeamId
TemplateId
StartDate
EndDate
Notes
CreatedUtc
ModifiedUtc
```

This allows:

```text
Inbound T1
    → Template 1

Inbound T2
    → Template 2

Inbound T3
    → Template 3
```

while also allowing:

```text
Integration Team
    → Template 1: Jan-Jun
    → Template 2: Apr-Dec
    → Template 3: Oct-Jun
```

Assignments may overlap.

The same team can therefore support multiple templates simultaneously.

---

# 18. Team-Template Editing

From Team Detail, users should see:

| Template   | Start    | End      |
| ---------- | -------- | -------- |
| Template 1 | Jan 2027 | Jun 2027 |
| Template 2 | Apr 2027 | Dec 2027 |

From Template Detail:

| Team        | Workstream   | Start    | End      |
| ----------- | ------------ | -------- | -------- |
| Inbound T2  | Inbound      | Aug 2027 | Jun 2028 |
| Integration | Integrations | Apr 2027 | Dec 2027 |

Both should support:

* Manual editing
* Inline creation
* Gantt editing

---

# 19. Rollout Teams

Rollout teams use the standard Team entity.

```text
TeamType = Rollout
```

Do not create a separate RolloutTeam domain model.

Support multiple simultaneous rollout teams.

Examples:

```text
Rollout Team A
Rollout Team B
Rollout Team C
Rollout Team D
```

Rollout teams may support different templates and sites over different periods.

---

# 20. Roles

Entity:

`Role`

Fields:

```text
Id
ProgramId
Name
Category nullable
Description
Active
CreatedUtc
ModifiedUtc
```

Initial examples:

```text
Business Analyst
Product Owner
Scrum Master
Developer
QA
Solution Architect
Integration Engineer
Data Engineer
Site Lead
Deployment Lead
Trainer
```

Roles must be dynamically creatable.

---

# 21. Team Roles

Entity:

`TeamRole`

Fields:

```text
Id
TeamId
RoleId
DefaultRequiredFte nullable
Notes
```

This describes typical team composition.

It is not itself time-phased resource demand.

---

# 22. People

Entity:

`Person`

Fields:

```text
Id
ProgramId
FirstName
LastName
DisplayName
PrimaryRoleId nullable
Organization nullable
Location nullable
ResourcePoolId nullable
AvailableStartDate nullable
AvailableEndDate nullable
DefaultCapacityFte
Active
Notes
CreatedUtc
ModifiedUtc
```

Default:

```text
DefaultCapacityFte = 1.0
```

`ResourcePoolId` replaces the earlier free-text `EmployeeType` field and links to a `ResourcePool` (see §24a). It indicates where the person is sourced from (e.g. Internal FTE, Contractor Pool, Professional Services, a named vendor). It is optional/nullable.

---

# 23. Resource Planning Model

The business-facing planning model should be based around a common concept:

`ResourcePlanLine`

A resource plan line represents either:

1. Open resource demand
2. Named resource allocation

The distinction is determined by Person.

```text
Person == null
    → Open Demand

Person != null
    → Named Allocation / Filled Demand
```

This concept should be used consistently throughout:

* Application UI
* Excel
* Reporting
* Import/export

---

# 24. ResourcePlanLine

Recommended entity:

```text
ResourcePlanLine
```

Fields:

```text
Id
ProgramId
ScenarioId

TeamId
TeamTemplateAssignmentId nullable
TemplatePhaseId nullable

WorkstreamId nullable
FocusAreaId nullable
RoleId

PersonId nullable
ResourcePoolId nullable

StartDate
EndDate
Fte

Notes

CreatedUtc
ModifiedUtc
```

`ResourcePoolId` is only meaningful when `PersonId` is null (open demand). It represents the pool the open demand is proposed to be sourced from. Once a Person is named, the person's own `ResourcePoolId` is the effective source and the line's own `ResourcePoolId` is cleared.

`Fte` is the common unit.

Examples:

```text
0.25 = quarter FTE
0.50 = half FTE
0.75 = three-quarter FTE
1.00 = one FTE
2.00 = two FTE
```

A named person's `0.50 FTE` normally represents 50% allocation for a person whose capacity is 1.0 FTE.

---

# 24a. Resource Pools

Entity:

```text
ResourcePool
```

Fields:

```text
Id
ProgramId
Name
Type (Internal | External)
CostCenter nullable
AverageRate
Vendor nullable
Notes nullable
Active
CreatedUtc
ModifiedUtc
```

Resource Pools describe where people (or open demand) are sourced from — for example Internal FTE, Contractor Pool, Professional Services, or a named vendor. `Vendor` allows rolling up multiple pools that belong to the same external vendor.

Usage:

* Each `Person` may optionally be assigned a `ResourcePool` (`Person.ResourcePoolId`).
* Each open-demand `ResourcePlanLine` (Person == null) may optionally propose a `ResourcePool` it expects to be sourced from.
* Pools are configurable in the application UI (Configuration → Resource Pools) and are also included as Excel reference data so they can be maintained/imported from the spreadsheet.

---

# 25. Open Demand

Example:

```text
Template: Template 1
Workstream: Inbound
Team: Inbound T1
Role: BA
Person: blank
Jan-Jun
FTE: 2.0
```

This means:

> Inbound T1 requires 2.0 currently unfilled BA FTE during this period.

There is no need for a separate Record Type field.

Person being blank is authoritative.

---

# 26. Named Allocation

Example:

```text
Template: Template 1
Workstream: Inbound
Team: Inbound T1
Role: BA
Person: Jane Smith
Jan-Jun
FTE: 1.0
```

This represents Jane being allocated at one full FTE.

If Jane's capacity is 1.0:

```text
1.0 FTE = 100%
0.5 FTE = 50%
```

The application UI may display allocations as either FTE or percentage.

The stored canonical planning value should be FTE.

---

# 27. Implied Demand Model

A named allocation represents filled demand.

An unnamed line represents remaining/open demand.

Example:

```text
Inbound T1 / BA / January

Jane          1.0
Mike          0.5
Unassigned    0.5
----------------
Total Need    2.0 FTE
```

Therefore:

```text
Filled Demand = 1.5 FTE
Open Demand   = 0.5 FTE
Total Need    = 2.0 FTE
```

This makes open resource gaps explicit.

---

# 28. Capacity Calculation

For each person and time period:

```text
Total Allocation =
Sum of named ResourcePlanLine FTE
```

Compare against:

```text
Person.DefaultCapacityFte
```

Example:

```text
Jane Capacity = 1.0

Inbound       0.50
Integration   0.25
Testing       0.50
------------------
Total         1.25
```

Jane is:

```text
125% allocated
25% over capacity
```

---

# 29. Time Storage

Internally, store resource planning as date ranges.

Do NOT store one database field for every month.

Example:

```text
Jane
Jan 1-Feb 28
1.0 FTE

Jane
Mar 1-May 31
0.5 FTE
```

Monthly representations should be generated from these records.

---

# 30. Time Granularity

Support:

* Month
* Quarter
* Year

Month is the primary resource-planning interval.

The application must comfortably support at least:

```text
60 months / 5 years
```

---

# 31. Phase Association

`TemplatePhaseId` on a resource plan line is optional.

Do not force a person or demand record to be split simply because a template moves between phases.

Example:

Jane may support Template 1 continuously from January through August even though Template 1 moves through:

```text
Jan-Feb Design
Mar-Jun Build
Jul-Aug Test
```

Jane can have one allocation spanning the entire period.

The application can derive which phases her allocation overlapped.

If a resource is specifically assigned to a phase, `TemplatePhaseId` may be populated.

---

# 32. Main Navigation

Primary navigation:

```text
Overview
Resource Plan
People
Teams
Templates
Workstreams
Timeline
Reports
Excel
Settings
```

Keep navigation shallow.

---

# 33. Overview Dashboard

The dashboard should answer:

* How much total resource demand exists?
* How much is filled?
* How much remains open?
* Which roles have the largest gaps?
* Which teams have the largest gaps?
* Which templates have the largest gaps?
* Who is overallocated?
* When are resource peaks occurring?
* Which templates/phases are currently active?
* Which rollout teams are active?

Suggested indicators:

```text
Active People
Active Teams
Total Planned FTE
Open FTE
Overallocated People
Active Templates
Active Rollout Teams
```

---

# 34. High-Level Template Gantt

Create a dedicated program-level Template Gantt.

Purpose:

> Provide a visual five-year view of templates and their lifecycle phases.

Example:

```text
                     2027                       2028
              Q1   Q2   Q3   Q4         Q1   Q2   Q3   Q4

Template 1
 Design       ████
 Build             ███████
 Test                       ███
 Rollout                        █████
 Hypercare                           ██

Template 2
          Design       ████
          Build              ███████
          Test                       ████
          Rollout                         █████

Template 3
                       Design      ███
                       Build            ███████

Template 4
                                  Design       ████
```

Requirements:

* Parent row per Template
* Child rows per Phase
* Expand/collapse
* Month/quarter/year zoom
* Five-year horizontal navigation
* Template overlap
* Phase overlap
* Drag phases
* Resize phases
* Tooltips
* Exact date editing
* Optional milestone markers

---

# 35. Template Detail

Selecting a Template should provide a consolidated view of:

* Template information
* Phases
* Teams supporting it
* Team dates
* Roles
* People
* Allocation
* Open demand

Example:

```text
Template 2

Inbound T2
    Sarah — BA — 1.0 FTE
    Mark — Developer — 1.0 FTE
    BA — Unassigned — 0.5 FTE

Integration
    Amy — Integration Engineer — 0.5 FTE
```

---

# 36. Template Resource Gantt

Provide a Gantt within Template Detail.

Hierarchy:

```text
Template
 ├── Team
 │    ├── Person
 │    ├── Person
 │    └── Open Role
```

Example:

```text
                       Aug Sep Oct Nov Dec Jan Feb Mar Apr May

Inbound T2             █████████████████████████████████████
 Sarah - BA 1.0        █████████████████████
 Mark - Dev 1.0            ███████████████████████████
 BA - Open 0.5                  ███████████████

Integration            █████████████████████████████████████
 Amy - Engineer .5         █████████████████████████████
```

Bars must be editable.

---

# 37. Resource Plan Screen

This is the application's primary working surface.

Provide:

```text
[ Grid ] [ Timeline ]
```

Both operate on `ResourcePlanLine`.

---

# 38. Resource Plan Grid

The grid should support spreadsheet-style editing.

Recommended structure:

| Template | Phase | Workstream | Team       | Role | Person | Jan | Feb | Mar | Apr |
| -------- | ----- | ---------- | ---------- | ---- | ------ | --: | --: | --: | --: |
| T1       |       | Inbound    | Inbound T1 | BA   | Jane   |   1 |   1 |   1 |  .5 |
| T1       |       | Inbound    | Inbound T1 | BA   |        |  .5 |  .5 |   1 |   1 |

The application grid should conceptually mirror the Excel timeline model.

Users should immediately understand the relationship between application and spreadsheet planning.

---

# 39. Resource Timeline

Timeline mode renders the same resource plan as Gantt bars.

Allow grouping by:

* Template
* Workstream
* Team
* Role
* Person

Allow:

* Move
* Resize
* Create
* Delete
* Open exact editor

Named and open-demand bars should be visually distinguishable without excessive color.

---

# 40. Gantt Creation

Preferred workflow:

1. Select a Team/Role/Person context.
2. Drag across an empty timeline.
3. Create a ResourcePlanLine.
4. Derive start/end dates.
5. Ask only for missing information.
6. Autosave.

For open demand, Person remains blank.

---

# 41. Gantt Movement

Dragging the center of a bar changes its date range while preserving duration and FTE.

Example:

```text
Jan-Mar
    ↓ move
Apr-Jun
```

---

# 42. Gantt Resizing

Dragging the left edge changes StartDate.

Dragging the right edge changes EndDate.

During drag show:

```text
Start: Apr 1, 2028
End: Sep 30, 2028
Duration: 6 months
FTE: 0.5
```

---

# 43. Gantt Snapping

Default resource-planning behavior:

```text
Snap to month boundaries
```

When appropriate, allow finer date precision.

Users must always have access to exact manual date editing.

---

# 44. Gantt Validation

Visual edits use the same validation as manual edits.

Warn about:

* Person overallocation
* Allocation outside availability
* Allocation outside team-template dates
* Team outside template dates
* Invalid phase association

Warnings should generally not block planning.

---

# 45. Team Gantt

Team detail should visually show template relationships.

Example:

```text
Integration Team

Template 1    █████████
Template 2       █████████████
Template 3             ███████████
```

Users can:

* Add template assignment
* Drag assignment
* Resize assignment
* Delete assignment
* Enter exact dates

---

# 46. Person Detail

Show:

```text
Name
Primary Role
Resource Pool
Organization
Location
Capacity
Availability
```

Timeline example:

```text
Jane Smith

Inbound T1        ███████████ .75
Integration            █████████ .25
Outbound T2                  ███████████ 1.0
```

Display capacity conflicts.

---

# 47. Team Detail

Show:

* Team Name
* Team Type
* Workstream
* Focus Area
* Active dates
* Template assignments
* Roles
* People
* Open demand
* Allocation
* Capacity/gaps

Provide Grid and Timeline modes.

---

# 48. Global Timeline

Provide a global Gantt view capable of grouping by:

```text
Template
Template Phase
Workstream
Team
Role
Person
Rollout Team
```

Filters:

```text
Date Range
Scenario
Template
Workstream
Focus Area
Team
Role
Person
Resource Pool
Team Type
```

---

# 49. Gap Analysis

Because open demand is represented explicitly, gap reporting primarily aggregates blank-person ResourcePlanLines.

Report:

| Period | Template | Workstream | Team | Role | Open FTE |
| ------ | -------- | ---------- | ---- | ---- | -------: |

Allow:

`Show open demand only`

Drill down from aggregate gaps to underlying planning lines.

---

# 50. Capacity Report

Report:

| Person | Role | Period | Capacity | Allocated | Remaining | Status |
| ------ | ---- | ------ | -------: | --------: | --------: | ------ |

Statuses:

```text
Available
Fully Allocated
Overallocated
```

---

# 51. Rollout Capacity

Provide rollout-specific views.

Allow analysis by:

* Template
* Rollout Team
* Site
* Role
* Person
* Month

Show:

* Allocated FTE
* Open FTE
* Capacity
* Conflicts

---

# 52. Sites

Entity:

`Site`

Fields:

```text
Id
ProgramId
Name
Code nullable
Region nullable
PlannedGoLiveDate nullable
StartDate nullable
EndDate nullable
Notes
CreatedUtc
ModifiedUtc
```

---

# 53. Team-Site Assignment

Entity:

`TeamSiteAssignment`

Fields:

```text
Id
TeamId
SiteId
StartDate
EndDate
CreatedUtc
ModifiedUtc
```

Support timeline editing when surfaced.

---

# 54. Autosave

Normal planning edits should autosave.

Display lightweight status:

```text
Saving...
Saved
Unable to Save
```

Do not require a Save button for routine planning.

---

# 55. Undo

Support Undo for:

* Grid changes
* Timeline moves
* Timeline resizing
* Resource creation
* Team-template changes
* Phase changes

Preferred shortcut:

```text
Ctrl+Z
```

---

# 56. Search

Global search should include:

* People
* Teams
* Templates
* Workstreams
* Roles
* Sites

Selecting a result navigates directly to it.

---

# 57. Filters

Common filters should persist during the user's session.

Support:

```text
Scenario
Date Range
Template
Workstream
Focus Area
Team
Role
Person
Resource Pool
Team Type
```

Provide:

`Clear Filters`

---

# 58. Excel Product Philosophy

Excel is a first-class planning output.

The normal Excel workbook must be:

> A standalone, professionally designed resource-planning workbook.

A recipient should not need to know that an application exists.

The workbook must not contain user-facing terminology such as:

```text
Application ID
Import Key
Database Record
Reimport Instructions
System Mapping
```

Any application compatibility must be invisible.

---

# 59. Excel Primary Resource Timeline

The primary Excel worksheet should be:

`Resource Plan`

It must present resource planning as a five-year monthly timeline.

Initial columns:

```text
Template
Phase
Workstream
Team
Role
Person
```

Optionally include:

```text
Notes
```

Then one column for every month.

Example:

| Template | Phase | Workstream   | Team        | Role     | Person | Jan-27 | Feb-27 | Mar-27 | Apr-27 |
| -------- | ----- | ------------ | ----------- | -------- | ------ | -----: | -----: | -----: | -----: |
| T1       |       | Inbound      | Inbound T1  | BA       | Jane   |    1.0 |    1.0 |    1.0 |     .5 |
| T1       |       | Inbound      | Inbound T1  | BA       |        |     .5 |     .5 |    1.0 |    1.0 |
| T2       |       | Integrations | Integration | Engineer | Amy    |        |     .5 |     .5 |     .5 |

Continue monthly columns for a minimum of five years.

---

# 60. Excel Resource Row Semantics

Each row represents one resource planning line.

Rule:

```text
Person populated
→ Named Allocation / Filled Demand

Person blank
→ Open Demand
```

No `Item Type` or `Record Type` column is necessary.

This rule should be documented simply as a business-planning convention.

---

# 61. Excel Measurement Unit

All monthly cells should use FTE.

Examples:

```text
0.25 = ¼ FTE
0.50 = ½ FTE
0.75 = ¾ FTE
1.00 = 1 FTE
2.00 = 2 FTE
```

Do NOT mix percentage and FTE semantics in the same monthly columns.

For a standard person with 1.0 FTE capacity:

```text
0.50 FTE = 50% allocation
```

This creates one consistent measurement for:

* Allocations
* Demand
* PivotTables
* Formulas
* Charts
* Capacity calculations

---

# 62. Excel Blank Cells

Treat:

```text
Blank
```

as zero allocation/demand.

Export zero values as blank.

Avoid visually filling the five-year timeline with zeros.

During import:

```text
Blank = 0
0 = 0
```

---

# 63. Excel Frozen Planning Dimensions

Freeze:

```text
Template
Phase
Workstream
Team
Role
Person
```

and Notes if included.

Users must be able to horizontally scroll through five years while keeping planning dimensions visible.

Freeze the header vertically as well.

---

# 64. Excel Month Headers

Month headers must be based on real dates.

For example, underlying value:

```text
1/1/2027
```

displayed as:

```text
Jan-27
```

Do not store month headers only as arbitrary text.

This enables normal Excel date analysis.

---

# 65. Excel Year Bands

Provide a visual year band above monthly columns.

Example:

```text
                2027                                      2028
Jan Feb Mar Apr May Jun Jul Aug Sep Oct Nov Dec | Jan Feb Mar...
```

Do not compromise the Excel Table structure with problematic merged table headers.

The actual table headers remain unique month/year values.

---

# 66. Excel Timeline Import

The application must interpret the monthly matrix and convert it into date-range records.

Example:

```text
Jane

Jan = 1.0
Feb = 1.0
Mar = .5
Apr = .5
May = .5
```

Import as:

```text
Jan-Feb
1.0 FTE

Mar-May
0.5 FTE
```

Adjacent months with identical context and FTE should be consolidated.

---

# 67. Excel Timeline Export

The reverse transformation must occur during export.

Internal:

```text
Jane
Jan-Feb
1.0 FTE

Jane
Mar-May
0.5 FTE
```

becomes:

| Person | Jan | Feb | Mar | Apr | May |
| ------ | --: | --: | --: | --: | --: |
| Jane   |   1 |   1 |  .5 |  .5 |  .5 |

The user should never need to understand the internal date-range representation.

---

# 68. Excel Phase Behavior

Phase is optional on resource rows.

If blank, the resource supports the broader Team/Template context.

The application can derive which Template Phases overlap the resource period.

If populated, Phase represents an intentional phase-specific assignment.

Do not require resource rows to split solely because a Template transitions between phases.

---

# 69. Excel Shared Teams

Shared teams should simply appear under multiple templates.

Example:

| Template | Workstream  | Team             | Role     | Person | Jan | Feb | Mar |
| -------- | ----------- | ---------------- | -------- | ------ | --: | --: | --: |
| T1       | Integration | Integration Team | Engineer | Amy    |  .5 |  .5 |  .5 |
| T2       | Integration | Integration Team | Engineer | Amy    |     |     |  .5 |

The application derives/maintains the corresponding many-to-many Team ↔ Template relationship.

---

# 70. Excel Master Data Creation

Users should be able to type new values directly.

Example:

```text
Template: T3
Workstream: Inbound
Team: New Inbound Team
Role: BA
Person: Sally Smith
```

When the application later reads this workbook, it should recognize missing entities and create them where unambiguous.

Do not require spreadsheet users to separately register values.

---

# 71. Excel Workbook Structure

Keep the workbook small.

Recommended:

```text
Instructions
Resource Plan (Template Plan overlay table + Resource Plan table, same sheet)
Reference Data
Summary
```

The Template Plan table and the Resource Plan table live on a single
"Resource Plan" sheet - Template Plan first (top), Resource Plan directly
below it - so a planner can see the template phase overlay lined up with
the plan data while entering/reviewing FTE. Both tables share the same
month columns (fixed label columns end at the same column, month columns
start right after), so the two timelines always line up visually. See
§73 for the details of the merged layout.

Avoid dozens of worksheets.

---

# 72. Instructions Worksheet

Explain the workbook purely as a planning tool.

Example:

> This workbook is used to plan staffing, team assignments, resource requirements, and template schedules across the WMS modernization program.

Explain:

* Resource Plan (including the Template Plan overlay table at its top)
* Reference Data
* Summary
* FTE conventions
* Blank-person/open-demand convention

Do not mention application compatibility.

---

# 73. Resource Plan Worksheet (Template Plan overlay + Resource Plan table)

The "Resource Plan" sheet holds two tables, stacked vertically, sharing
the same month columns:

1. **Template Plan** (top): a human-friendly, filterable schedule table
   showing Templates and their Phases only. Team-to-Template assignments
   are **not** shown on this table - a Team's association with a Template
   is already implied by the Team values present on the Resource Plan
   table below it, so repeating it here would be redundant.
2. **Resource Plan** (below): the row-per-planning-line grid described
   later in this section.

## Template Plan table

Columns:

```text
Template
Phase
Notes
<one column per month in the planning horizon>
```

Each row represents one Template Phase. The monthly columns mark that
phase's active months with an "X" (conditionally formatted with the same
color used for that phase elsewhere in the app, so the row reads like a
Gantt bar). There are intentionally no separate Start Date/End Date
columns: the "X" marks are the single source of truth for a phase's dates,
so showing dates alongside them (which could disagree) would be
confusing.

On re-import, a phase's Start Date/End Date are always derived from the
first/last month marked "X" in that row - adding or removing an "X" and
re-importing adjusts the underlying Template Phase accordingly. A row with
no "X" marks at all is flagged as an error (a phase must have at least one
active month marked).

The workbook is the master/full state of the Template Plan: after
committing the rows present in the sheet, any Template or Phase that
already exists in the program but is no longer named anywhere in the
parsed rows is deleted, so removing a row (or an entire Template's rows)
from the sheet and re-importing removes it from the app too. This is
strictly name-based - it does not require every row to be error-free;
only rows with a blank Template or Phase name are excluded from the
"still present" set, so a row with a transient "At least one month must be
marked with X" error still protects its Template/Phase from being deleted
(only its date range fails to update that import). Deleting a Template
cascades to its Phases and TeamTemplateAssignments; deleting either a
Template or a Phase only clears (sets to null) the corresponding
TemplatePhaseId/TeamTemplateAssignmentId on any ResourcePlanLine that
referenced it - the line's Team/Role/Person/FTE data is preserved, it
simply becomes unlinked from that phase/template rather than being
deleted itself.

Month-header detection on import is deliberately resilient to a real-world
ClosedXML/Excel round-trip quirk: after a workbook has been saved and
reopened (including a plain ClosedXML `SaveAs`, not just a real Excel
save), a month header cell can come back with `DataType == Number` even
though its cell style still carries a date number format (e.g. Excel's
built-in format 17, `mmm-yy`) and its numeric value is still a valid OLE
Automation date serial - ClosedXML does not always re-classify such cells
as `DateTime` on reload. If month-column detection relied solely on
`DataType == DateTime`, this would cause every month column to go
undetected, making every Template Plan row fail with "At least one month
must be marked with X" regardless of any actual "X" marks present. To
guard against this, the importer also recognizes a header cell as a month
column when it is numeric AND its style is a known date format (built-in
number format IDs 14-22/45-47, or a custom format containing date tokens
outside quoted literals) - in that case its value is interpreted directly
as an OLE Automation date serial (`DateTime.FromOADate`).

The Template Plan table's month columns start at the same column as the
Resource Plan table's month columns below it, so the two timelines line
up. Both the Template Plan section (its Template/Phase/Notes label
columns) and the Resource Plan section below it are each implemented as
their own structured Excel Table (ListObject), so both can be filtered
independently from each other even though they share one worksheet -
Excel's one-AutoFilter-per-sheet limit only applies to the plain/legacy
AutoFilter feature, not to Tables. The month columns are intentionally
kept outside both Tables (as plain Date-typed cells rather than table
columns), because OOXML requires Table column headers to be plain text -
wrapping a date-typed header cell in a Table would strip its date typing
on save, breaking re-import parsing. This has no practical filtering
downside: an Excel Table's row filter hides/shows the entire worksheet
row, so filtering by any label column (Template, Phase, Team, Role,
Person, etc.) already hides/shows that row's month cells too, even though
those columns sit outside the Table's own column range.

Example (Jan-Jun 2027 shown):

| Template | Phase  | Notes | Jan-27 | Feb-27 | Mar-27 | Apr-27 | May-27 | Jun-27 |
| -------- | ------ | ----- | ------ | ------ | ------ | ------ | ------ | ------ |
| T1       | Design |       | X      | X      | X      |        |        |        |
| T1       | Build  |       |        |        |        | X      | X      | X      |

## Resource Plan table

Directly below the Template Plan table (separated by blank rows and its
own section title/header row), one row per planning line, filterable via
its own structured Table's column-header dropdowns (independent of the
Template Plan table's filter above it). Columns are ordered:

```text
Workstream
Team
Pool
Role
Person
Template
Phase
Focus Area
Notes
<one column per month in the planning horizon>
```

The primary planning columns (Workstream, Team, Pool, Role, Person) come
first; the secondary/context columns (Template, Phase, Focus Area, Notes)
are grouped into a collapsible outline group (collapsed by default) so
they stay out of the way but are one click away when needed. The sheet
does not freeze any rows/columns or split panes by default - it opens
plain so both the Template Plan overlay above and the Resource Plan grid
below can be scrolled/viewed freely.


---

# 74. Reference Data Worksheet

Use one worksheet containing normal Excel tables for commonly reused values.

Include sections for:

```text
People
Teams
Roles
Templates
Workstreams
Focus Areas
Sites
Resource Pools
Team Types
```

This sheet may supply dropdown values.

Users can maintain the lists directly.

On import, each of these eight tables (People, Teams, Roles, Templates,
Workstreams, Focus Areas, Sites, Resource Pools) is parsed directly and
used to create or update the corresponding entity - this is independent
of whether that name also appears anywhere on the Resource Plan or
Template Plan tables. This means, for example, adding a brand new Resource
Pool row here with its Cost Center/Average Rate/Vendor/Notes filled in,
then re-importing the workbook, creates that pool with those exact
details (not just a bare name defaulted to Internal/$0, which is all that
happens when a new pool name is only referenced via the Resource Plan
table's Pool column). Likewise, editing an existing row's fields (e.g.
bumping a pool's Average Rate) and re-importing updates that entity - the
Reference Data sheet is treated as the authoritative full state for the
fields it exposes, so a field left blank on re-import clears it (Name is
the only always-required field; Resource Pools default to Internal type
and $0 rate if Type/Average Rate are blank or unparseable). Resource Pools
are processed before People during import so a Person row's "Resource
Pool" column can resolve against a pool added in the very same import
pass. Each table's data rows are located by matching its bold title text
in row 1 (not by fixed column numbers), so reordering the eight tables on
the sheet doesn't break import.

---

# 75. Excel Dropdowns

Use standard Excel dropdowns where helpful.

Implemented on the Resource Plan table: Workstream, Team, Pool, and Role
each have an in-cell List dropdown sourced live from the corresponding
Reference Data list (Workstreams, Teams, Resource Pools, Roles), so
values pick from the master list instead of being retyped/mistyped. The
Reference Data sheet's own People table also has a dropdown on its
"Resource Pool" column, sourced from the Resource Pools list.

Dropdowns are conveniences, not restrictions: validation uses the Warning
error style (not Stop), so a user typing a brand-new value not yet in the
list gets a warning prompt but can still keep it.

Each dropdown's source range on the Reference Data sheet extends well
past that list's current row count (rather than stopping exactly at the
last populated row), so a user who appends new rows to a Reference Data
list directly in Excel (e.g. a new Resource Pool or Team) sees those new
values in dependent dropdowns immediately, without needing to re-export.

---

# 76. Summary Worksheet

Provide useful standalone Excel reporting such as:

* Filled FTE by month
* Open FTE by month
* Total requirement by month
* Open demand by role
* Open demand by team
* Open demand by template
* Overallocated people
* Template timeline
* Rollout staffing

Summary cells are written as live Excel formulas (e.g. `SUMIFS` against the
Resource Plan sheet's Person/Team/Role/month columns), not static computed
values, so the Summary recalculates automatically if a user edits the
Resource Plan sheet directly in Excel.

Use normal Excel techniques where practical.

Users should be free to modify the Summary.

## Pool / Vendor Cost & Headcount Rollups

The Summary sheet also includes the following live-formula sections,
grouped by Resource Pool and, separately, by Vendor (pools sharing the same
Vendor rolled together):

```text
Pool Summary - Cost by Quarter
Pool Summary - Cost by Year
Pool Summary - Count by Quarter
Pool Summary - Count by Year
Vendor Summary - Cost by Quarter
Vendor Summary - Cost by Year
```

Each of these has three variants: **Allocated Only** (named Person rows),
**Demand Only** (open-demand rows), and **Both** (combined). Each is a grid
with one row per pool (or vendor) and one column per quarter/year, plus a
Total row summing all pools/vendors for that period.

Assumptions:

* Capacity is 2080 hours/year (2080/12 per month, 2080/4 per quarter).
* Cost = sum of a row's monthly FTE across the period, times per-month
  capacity hours, times the pool's Average Rate.
* Count treats 1.0 as a single FTE (e.g. two people at 0.5 FTE in the same
  period sum to a count of 1); a row's quarterly count is the average FTE
  across that quarter's months, and a yearly count is the average of its
  quarters.
* A filled row's effective pool is its Person's own Resource Pool; an
  open-demand row's effective pool is its own Pool column. Year figures are
  derived from the quarter figures (summed for cost, averaged for count)
  rather than recomputed from scratch.

All figures are formulas (`SUMIFS`/`VLOOKUP`/`SUM`/`AVERAGE`) referencing a
handful of hidden helper columns added to the end of the Resource Plan
sheet (Effective Pool/Vendor/Hourly Rate, plus one Cost and one Count
helper column per quarter) and the Reference Data sheet's People and
Resource Pools tables - never static snapshot values.

---

# 77. Custom Excel Analysis

A major acceptance requirement is:

> Users should be able to create most custom resource views directly from Resource Plan without joining multiple worksheets.

They should be able to analyze:

* Template
* Phase
* Workstream
* Team
* Role
* Person
* Month
* Allocation
* Open demand

from the primary table.

---

# 78. Excel Independence

Assume the person receiving the workbook may:

* Never use the application
* Not know the application exists
* Add worksheets
* Add formulas
* Add charts
* Add PivotTables
* Add rows
* Add columns
* Rename planning values
* Sort/filter
* Share the workbook

The workbook must remain useful as an independent planning artifact.

---

# 79. Invisible Technical Metadata

If technical metadata is useful for matching records during later import, it may be stored invisibly through:

* Very hidden worksheets
* Workbook custom properties
* Hidden mapping structures
* Other nonintrusive mechanisms

Normal users must never be responsible for maintaining it.

The importer must not rely exclusively on metadata remaining intact.

---

# 80. Resilient Excel Reading

When reading a workbook, prioritize:

1. Business-readable table contents
2. Table/column names
3. Existing master-data matching
4. Hidden metadata if available

Do not rely on:

* Absolute row numbers
* Fixed cell addresses
* Workbook users preserving invisible application structures

Reasonable Excel modifications should not make the workbook unusable.

---

# 81. Technical Export

Provide a separate advanced option:

`Technical Data Export`

This may expose normalized data and IDs.

It is intended for:

* Integration
* Troubleshooting
* Migration
* Technical analysis

It is not the default Excel experience.

---

# 82. Excel Round-Trip Acceptance Scenario

Application contains:

```text
Template 1
Inbound T1
Jane Smith
BA
Jan-Jun
1.0 FTE
```

Export shows:

| Template | Workstream | Team       | Role | Person | Jan | Feb | Mar | Apr | May | Jun |
| -------- | ---------- | ---------- | ---- | ------ | --: | --: | --: | --: | --: | --: |
| T1       | Inbound    | Inbound T1 | BA   | Jane   |   1 |   1 |   1 |   1 |   1 |   1 |

User changes:

```text
Apr = .5
May = .5
Jun = .5
```

On subsequent application ingestion, interpret:

```text
Jan-Mar = 1.0
Apr-Jun = .5
```

---

# 83. Excel Open Demand Scenario

Workbook contains:

| Template | Team       | Role | Person | Jan | Feb | Mar |
| -------- | ---------- | ---- | ------ | --: | --: | --: |
| T1       | Inbound T1 | BA   | Jane   |   1 |   1 |   1 |
| T1       | Inbound T1 | BA   | Mike   |  .5 |  .5 |  .5 |
| T1       | Inbound T1 | BA   |        |  .5 |  .5 |   1 |

Application interprets March as:

```text
Jane        1.0 filled
Mike        0.5 filled
Unassigned  1.0 open

Total Requirement = 2.5 FTE
Open Requirement  = 1.0 FTE
```

---

# 84. Planning Warnings

Implement nonblocking warnings for:

### Overallocation

```text
Jane is allocated at 125% during May.
```

### Assignment Outside Team Dates

```text
This assignment extends beyond the team's planned period.
```

### Assignment Outside Template Assignment

```text
This allocation extends beyond the team's Template 2 assignment.
```

### Availability

```text
Jane is unavailable for part of this period.
```

### Role Mismatch

```text
Jane's primary role is BA; this assignment uses Developer.
```

Allow the user to continue unless data would be structurally invalid.

---

# 85. Visual Indicators

Use restrained highlighting for:

```text
Normal
Warning
Critical
Inactive
Open Demand
```

Avoid excessive color.

Resource gaps and overallocation should be visually obvious.

---

# 86. Drill Down

Summary metrics should drill down.

Example:

```text
Business Analyst
May 2028
Open Demand: 4.5 FTE
```

Click:

```text
Template 2
    Inbound      1.0
    Outbound     2.0

Template 3
    Inventory    1.5
```

Then allow navigation to the Team or Resource Plan.

---

# 87. Timeline Service

Create a dedicated application-layer service.

Responsibilities include:

```text
MoveResourcePlanLine(...)
ResizeResourcePlanLineStart(...)
ResizeResourcePlanLineEnd(...)
CreateResourcePlanLine(...)

MoveTemplatePhase(...)
ResizeTemplatePhase(...)

MoveTeamTemplateAssignment(...)
ResizeTeamTemplateAssignment(...)

ValidateTimelineChange(...)
```

Do not manipulate EF entities directly from Gantt components.

---

# 88. Timeline Transaction Behavior

A drag/resize operation should be one logical transaction.

Process:

```text
1. User begins drag
2. UI previews dates
3. User releases
4. Validate
5. Persist
6. Recalculate affected capacity/gaps
7. Refresh
8. Add to undo history
```

If persistence fails, restore the previous position.

---

# 89. Application Services

Recommended services:

```text
ProgramService
ScenarioService
TemplateService
WorkstreamService
TeamService
PersonService
RoleService

ResourcePlanService
CapacityService
GapAnalysisService

TimelineService
LookupService

ExcelExportService
ExcelImportService
ExcelTransformationService
```

---

# 90. Excel Transformation Service

Create a dedicated service responsible for transforming between:

```text
Application date-range records
↔
Monthly Excel matrix
```

Responsibilities:

* Expand ranges into months
* Consolidate identical adjacent months
* Interpret blank cells
* Interpret blank Person as demand
* Resolve new master data
* Resolve team-template relationships
* Validate workbook contents
* Detect ambiguous changes
* Preserve reasonable user edits

This logic must be independently unit tested.

---

# 91. Date Calculation Service

Provide reusable date-overlap logic.

Example:

```csharp
bool Overlaps(
    DateOnly start1,
    DateOnly end1,
    DateOnly start2,
    DateOnly end2);
```

Default resource planning uses whole-month treatment.

Allow future configuration for working-day prorating.

---

# 92. Planning Settings

Settings should include:

```text
Default planning granularity = Monthly

Default person capacity = 1.0 FTE

Fiscal year starting month

Default program date range

Show inactive records

Warn on overallocation

Timeline snapping = Month
```

## Danger Zone: Clear All Data

The Settings page includes a "Clear All Data" action (guarded by a typed
confirmation phrase plus a JS confirm dialog) that permanently deletes
every Team, Person, Role, Template, Template Phase, Workstream, Focus
Area, Site, Resource Pool, Team-Template Assignment, Team Role, Team-Site
Assignment, Resource Plan Line, Planning Scenario, and Audit Entry for the
program - resetting the app to a fresh, empty state without requiring a
full application restart or re-running migrations. The single Program row
itself (its Name/Description/planning horizon) is preserved, since the
rest of the app assumes exactly one Program always exists, and a brand
new blank "Baseline" Planning Scenario is created immediately afterward,
since most pages assume at least one baseline scenario exists. Users are
directed to take a Backup (section 95) first, since this action cannot be
undone.

---

# 93. Performance Requirements

The system should comfortably handle approximately:

```text
5+ years
60+ planning months
100+ teams
1,000+ people
10,000+ resource plan ranges
Multiple simultaneous templates
Multiple rollout teams
```

Do not load the entire database into browser memory unnecessarily.

Perform aggregation server-side where appropriate.

---

# 94. Audit History

Provide lightweight audit tracking for important planning changes.

Record:

```text
Entity Type
Entity ID
Change Type
ChangedUtc
Old Value
New Value
```

At minimum track:

* Resource planning lines
* Team-template dates
* Template phase dates
* Team dates

---

# 95. Backup

For SQLite deployments provide:

```text
Backup Database
Restore Backup
```

Restore requires confirmation.

---

# 96. Required Automated Tests

## Resource Planning

Test:

* Named allocation
* Open demand
* Partial FTE
* Multiple assignments
* Overallocation
* Capacity less than 1 FTE

## Dates

Test:

* Month boundaries
* Year boundaries
* Multi-year assignments
* Overlapping ranges

## Team ↔ Template

Test:

* One team / one template
* One team / multiple templates
* Overlapping template assignments
* Multiple teams / same template

## Gantt

Test:

* Move
* Resize start
* Resize end
* Create
* Undo
* Validation

## Excel

Test:

* Five-year export
* Monthly expansion
* Blank cells
* Named person interpretation
* Blank person interpretation
* Contiguous-range consolidation
* New Person
* New Team
* New Role
* New Workstream
* Shared Team
* Phase optionality
* User-added worksheets/columns
* Column reordering
* Workbook without hidden metadata

---

# 97. Development Seed Data

Create realistic sample data.

Program:

```text
WMS Modernization
```

Templates:

```text
Template 1
Template 2
Template 3
Template 4
```

Workstreams:

```text
Inbound
Outbound
Inventory
Automation
Integrations
Reporting
```

Teams:

```text
T1 Inbound Build
T1 Outbound Build
T2 Inbound Build
T2 Outbound Build
Integration Team
Automation Team
SIT Team
Rollout Team A
Rollout Team B
Rollout Team C
```

Include:

* Approximately 30 people
* Approximately 10 roles
* Shared teams
* Overlapping templates
* Open demand
* Partial allocations
* Intentionally overallocated people
* Parallel rollout teams

The application should demonstrate useful behavior immediately after startup.

---

# 98. MVP Development Sequence

## Phase 1 — Foundation

Build:

* Solution
* Database
* Domain entities
* Migrations
* Seed data
* Navigation
* Lookup creation
* People
* Teams
* Roles
* Workstreams
* Templates
* Template phases
* Team-template relationships

## Phase 2 — Resource Planning

Build:

* ResourcePlanLine
* Resource Plan Grid
* Monthly representation
* Open demand
* Named allocation
* Capacity calculations
* Gap calculations
* Copy/paste
* Autosave

## Phase 3 — Visual Planning

Build:

* Template Gantt
* Phase drag/resize
* Team-template Gantt
* Resource Gantt
* Drag/resize/create
* Timeline validation
* Undo

Do not defer Gantt functionality until the end.

## Phase 4 — Excel

Build:

* Five-year Resource Plan timeline
* Template Plan overlay (merged onto the Resource Plan sheet)
* Reference Data
* Summary
* Monthly expansion/consolidation
* Workbook reading
* Standalone Excel UX
* Technical export

## Phase 5 — Reporting

Build:

* Dashboard
* Gap reporting
* Capacity reporting
* Template reporting
* Rollout reporting
* Drill-down

## Phase 6 — Advanced

Build:

* Scenarios
* Scenario duplication
* Audit history
* Advanced bulk editing
* Improved visual planning

---

# 99. Critical Acceptance Scenarios

## A. Three Inbound Teams

Create:

```text
Inbound T1 → Template 1
Inbound T2 → Template 2
Inbound T3 → Template 3
```

Selecting Template 2 shows:

* Inbound T2
* Its roles
* Assigned people
* Open demand
* Assignment periods
* FTE allocations

---

## B. Shared Integration Team

Integration Team supports:

```text
Template 1
Template 2
Template 3
```

during overlapping periods.

The application correctly represents all relationships.

---

## C. Cross-Template Overallocation

Jane supports:

```text
Template 1 / Inbound = .75 FTE
Template 2 / Integration = .50 FTE
```

during May.

Application reports:

```text
1.25 FTE
125% allocated
25% over capacity
```

---

## D. Open Demand

Inbound T2 requires:

```text
Jane = 1.0
Mike = .5
Open = 1.0
```

Application reports:

```text
Filled = 1.5
Open = 1.0
Total Need = 2.5
```

---

## E. Template Gantt

All four Templates appear across the program timeline.

Users can:

* Expand phases
* Move phases
* Resize phases
* See overlaps

---

## F. Resource Gantt

User drags Jane's allocation from:

```text
Jan-Mar
```

to:

```text
Apr-Jun
```

The application updates the underlying date range while preserving FTE.

---

## G. Five-Year Excel

Workbook displays:

```text
Template | Phase | Workstream | Team | Role | Person | Jan-27 ... Dec-31
```

with approximately 60 monthly planning columns.

The first planning columns remain frozen while horizontally scrolling.

---

## H. Excel Open Demand

A blank Person with monthly FTE values is interpreted as open demand.

---

## I. Excel New Team

A workbook user adds:

```text
Template 3
Inbound
New T3 Inbound Team
BA
Sally Smith
```

with:

```text
Feb = 1
Mar = 1
Apr = 1
```

The application can resolve/create the new entities and represent Sally's allocation.

---

## J. Excel Independence

A user receives the workbook with no knowledge of the application.

They can successfully:

* Understand it
* Edit it
* Filter it
* Add resources
* Add demand
* Build PivotTables
* Build charts
* Share it
* Continue using it as a standalone planning workbook

---

# 100. Definition of Done

The initial usable application is complete when a planner can:

1. Maintain four or more templates.
2. Maintain template phases.
3. See templates and phases in an editable Gantt.
4. Create any number of teams.
5. Associate teams with multiple templates.
6. Have overlapping team-template assignments.
7. Create people and roles inline.
8. Create open demand.
9. Assign named people.
10. Allocate people using FTE.
11. Plan resources monthly across five years.
12. Edit resource planning through an Excel-like grid.
13. Edit the same resource planning through Gantt bars.
14. Drag allocations.
15. Resize allocations.
16. Drag template phases.
17. Resize template phases.
18. Modify team-template relationships visually.
19. Identify open resource demand.
20. Identify overallocated people.
21. Filter by template, workstream, team, role, person, and date.
22. Analyze multiple simultaneous rollout teams.
23. Export a standalone five-year Excel resource timeline.
24. Represent open demand in Excel without requiring Record Type.
25. Represent named allocations in the same Excel structure.
26. Read reasonable edits made to that workbook.
27. Allow the workbook to remain useful independently of the application.
28. Operate effectively over the complete five-year program horizon.

---

# 101. Final Product Philosophy

When making implementation decisions, follow these rules.

### Minimize configuration

Prefer:

```text
Type Team → Create → Continue
```

over:

```text
Administration → Teams → Add → Save → Return → Select
```

### Support structured and visual planning equally

Users should be able to:

```text
Type exact dates
```

or:

```text
Drag the bar
```

Both modify the same underlying information.

### Treat FTE as the common resource language

Use:

```text
0.5 FTE
```

consistently for both open and filled resource needs.

Translate to percentages where useful in the UI.

### Keep Excel human-first

The standard Excel workbook is a standalone planning tool.

Application compatibility must never become the spreadsheet user's problem.

### Keep Excel simple

The primary resource view is:

```text
Template
Phase
Workstream
Team
Role
Person
Jan-27
Feb-27
Mar-27
...
```

not a collection of normalized database exports.

### Make gaps explicit

```text
Person populated = filled resource need
Person blank = open resource need
```

### Preserve flexibility

A team can support multiple templates.

A person can support multiple teams.

Templates can overlap.

Rollout teams can operate simultaneously.

Planning data can be incomplete while the plan is evolving.

### Optimize for the planner

When forced to choose between database elegance and planning usability:

> Keep the internal model technically sound, but optimize the user experience for the person actually building and changing the resource plan.
