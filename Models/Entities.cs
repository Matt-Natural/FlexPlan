using System.ComponentModel.DataAnnotations;

namespace FlexPlan.Models;

public enum TaskPriority { Low = 1, Medium = 2, High = 3 }

public enum ProgressStatus { Pending = 0, InProgress = 1, Completed = 2 }

public class User
{
    public int Id { get; set; }

    [Required, StringLength(80)]
    public string FullName { get; set; } = "";

    [Required, EmailAddress, StringLength(120)]
    public string Email { get; set; } = "";

    [Required]
    public string PasswordHash { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // Profile
    [StringLength(40)] public string Role { get; set; } = "Student";
    [StringLength(40)] public string PreferredSchedule { get; set; } = "Flexible";
    [StringLength(60)] public string TimeZoneLabel { get; set; } = "(GMT+8) Philippines";

    // Settings
    [StringLength(10)] public string Theme { get; set; } = "Light";      // Light | Dark | System
    public bool RemindersEnabled { get; set; } = true;
    public bool DesktopNotifications { get; set; } = true;
    public int ReminderLeadMinutes { get; set; } = 1440;                  // how far ahead reminders appear

    // Scheduling preferences
    public int BufferMinutes { get; set; } = 10;        // breathing room around every item
    public bool LunchBreak { get; set; } = true;        // keep 12:00-13:00 free of flexible tasks
    public int MaxSessionMinutes { get; set; } = 90;    // longest single work session before a break

    public List<TaskItem> Tasks { get; set; } = new();

    public string Initials =>
        string.Concat(FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(p => char.ToUpper(p[0])));
}

public class TaskItem
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    [Required, StringLength(120)]
    public string Title { get; set; } = "";

    [StringLength(1000)]
    public string? Description { get; set; }

    [StringLength(40)]
    public string Category { get; set; } = "Personal";

    public DateTime DueDate { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }

    public TaskPriority Priority { get; set; } = TaskPriority.Medium;
    public ProgressStatus Status { get; set; } = ProgressStatus.Pending;

    public int EstimatedMinutes { get; set; }

    /// <summary>Flexible tasks are placed (and moved) automatically by the scheduler.</summary>
    public bool IsFlexible { get; set; }

    /// <summary>Flexible tasks may be cut into several shorter sessions.</summary>
    public bool AllowSplit { get; set; }

    /// <summary>Appointments always have a fixed start and end time.</summary>
    public bool IsAppointment { get; set; }

    public bool HasReminder { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>Planned work sessions for a flexible task (one or more).</summary>
    public List<TaskSession> Sessions { get; set; } = new();

    public List<Subtask> Subtasks { get; set; } = new();

    // Computed helpers (read-only, so EF Core does not map them).
    public int SubtaskTotal => Subtasks.Count;
    public int SubtaskDone => Subtasks.Count(s => s.IsDone);
    public bool IsOverdue => Status != ProgressStatus.Completed && DueDate < DateTime.Now;
    public DateTime AnchorDate => StartTime ?? DueDate;
    public bool HasBlock => StartTime.HasValue && EndTime.HasValue;
    public string CategoryClass => "cat-" + new string(Category.ToLowerInvariant().Where(char.IsLetter).ToArray());

    public string DisplayTime() =>
        HasBlock ? $"{StartTime:h:mm tt} – {EndTime:h:mm tt}" : $"Due {DueDate:h:mm tt}";
}

public class TaskSession
{
    public int Id { get; set; }
    public int TaskItemId { get; set; }
    public TaskItem? TaskItem { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
}

public class Subtask
{
    public int Id { get; set; }
    public int TaskItemId { get; set; }
    public TaskItem? TaskItem { get; set; }

    [Required, StringLength(120)]
    public string Title { get; set; } = "";

    public bool IsDone { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>Fixed lists used by the forms.</summary>
public static class Choices
{
    public static readonly string[] Roles = { "Student", "Employee", "Teacher", "Freelancer", "Parent", "Business owner", "Other" };
    public static readonly string[] Schedules = { "Flexible", "Morning", "Daytime", "Evening" };
    public static readonly string[] Categories = { "School", "Work", "Study", "Personal", "Family", "Business", "Other" };
    public static readonly string[] TimeZones =
    {
        "(GMT+8) Philippines", "(GMT+0) London", "(GMT+1) Paris", "(GMT+5:30) India",
        "(GMT+9) Japan", "(GMT+10) Sydney", "(GMT-5) New York", "(GMT-8) Los Angeles"
    };
    public static readonly string[] Themes = { "Light", "Dark", "System" };
    public static readonly int[] BufferOptions = { 0, 5, 10, 15, 30 };
    public static readonly int[] SessionOptions = { 30, 45, 60, 90, 120 };

    /// <summary>Hours of the day the adaptive scheduler may use for flexible tasks.</summary>
    public static (int Start, int End) Hours(string preferred) => preferred switch
    {
        "Morning" => (6, 14),
        "Daytime" => (9, 17),
        "Evening" => (12, 22),
        _ => (8, 20)
    };

    public static string Describe(string preferred) => preferred switch
    {
        "Morning" => "Morning (6 AM – 2 PM)",
        "Daytime" => "Daytime (9 AM – 5 PM)",
        "Evening" => "Evening (12 PM – 10 PM)",
        _ => "Flexible (8 AM – 8 PM)"
    };
}
