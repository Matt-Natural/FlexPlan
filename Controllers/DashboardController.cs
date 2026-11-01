using FlexPlan.Data;
using FlexPlan.Models;
using FlexPlan.Services;
using FlexPlan.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FlexPlan.Controllers;

[Authorize]
public class DashboardController(AppDbContext db, SchedulerService scheduler) : BaseController
{
    public async Task<IActionResult> Index()
    {
        var tasks = await db.Tasks.Where(t => t.UserId == UserId).ToListAsync();
        var now = DateTime.Now;
        var today = now.Date;
        var open = tasks.Where(t => t.Status != ProgressStatus.Completed).ToList();
        var todayItems = await scheduler.GetBlocksAsync(UserId, today, today.AddDays(1), includeCompleted: false);
        var done = tasks.Count(t => t.Status == ProgressStatus.Completed);

        var first = (User.Identity?.Name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

        return View(new DashboardViewModel
        {
            UserName = first,
            Greeting = now.Hour < 12 ? "Good morning" : now.Hour < 18 ? "Good afternoon" : "Good evening",
            Total = tasks.Count,
            Completed = done,
            CompletionPercent = tasks.Count == 0 ? 0 : (int)Math.Round(100.0 * done / tasks.Count),
            TodayCount = todayItems.Where(b => !b.Task.IsAppointment).Select(b => b.Task.Id).Distinct().Count(),
            AppointmentsToday = todayItems.Where(b => b.Task.IsAppointment).Select(b => b.Task.Id).Distinct().Count(),
            OverdueCount = open.Count(t => t.DueDate < now),
            Today = todayItems,
            Upcoming = open.Where(t => t.DueDate >= now && t.DueDate < today.AddDays(8))
                .OrderBy(t => t.DueDate).ThenByDescending(t => t.Priority).Take(6).ToList(),
            Overdue = open.Where(t => t.DueDate < now).OrderBy(t => t.DueDate).ToList(),
            ConflictCount = (await scheduler.GetConflictsAsync(UserId)).Count
        });
    }
}
