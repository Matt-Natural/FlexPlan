using System.ComponentModel.DataAnnotations;
using FlexPlan.Models;
using FlexPlan.Services;

namespace FlexPlan.ViewModels;

public class RegisterViewModel
{
    [Required(ErrorMessage = "Enter your full name."), StringLength(80)]
    [Display(Name = "Full name")]
    public string FullName { get; set; } = "";

    [Required(ErrorMessage = "Enter your email address."), EmailAddress(ErrorMessage = "Enter a valid email address.")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Create a password."), StringLength(100, MinimumLength = 8, ErrorMessage = "Use at least 8 characters.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [Required(ErrorMessage = "Re-enter your password.")]
    [DataType(DataType.Password), Compare(nameof(Password), ErrorMessage = "Passwords don't match.")]
    [Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = "";
}

public class LoginViewModel
{
    [Required(ErrorMessage = "Enter your email address."), EmailAddress(ErrorMessage = "Enter a valid email address.")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Enter your password.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [Display(Name = "Keep me signed in")]
    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }
}

public class EditProfileViewModel
{
    [Required(ErrorMessage = "Enter your full name."), StringLength(80)]
    [Display(Name = "Full name")]
    public string FullName { get; set; } = "";

    public string Role { get; set; } = "Student";

    [Display(Name = "Preferred schedule")]
    public string PreferredSchedule { get; set; } = "Flexible";

    [Display(Name = "Time zone")]
    public string TimeZoneLabel { get; set; } = "(GMT+8) Philippines";
}

public class ProfileViewModel
{
    public User User { get; set; } = null!;
    public int Total { get; set; }
    public int Completed { get; set; }
    public int Pending { get; set; }
    public int Overdue { get; set; }
}

public class SettingsViewModel
{
    public string Tab { get; set; } = "account";
    public User User { get; set; } = null!;
    public string DbType { get; set; } = "SQLite";
    public string ConnectionString { get; set; } = "";
    public int TaskCount { get; set; }
}

public class TaskFormViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Give it a title."), StringLength(120)]
    public string Title { get; set; } = "";

    [StringLength(1000)]
    public string? Description { get; set; }

    [Required, StringLength(40)]
    public string Category { get; set; } = "Personal";

    [DataType(DataType.DateTime)]
    [DisplayFormat(DataFormatString = "{0:yyyy-MM-ddTHH:mm}", ApplyFormatInEditMode = true)]
    [Display(Name = "Due date")]
    public DateTime? DueDate { get; set; }

    [DataType(DataType.DateTime)]
    [DisplayFormat(DataFormatString = "{0:yyyy-MM-ddTHH:mm}", ApplyFormatInEditMode = true)]
    [Display(Name = "Start time")]
    public DateTime? StartTime { get; set; }

    [DataType(DataType.DateTime)]
    [DisplayFormat(DataFormatString = "{0:yyyy-MM-ddTHH:mm}", ApplyFormatInEditMode = true)]
    [Display(Name = "End time")]
    public DateTime? EndTime { get; set; }

    public TaskPriority Priority { get; set; } = TaskPriority.Medium;
    public ProgressStatus Status { get; set; } = ProgressStatus.Pending;

    [Range(0, 1440, ErrorMessage = "Enter a duration between 0 and 1440 minutes.")]
    [Display(Name = "Estimated minutes")]
    public int EstimatedMinutes { get; set; } = 60;

    [Display(Name = "Let FlexPlan schedule this task")]
    public bool IsFlexible { get; set; }

    public bool IsAppointment { get; set; }

    [Display(Name = "Allow splitting into shorter sessions")]
    public bool AllowSplit { get; set; }

    [Display(Name = "Set reminder")]
    public bool HasReminder { get; set; } = true;

    /// <summary>Create form only: one subtask per line.</summary>
    [StringLength(3000)]
    public string? SubtasksText { get; set; }

    // Shown on the Edit page (not posted back).
    public List<Subtask> Subtasks { get; set; } = new();
    public List<TaskSession> Sessions { get; set; } = new();
}

public class DashboardViewModel
{
    public string UserName { get; set; } = "";
    public string Greeting { get; set; } = "";
    public int Total { get; set; }
    public int TodayCount { get; set; }
    public int AppointmentsToday { get; set; }
    public int OverdueCount { get; set; }
    public int Completed { get; set; }
    public int CompletionPercent { get; set; }
    public List<Block> Today { get; set; } = new();
    public List<TaskItem> Upcoming { get; set; } = new();
    public List<TaskItem> Overdue { get; set; } = new();
    public int ConflictCount { get; set; }
}

public class CalendarViewModel
{
    public string Mode { get; set; } = "month";
    public DateTime Date { get; set; }
    public DateTime Selected { get; set; }
    public DateTime RangeStart { get; set; }
    public DateTime RangeEnd { get; set; }
    public DateTime Prev { get; set; }
    public DateTime Next { get; set; }
    public string Title { get; set; } = "";
    public List<Block> Items { get; set; } = new();
}

public class ScheduleViewModel
{
    public DateTime Date { get; set; }
    public int StartHour { get; set; } = 6;
    public int EndHour { get; set; } = 22;
    public List<Block> Blocks { get; set; } = new();
    public List<Block> DueOnly { get; set; } = new();
    public int ConflictCount { get; set; }
}
