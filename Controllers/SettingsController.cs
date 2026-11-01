using System.Text;
using FlexPlan.Data;
using FlexPlan.Models;
using FlexPlan.Services;
using FlexPlan.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FlexPlan.Controllers;

[Authorize]
public class SettingsController(AppDbContext db, IPasswordHasher<User> hasher, IConfiguration config, SchedulerService scheduler) : BaseController
{
    public async Task<IActionResult> Index(string tab = "account")
    {
        var user = await db.Users.FindAsync(UserId);
        if (user is null) return NotFound();

        tab = tab is "account" or "scheduling" or "appearance" or "notifications" or "database" ? tab : "account";
        return View(new SettingsViewModel
        {
            Tab = tab,
            User = user,
            DbType = "SQLite",
            ConnectionString = config.GetConnectionString("Default") ?? "",
            TaskCount = await db.Tasks.CountAsync(t => t.UserId == UserId)
        });
    }

    [HttpPost]
    public async Task<IActionResult> SaveAccount(string fullName)
    {
        var user = await db.Users.FindAsync(UserId);
        fullName = (fullName ?? "").Trim();
        if (user is null) return NotFound();
        if (fullName.Length is 0 or > 80)
        {
            TempData["Warning"] = "Enter a name between 1 and 80 characters.";
            return RedirectToAction(nameof(Index), new { tab = "account" });
        }
        user.FullName = fullName;
        await db.SaveChangesAsync();
        await AuthHelper.SignInAsync(HttpContext, user, true);
        TempData["Success"] = "Account saved.";
        return RedirectToAction(nameof(Index), new { tab = "account" });
    }

    [HttpPost]
    public async Task<IActionResult> ChangePassword(string currentPassword, string newPassword, string confirmPassword)
    {
        var user = await db.Users.FindAsync(UserId);
        if (user is null) return NotFound();

        string? error = null;
        if (hasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword ?? "") == PasswordVerificationResult.Failed)
            error = "Your current password is incorrect.";
        else if ((newPassword ?? "").Length < 8)
            error = "Use at least 8 characters for the new password.";
        else if (newPassword != confirmPassword)
            error = "The new passwords don't match.";

        if (error is not null) TempData["Warning"] = error;
        else
        {
            user.PasswordHash = hasher.HashPassword(user, newPassword!);
            await db.SaveChangesAsync();
            TempData["Success"] = "Password changed.";
        }
        return RedirectToAction(nameof(Index), new { tab = "account" });
    }

    [HttpPost]
    public async Task<IActionResult> SaveAppearance(string theme)
    {
        var user = await db.Users.FindAsync(UserId);
        if (user is null) return NotFound();
        if (!Choices.Themes.Contains(theme)) theme = "Light";

        user.Theme = theme;
        await db.SaveChangesAsync();
        AuthHelper.SetThemeCookie(HttpContext, theme);
        TempData["Success"] = $"Theme set to {theme.ToLower()}.";
        return RedirectToAction(nameof(Index), new { tab = "appearance" });
    }

    [HttpPost]
    public async Task<IActionResult> SaveScheduling(int bufferMinutes, bool lunchBreak, int maxSessionMinutes)
    {
        var user = await db.Users.FindAsync(UserId);
        if (user is null) return NotFound();

        user.BufferMinutes = Choices.BufferOptions.Contains(bufferMinutes) ? bufferMinutes : 10;
        user.LunchBreak = lunchBreak;
        user.MaxSessionMinutes = Choices.SessionOptions.Contains(maxSessionMinutes) ? maxSessionMinutes : 90;
        await db.SaveChangesAsync();

        await scheduler.RescheduleAsync(UserId);
        TempData["Success"] = "Scheduling preferences saved. Your flexible tasks were re-planned.";
        return RedirectToAction(nameof(Index), new { tab = "scheduling" });
    }

    [HttpPost]
    public async Task<IActionResult> SaveNotifications(bool remindersEnabled, bool desktopNotifications, int reminderLeadMinutes)
    {
        var user = await db.Users.FindAsync(UserId);
        if (user is null) return NotFound();

        user.RemindersEnabled = remindersEnabled;
        user.DesktopNotifications = desktopNotifications;
        user.ReminderLeadMinutes = new[] { 30, 60, 1440, 2880 }.Contains(reminderLeadMinutes) ? reminderLeadMinutes : 1440;
        await db.SaveChangesAsync();

        TempData["Success"] = "Notification settings saved.";
        return RedirectToAction(nameof(Index), new { tab = "notifications" });
    }

    [HttpGet]
    public async Task<IActionResult> Export()
    {
        var tasks = await db.Tasks.Where(t => t.UserId == UserId).OrderBy(t => t.DueDate).ToListAsync();

        static string Q(string? s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
        var sb = new StringBuilder("Title,Description,Category,Priority,Status,Due,Start,End,Appointment\r\n");
        foreach (var t in tasks)
            sb.AppendLine(string.Join(",", Q(t.Title), Q(t.Description), Q(t.Category), t.Priority, t.Status,
                t.DueDate.ToString("yyyy-MM-dd HH:mm"), t.StartTime?.ToString("yyyy-MM-dd HH:mm"),
                t.EndTime?.ToString("yyyy-MM-dd HH:mm"), t.IsAppointment));

        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(),
            "text/csv", "flexplan-tasks.csv");
    }
}
