This repository implements the WMS Resource Planning application.

Authoritative product requirements are in /docs/SPEC.md.

Before implementing a feature:
1. Read the relevant sections of SPEC.md.
2. Review implementation-status.md.
3. Inspect existing code before changing architecture.
4. Do not introduce requirements that conflict with the specification.

Technology:
- .NET 9
- ASP.NET Core
- Blazor Web App
- EF Core
- SQLite initially
- ClosedXML
- Nullable reference types enabled
- async/await
- dependency injection
- automated tests required

Important architectural concepts:
- ResourcePlanLine is the core resource-planning entity.
- Person == null means open demand.
- Person != null means named allocation / filled demand.
- Teams and Templates are many-to-many through TeamTemplateAssignment.
- Resource planning must support at least 60 months.
- Grid and Gantt edit the same underlying records.
- Excel uses the standalone monthly Resource Plan matrix defined in the spec.

Do not replace these concepts without explicit instruction.