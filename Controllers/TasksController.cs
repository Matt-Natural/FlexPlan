using FlexPlan.Data;
using FlexPlan.Models;
using FlexPlan.Services;
using FlexPlan.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FlexPlan.Controllers;

[Authorize]
public class TasksController(AppDbContext db, SchedulerService scheduler) : BaseController
{
    // ---------- List: tabs (All / Pending / In Progress / Completed / Overdue), search, sort ----------
    public async Task<IActionResult> Index(string? q, string? status, string sort = "due")
    {
        var now = DateTime.Now;
        IQueryable<TaskItem> query = db.Tasks
            .Include(t => t.Sessions)
            .Include(t => t.Subtasks)
            .Where(t => t.UserId == UserId);

        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(t => t.Title.Contains(q) || (t.Description != null && t.Description.Contains(q)));

        if (status == "Overdue")
            query = query.Where(t => t.Status != ProgressStatus.Completed && t.DueDate < now);
        else if (Enum.TryParse<ProgressStatus>(status, out var s))
            query = query.Where(t => t.Status == s);

        query = sort switch
        {
            "priority" => query.OrderByDescending(t => t.Priority).ThenBy(t => t.DueDate),
            "created" => query.OrderByDescending(t => t.CreatedAt),
            _ => query.OrderBy(t => t.Status).ThenBy(t => t.DueDate)
        };

        var conflicts = await scheduler.GetConflictsAsync(UserId);
        ViewBag.ConflictIds = conflicts.SelectMany(c => new[] { c.First.Id, c.Second.Id }).ToHashSet();
        ViewBag.Q = q; ViewBag.Status = status ?? ""; ViewBag.Sort = sort;

        return View(await query.ToListAsync());
    }

    // ---------- Create (task or appointment) ----------
    [HttpGet]
    public IActionResult Create(string? type)
    {
        var vm = new TaskFormViewModel { DueDate = DateTime.Today.AddDays(1).AddHours(17) };
        if (type == "appointment")
        {
            var start = DateTime.Today.AddHours(DateTime.Now.Hour + 1);
            vm.IsAppointment = true;
            vm.StartTime = start;
            vm.EndTime = start.AddHours(1);
            vm.DueDate = null;
            vm.Category = "Work";
        }
        return View(vm);
    }

    [HttpPost]
    public async Task<IActionResult> Create(TaskFormViewModel vm)
    {
        ValidateSchedule(vm);
        if (!ModelState.IsValid) return View(vm);

        var task = new TaskItem { UserId = UserId, CreatedAt = DateTime.Now };
        Apply(vm, task);

        var order = 0;
        foreach (var line in (vm.SubtasksText ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(30))
        {
            var title = line.Length > 120 ? line[..120] : line;
            task.Subtasks.Add(new Subtask { Title = title, SortOrder = order++ });
        }

        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        await AfterChangeAsync(vm.IsAppointment ? "Appointment added." : "Task added.");
        return RedirectToAction(nameof(Index));
    }

    // ---------- Edit ----------
    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var t = await FindAsync(id, full: true);
        if (t is null) return NotFound();

        return View(new TaskFormViewModel
        {
            Id = t.Id, Title = t.Title, Description = t.Description, Category = t.Category,
            DueDate = t.IsAppointment ? null : t.DueDate, StartTime = t.StartTime, EndTime = t.EndTime,
            Priority = t.Priority, Status = t.Status, EstimatedMinutes = t.EstimatedMinutes,
            IsFlexible = t.IsFlexible, AllowSplit = t.AllowSplit, IsAppointment = t.IsAppointment,
            HasReminder = t.HasReminder,
            Subtasks = t.Subtasks.OrderBy(s => s.SortOrder).ToList(),
            Sessions = t.Sessions.OrderBy(s => s.StartTime).ToList()
        });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, TaskFormViewModel vm)
    {
        var task = await FindAsync(id, full: true);
        if (task is null) return NotFound();

        ValidateSchedule(vm);
        if (!ModelState.IsValid)
        {
            vm.Id = id;
            vm.Subtasks = task.Subtasks.OrderBy(s => s.SortOrder).ToList();
            vm.Sessions = task.Sessions.OrderBy(s => s.StartTime).ToList();
            return View(vm);
        }

        Apply(vm, task);
        await db.SaveChangesAsync();

        await AfterChangeAsync("Changes saved. Your schedule was updated.");
        return RedirectToAction(nameof(Index));
    }

    // ---------- Delete ----------
    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        var task = await FindAsync(id);
        if (task is null) return NotFound();

        db.Tasks.Remove(task);
        await db.SaveChangesAsync();

