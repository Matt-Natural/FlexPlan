using FlexPlan.Services;
using FlexPlan.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlexPlan.Controllers;

[Authorize]
public class CalendarController(SchedulerService scheduler) : BaseController
{
    public async Task<IActionResult> Index(string mode = "month", DateTime? date = null)
    {
        mode = mode is "day" or "week" or "month" ? mode : "month";
        var d = (date ?? DateTime.Today).Date;
        var selected = d;

        DateTime start, end, prev, next;
        string title;

        switch (mode)
        {
            case "day":
                start = d; end = d.AddDays(1);
                prev = d.AddDays(-1); next = d.AddDays(1);
                title = d.ToString("dddd, MMMM d, yyyy");
                break;
            case "week":
                start = d.AddDays(-(int)d.DayOfWeek); end = start.AddDays(7);
                prev = start.AddDays(-7); next = start.AddDays(7);
                title = $"{start:MMM d} – {end.AddDays(-1):MMM d, yyyy}";
                break;
            default:
                var first = new DateTime(d.Year, d.Month, 1);
                start = first.AddDays(-(int)first.DayOfWeek); // grid starts on Sunday
                end = start.AddDays(42);
                prev = first.AddMonths(-1); next = first.AddMonths(1);
                title = first.ToString("MMMM yyyy");
                d = first;
                break;
        }

        return View(new CalendarViewModel
        {
            Mode = mode, Date = d, Selected = selected, RangeStart = start, RangeEnd = end,
            Prev = prev, Next = next, Title = title,
            Items = await scheduler.GetBlocksAsync(UserId, start, end, includeCompleted: true)
        });
    }
}
