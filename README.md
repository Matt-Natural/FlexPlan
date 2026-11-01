# FlexPlan – Adaptive Scheduling and Personalized Task Management

![Build](../../actions/workflows/build.yml/badge.svg)

ASP.NET Core 8 MVC (C#) + Entity Framework Core + SQLite.

## Run
```
dotnet restore
dotnet run
```
Open http://localhost:5000 (or the URL printed in the terminal). The database is created and upgraded
automatically on startup using EF Core migrations.

> **Coming from an older version without migrations?** FlexPlan renames your old `flexplan.db` to
> `flexplan.db.backup-<date>` on first start and creates a fresh database.

## Features
- Accounts, profile, settings (account, scheduling, appearance, notifications, database/CSV export)
- Tasks and appointments with priority, category, reminders, status tracking
- **Adaptive scheduling** of flexible tasks around fixed events
- **Split long tasks** into shorter sessions (max session length is a setting)
- **Breaks and buffers**: buffer around every item + optional 12-1 PM lunch break
- **Subtasks**: checklist per task; checking them all completes the task
- Conflict detection with suggested fixes (move / swap / keep both)
- Calendar (month, week, day) and a day timeline
- Mobile-friendly layout: bottom tab bar, card-style task list, large tap targets

## Database migrations
Migrations live in `Migrations/`. After you change a model in `Models/Entities.cs`:
```
dotnet tool install --global dotnet-ef     # once
dotnet ef migrations add NameOfChange
dotnet run                                 # applies it on startup
```
To undo the last migration that has not been applied elsewhere: `dotnet ef migrations remove`.

## Key code
- `Services/SchedulerService.cs` – scheduling, splitting, buffers, conflicts, suggestions
- `Data/DatabaseBootstrapper.cs` – migrate on startup, back up pre-migration databases
- `Controllers/`, `Views/`, `wwwroot/css/site.css`