        await AfterChangeAsync("Deleted.");
        return RedirectToAction(nameof(Index));
    }

    // ---------- Subtasks ----------
    [HttpPost]
    public async Task<IActionResult> AddSubtask(int id, string? title)
    {
        var task = await FindAsync(id, full: true);
        if (task is null) return NotFound();

        title = (title ?? "").Trim();
        if (title.Length is 0 or > 120)
            TempData["Warning"] = "Enter a subtask between 1 and 120 characters.";
        else if (task.Subtasks.Count >= 30)
            TempData["Warning"] = "A task can have up to 30 subtasks.";
        else
        {
            task.Subtasks.Add(new Subtask
            {
                Title = title,
                SortOrder = task.Subtasks.Count == 0 ? 0 : task.Subtasks.Max(s => s.SortOrder) + 1
            });
            if (task.Status == ProgressStatus.Completed) task.Status = ProgressStatus.InProgress;
            await db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> ToggleSubtask(int id, string? returnTo)
    {
        var sub = await db.Subtasks
            .Include(s => s.TaskItem).ThenInclude(t => t!.Subtasks)
            .FirstOrDefaultAsync(s => s.Id == id && s.TaskItem!.UserId == UserId);
        if (sub is null) return NotFound();

        var task = sub.TaskItem!;
        sub.IsDone = !sub.IsDone;

        // Keep the task's status in step with its checklist.
        var before = task.Status;
        var done = task.Subtasks.Count(s => s.IsDone);
        if (done == task.Subtasks.Count) task.Status = ProgressStatus.Completed;
        else if (done > 0 && task.Status != ProgressStatus.InProgress) task.Status = ProgressStatus.InProgress;
        else if (done == 0 && task.Status == ProgressStatus.Completed) task.Status = ProgressStatus.Pending;
        await db.SaveChangesAsync();

        if (task.Status != before)
            await AfterChangeAsync(task.Status == ProgressStatus.Completed
                ? "All subtasks done – task marked as completed."
                : "Task status updated from its subtasks.");

        return !string.IsNullOrEmpty(returnTo) && Url.IsLocalUrl(returnTo)
            ? LocalRedirect(returnTo)
            : RedirectToAction(nameof(Edit), new { id = task.Id });
    }

    [HttpPost]
    public async Task<IActionResult> DeleteSubtask(int id)
    {
        var sub = await db.Subtasks.Include(s => s.TaskItem)
            .FirstOrDefaultAsync(s => s.Id == id && s.TaskItem!.UserId == UserId);
        if (sub is null) return NotFound();

        var taskId = sub.TaskItemId;
        db.Subtasks.Remove(sub);
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Edit), new { id = taskId });
    }

    // ---------- Progress tracking ----------
    [HttpPost]
    public async Task<IActionResult> SetStatus(int id, ProgressStatus status, string? returnTo)
    {
        var task = await FindAsync(id);
        if (task is null) return NotFound();

        task.Status = status;
        await db.SaveChangesAsync();

        await AfterChangeAsync(status == ProgressStatus.Completed ? "Marked as done." : "Status updated.");
        return Back(returnTo);
    }

    // ---------- Adaptive scheduling (manual trigger) ----------
    [HttpPost]
    public async Task<IActionResult> AutoSchedule(string? returnTo)
    {
        await AfterChangeAsync("Schedule re-optimized.");
        return Back(returnTo);
    }

    // ---------- Reminders (polled by the page header) ----------
    [HttpGet]
    public async Task<IActionResult> Reminders()
    {
        var user = await db.Users.FindAsync(UserId);
        if (user is null || !user.RemindersEnabled)
            return Json(new { desktop = false, items = Array.Empty<object>() });

        var now = DateTime.Now;
        var horizon = now.AddMinutes(user.ReminderLeadMinutes);
        var soon = now.AddMinutes(Math.Min(user.ReminderLeadMinutes, 60));

        var tasks = await db.Tasks
            .Include(t => t.Sessions)
            .Where(t => t.UserId == UserId && t.HasReminder && t.Status != ProgressStatus.Completed
                        && (t.DueDate < horizon
                            || (t.StartTime != null && t.StartTime > now && t.StartTime < horizon)
                            || t.Sessions.Any(s => s.StartTime > now && s.StartTime < horizon)))
            .OrderBy(t => t.DueDate)
            .Take(20)
            .ToListAsync();

        DateTime? NextStart(TaskItem t) =>
            t.Sessions.Where(s => s.StartTime > now).Select(s => (DateTime?)s.StartTime).OrderBy(x => x).FirstOrDefault()
            ?? (t.StartTime > now ? t.StartTime : null);

        var items = tasks.Select(t =>
        {
            var next = NextStart(t);
            var startsInWindow = next.HasValue && next.Value < horizon;
            return new
            {
                id = t.Id,
                title = t.Title,
                overdue = t.DueDate < now,
                startsSoon = next.HasValue ? next.Value < soon : t.DueDate > now && t.DueDate < soon,
                message = startsInWindow
                    ? $"Starts at {next!.Value:h:mm tt}"
                    : t.DueDate < now ? $"Overdue since {t.DueDate:MMM d, h:mm tt}" : $"Due {t.DueDate:MMM d, h:mm tt}"
            };
        });
        return Json(new { desktop = user.DesktopNotifications, items });
    }

    // ---------- Helpers ----------
    private IActionResult Back(string? returnTo) =>
        !string.IsNullOrEmpty(returnTo) && Url.IsLocalUrl(returnTo) ? LocalRedirect(returnTo) : RedirectToAction(nameof(Index));

    private Task<TaskItem?> FindAsync(int id, bool full = false)
    {
        IQueryable<TaskItem> q = db.Tasks;
        if (full) q = q.Include(t => t.Subtasks).Include(t => t.Sessions);
        return q.FirstOrDefaultAsync(t => t.Id == id && t.UserId == UserId);
    }

    private void ValidateSchedule(TaskFormViewModel vm)
    {
        if (!Choices.Categories.Contains(vm.Category))
            ModelState.AddModelError(nameof(vm.Category), "Choose a category from the list.");

        if (vm.IsAppointment)
        {
            vm.IsFlexible = false;
            vm.AllowSplit = false;
            ModelState.Remove(nameof(vm.DueDate));
            if (!vm.StartTime.HasValue) ModelState.AddModelError(nameof(vm.StartTime), "Choose a start time.");
            if (!vm.EndTime.HasValue) ModelState.AddModelError(nameof(vm.EndTime), "Choose an end time.");
            if (vm.StartTime.HasValue && vm.EndTime.HasValue && vm.EndTime <= vm.StartTime)
                ModelState.AddModelError(nameof(vm.EndTime), "End time must be after the start time.");
            return;
        }

        if (!vm.DueDate.HasValue) ModelState.AddModelError(nameof(vm.DueDate), "Choose a due date and time.");

        if (vm.IsFlexible)
        {
            if (vm.EstimatedMinutes < 5)
                ModelState.AddModelError(nameof(vm.EstimatedMinutes), "Flexible tasks need a duration of at least 5 minutes.");
            vm.StartTime = null; vm.EndTime = null;
            return;
        }

        vm.AllowSplit = false;
        if (vm.StartTime.HasValue != vm.EndTime.HasValue)
            ModelState.AddModelError(nameof(vm.EndTime), "Enter both a start and an end time, or leave both empty.");
        else if (vm.StartTime.HasValue && vm.EndTime <= vm.StartTime)
            ModelState.AddModelError(nameof(vm.EndTime), "End time must be after the start time.");
    }

    private static void Apply(TaskFormViewModel vm, TaskItem task)
    {
        task.Title = vm.Title.Trim();
        task.Description = vm.Description?.Trim();
        task.Category = vm.Category;
        task.Priority = vm.Priority;
        task.Status = vm.Status;
        task.HasReminder = vm.HasReminder;
        task.IsAppointment = vm.IsAppointment;
        task.IsFlexible = vm.IsFlexible && !vm.IsAppointment;
        task.AllowSplit = task.IsFlexible && vm.AllowSplit;
        task.EstimatedMinutes = vm.IsAppointment
            ? (int)(vm.EndTime!.Value - vm.StartTime!.Value).TotalMinutes
            : vm.EstimatedMinutes;
        task.StartTime = task.IsFlexible ? null : vm.StartTime;
        task.EndTime = task.IsFlexible ? null : vm.EndTime;
        task.DueDate = vm.IsAppointment ? vm.EndTime!.Value : vm.DueDate!.Value;
    }

    /// <summary>Re-run the adaptive scheduler, then report unplaced tasks and conflicts.</summary>
    private async Task AfterChangeAsync(string successMessage)
    {
        var result = await scheduler.RescheduleAsync(UserId);
        var conflicts = await scheduler.GetConflictsAsync(UserId);

        var splitText = result.SplitTasks switch
        {
            0 => "",
            1 => " 1 long task was split into sessions.",
            _ => $" {result.SplitTasks} long tasks were split into sessions."
        };
        TempData["Success"] = successMessage + splitText;

        var warnings = new List<string>();
        if (result.Unscheduled.Count > 0)
            warnings.Add($"No room before the deadline for: {string.Join(", ", result.Unscheduled.Select(t => t.Title))}. " +
                         "Extend the deadline, shorten the duration, allow splitting, or free up time.");
        if (conflicts.Count > 0)
            warnings.Add($"{conflicts.Count} scheduling conflict{(conflicts.Count == 1 ? "" : "s")} found. Open Schedule › Conflicts to fix {(conflicts.Count == 1 ? "it" : "them")}.");

        if (warnings.Count > 0) TempData["Warning"] = string.Join(" ", warnings);
    }
}
