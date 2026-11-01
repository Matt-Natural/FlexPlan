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

## Put it online (so it runs without your PC)
FlexPlan includes a `Dockerfile` and `render.yaml`. In short:
1. Create a free PostgreSQL database (for example at https://neon.tech) and copy its connection string.
2. On https://render.com choose **New + > Web Service**, connect this GitHub repository, set the runtime to **Docker**.
3. Add the environment variable `DATABASE_URL` = your connection string, then deploy.
4. Open the `https://<name>.onrender.com` address Render shows.

On the free plan the site goes to sleep when unused and takes about a minute to wake up. Users are signed out
when it restarts. With PostgreSQL the tables are created automatically on first start; the migrations in
`Migrations/` apply to the local SQLite database.
