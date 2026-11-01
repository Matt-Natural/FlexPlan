using FlexPlan.Data;
using FlexPlan.Models;
using FlexPlan.Services;
using FlexPlan.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FlexPlan.Controllers;

public class AccountController(AppDbContext db, IPasswordHasher<User> hasher) : BaseController
{
    [HttpGet]
    public IActionResult Register() =>
        User.Identity?.IsAuthenticated == true ? RedirectToAction("Index", "Dashboard") : View(new RegisterViewModel());

    [HttpPost]
    public async Task<IActionResult> Register(RegisterViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var email = vm.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == email))
        {
            ModelState.AddModelError(nameof(vm.Email), "An account with this email already exists. Try signing in.");
            return View(vm);
        }

        var user = new User { FullName = vm.FullName.Trim(), Email = email };
        user.PasswordHash = hasher.HashPassword(user, vm.Password);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        await AuthHelper.SignInAsync(HttpContext, user, true);
        return RedirectToAction("Index", "Dashboard");
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null) =>
        User.Identity?.IsAuthenticated == true
            ? RedirectToAction("Index", "Dashboard")
            : View(new LoginViewModel { ReturnUrl = returnUrl });

    [HttpPost]
    public async Task<IActionResult> Login(LoginViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var email = vm.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);

        var ok = user is not null &&
                 hasher.VerifyHashedPassword(user, user.PasswordHash, vm.Password) != PasswordVerificationResult.Failed;
        if (!ok)
        {
            ModelState.AddModelError("", "Email or password is incorrect.");
            return View(vm);
        }

        await AuthHelper.SignInAsync(HttpContext, user!, vm.RememberMe);

        if (!string.IsNullOrEmpty(vm.ReturnUrl) && Url.IsLocalUrl(vm.ReturnUrl))
            return LocalRedirect(vm.ReturnUrl);
        return RedirectToAction("Index", "Dashboard");
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Home");
    }

    // ---------- My Profile ----------
    [Authorize]
    public async Task<IActionResult> Profile()
    {
        var user = await db.Users.FindAsync(UserId);
        if (user is null) return RedirectToAction(nameof(Login));

        var tasks = await db.Tasks.Where(t => t.UserId == UserId).ToListAsync();
        var now = DateTime.Now;
        return View(new ProfileViewModel
        {
            User = user,
            Total = tasks.Count,
            Completed = tasks.Count(t => t.Status == ProgressStatus.Completed),
            Pending = tasks.Count(t => t.Status != ProgressStatus.Completed),
            Overdue = tasks.Count(t => t.Status != ProgressStatus.Completed && t.DueDate < now)
        });
    }

    [Authorize, HttpGet]
    public async Task<IActionResult> EditProfile()
    {
        var u = await db.Users.FindAsync(UserId);
        if (u is null) return NotFound();
        return View(new EditProfileViewModel
        {
            FullName = u.FullName, Role = u.Role,
            PreferredSchedule = u.PreferredSchedule, TimeZoneLabel = u.TimeZoneLabel
        });
    }

    [Authorize, HttpPost]
    public async Task<IActionResult> EditProfile(EditProfileViewModel vm)
    {
        var u = await db.Users.FindAsync(UserId);
        if (u is null) return NotFound();

        if (!Choices.Roles.Contains(vm.Role)) ModelState.AddModelError(nameof(vm.Role), "Choose a role from the list.");
        if (!Choices.Schedules.Contains(vm.PreferredSchedule)) ModelState.AddModelError(nameof(vm.PreferredSchedule), "Choose a schedule from the list.");
        if (!Choices.TimeZones.Contains(vm.TimeZoneLabel)) ModelState.AddModelError(nameof(vm.TimeZoneLabel), "Choose a time zone from the list.");
        if (!ModelState.IsValid) return View(vm);

        u.FullName = vm.FullName.Trim();
        u.Role = vm.Role;
        u.PreferredSchedule = vm.PreferredSchedule;
        u.TimeZoneLabel = vm.TimeZoneLabel;
        await db.SaveChangesAsync();

        await AuthHelper.SignInAsync(HttpContext, u, true); // refresh the name shown in the header

        // A new preferred schedule changes which hours flexible tasks can use.
        await HttpContext.RequestServices.GetRequiredService<SchedulerService>().RescheduleAsync(u.Id);

        TempData["Success"] = "Profile updated. Flexible tasks were re-planned for your preferred schedule.";
        return RedirectToAction(nameof(Profile));
    }
}
