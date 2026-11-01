using FlexPlan.Data;
using FlexPlan.Services;
using FlexPlan.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FlexPlan.Controllers;

[Authorize]
public class ScheduleController(AppDbContext db, SchedulerService scheduler) : BaseController
{
    // Day timeline.
    public async Task<IActionResult> Index(DateTime? date)
    {
        var day = (date ?? DateTime.Today).Date;
        var blocks = await scheduler.GetBlocksAsync(UserId, day, day.AddDays(1), includeCompleted: false);

        return View(new ScheduleViewModel
        {
            Date = day,
            Blocks = blocks.Where(b => b.End.HasValue).ToList(),
            DueOnly = blocks.Where(b => !b.End.HasValue).ToList(),
            ConflictCount = (await scheduler.GetConflictsAsync(UserId)).Count
        });
    }

    // Adaptive scheduling: conflict alert with suggested adjustments.
    [HttpGet]
    public async Task<IActionResult> Conflicts() => View(await scheduler.GetAdviceAsync(UserId));

    [HttpPost]
    public async Task<IActionResult> Apply(int firstId, int secondId, string choice)
    {
        var (ok, message) = await scheduler.ApplyAsync(UserId, firstId, secondId, choice);
        TempData[ok ? "Success" : "Warning"] = message;
        return RedirectToAction(nameof(Conflicts));
    }
}
