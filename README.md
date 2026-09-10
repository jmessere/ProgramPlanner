# WMS Resource Planning Application

A resource-planning application for a large, multi-year WMS modernization
program: templates, phases, workstreams, teams, roles, people, open demand,
named allocations, capacity/gap analysis, and Excel export/import.

See `/docs/SPEC.md` for the full product specification, `/docs/implementation-status.md`
for current build status, and `/docs/implementation-decisions.md` for
notable implementation decisions.

## Prerequisites

* [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
* (Optional, for schema changes) `dotnet-ef` global tool:
  `dotnet tool install --global dotnet-ef --version 9.0.9`

## Restore packages

```powershell
dotnet restore
```

## Build

```powershell
dotnet build
```

## Database

SQLite is used for local development. The database file lives at:

```text
src/WmsResourcePlanner.Web/App_Data/wmsresourceplanner.db
```

This path is excluded from source control (see `.gitignore`). On first run,
the application automatically:

1. Applies EF Core migrations (`db.Database.Migrate()`), creating the
   database file/schema if it does not exist.
2. Seeds realistic development data (`SeedData.SeedAsync`) if the database
   is empty - 4 overlapping templates with lifecycle phases, 9 workstreams,
   11 teams (including a shared Integration Team and 4 Rollout teams), 11
   roles, 30 people, and a resource plan containing named allocations, open
   demand, partial FTE, and two intentional overallocations.

To reset the database, delete the `App_Data` folder contents and re-run the
application.

To create a new migration after changing the domain model:

```powershell
dotnet-ef migrations add <MigrationName> `
  --project src/WmsResourcePlanner.Infrastructure `
  --startup-project src/WmsResourcePlanner.Web `
  -o Data/Migrations
```

## Run

```powershell
cd src/WmsResourcePlanner.Web
dotnet run
```

Then browse to the URL printed in the console (typically
`http://localhost:5000` or similar; see `Properties/launchSettings.json`).

## Run tests

```powershell
dotnet test
```

## Solution layout

```text
src/
    WmsResourcePlanner.Domain/          Entities (Program, Template, Team,
                                         ResourcePlanLine, ...)
    WmsResourcePlanner.Application/     Services, DTOs, calculations
                                         (transformation engine, capacity,
                                         gap analysis, lookups, scenarios)
    WmsResourcePlanner.Infrastructure/  EF Core DbContext, migrations, seed
                                         data, Excel export (ClosedXML)
    WmsResourcePlanner.Web/             Blazor Web App (Server interactivity)
tests/
    WmsResourcePlanner.Tests/           xUnit tests (transformation engine,
                                         capacity, gap analysis, Team↔Template
                                         relationship, Excel export, scenarios)
```

## Current functionality

See `/docs/implementation-status.md` for a full, current accounting of what
is implemented, what remains, and known issues. In summary, master data
management, the monthly Resource Plan grid, capacity/gap reporting, Excel
export, and scenario duplication are implemented and tested end-to-end.
Gantt/timeline drag-and-drop editing, Excel import, and undo are not yet
implemented.
