# Excel Export Changes
## Resource Plan Sheet
- Add black gridlines to the entirety of the resource plan table including months
- Set unique cell background color for each year in the resource plan months
- Add column grouping for the years in the resource plan table. For example all months in a given year should be collapsible
- For helper columns in the resource plan sheet; Change formula in "effective pool" helper column to look up the the pool by person, but if the person or pool doesn't exist on the reference data sheet it should fallback to pool on the resource plan. If neither are specified show a pool look up error.

# Excel Import Changes
## Dynamic column detection
- When importing resource plan assume the location of columns can change for; Workstream, Team, Pool, Role, Person, Template, Phase, Notes.
- Always determine position of columns before importing data
- When importing if a person is specified with no pool in the resource plan table, however they have a pool specified in the reference data then ensure that pool is linked to that person in the database. We should always check both the raw resource plan table as well as reference data. If data exists in one place but not the other we use the value where the data exists for the import update. If values exist in both and conflict then we show a validation error.

# General Changes
## Audit Log
- When doing excel import/export these actions should show in the audit log
- Taking DB backup should show in the audit log
- When recording audit log events capture the environments user name as well as machine name and show in the logs for who performed the action

## Resource Gantt
- Add checkbox that allows us to show over allocated only
- Add checkbox that allows us to show under allocated only
- When there are two assignements for a single person that overlap the bars must be shown on separate rows so the user can see the different assignments clearly

## Backup
- When taking a backup allow the user to give the back a description

## Global Timeline
- Add one more level of group by. There should be 3 total
- Add checkbox for over allocation only
- When a person is over-allocated highlight the over allocation with a red color. Any allocations contributing to the overallocation should be shown in red
- When there are two allocations for a single person that overlap the bars must be shown on separate rows so the user can see the different assignments clearly.
- When there are two allocations that "book end" and the end of the first is the start of the second, you cannot see the end of the first nor drag without moving the second. Any book end allocations must do the following
	- The earlier bars should render last so the draggable handle is always visible
	- Any bars we show back to back should be shown in different shades of the same color so we can distinguish them.

## Reports
- Don't show all reports on a single page. Going to "Reports" should show you a list of reports you can views
- All reports will have export to excel option
- Reports that should be supported
	- Gap Analysis (Open Demand)
		- Show in a timeline format where rows are Pool and Role. Then columns are months over the timeline with total counts for Pool/Role combinations
		- Show raw data table below
	- Capacity Reports
		- Show in a timeline format where rows are people. Then columns are months over the timeline with capacity shown as values. Overallocations are red and unders are yellow.
	- Vendor Counts Report
		- Show in a timeline format where rows are vendor. Columns are months over the timeline with total counts shown for each month.
		- Should check every allocation for the vendor in that period via pool associations to both allocations and demand.
		- Option to show by quarter or month.
		- Option to show allocation only, demand only, or both
		- Option to show internal vs external.
		- Option to filter by vendor
	- Vendor Cost Report
		- Show in a timeline format where rows are vendor. Columns are months over the timeline with total cost shown for each month.
		- Should check every allocation for the vendor in that period via pool associations to both allocations and demand.
		- Option to show by quarter or month.
		- Option to show allocation only, demand only, or both
		- Option to show internal vs external.
		- Option to filter by vendor
	- Pool Count Report
		- Show in a timeline format where rows are pools. Columns are months over the timeline with total counts shown for each month.
		- Should check every allocation for the pool in that period for both allocations and demand.
		- Option to show by quarter or month.
		- Option to show allocation only, demand only, or both
		- Option to show internal vs external.
		- Option to filter by vendor
	- Pool Cost Report
		- Show in a timeline format where rows are pools. Columns are months over the timeline with total cost shown for each month.
		- Should check every allocation for the pool in that period for both allocations and demand.
		- Option to show by quarter or month.
		- Option to show allocation only, demand only, or both
		- Option to show internal vs external.
		- Option to filter by vendor
		
## Application Wide
- When changing screens show loading indicator as some screens take a while to loading
- All gantt views need the ability zoom out to quarter granularity. Ensure any drag/move functionality is preserved
- For the templates add a "Sub-Phase" component that allows us to break down phases more fine grained. For example we break a "Test" phase into Unit, Functional, SIT, UAT, etc.

## Templates
- Start and end columns don't show the dates. It shows "mm/dd/yyyy" even though we can see start/end via the bars on the gantt views for template.

