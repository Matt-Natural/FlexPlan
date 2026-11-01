namespace FlexPlan.Models;

/// <summary>
/// One thing to draw on a calendar or timeline: a fixed event, one work session of a
/// flexible task, or (when End is null) a deadline for a task with no time block.
/// Not stored in the database.
/// </summary>
public class Block(TaskItem task, DateTime start, DateTime? end, int index, int count)
{
    public TaskItem Task { get; } = task;
    public DateTime Start { get; } = start;
    public DateTime? End { get; } = end;
    public int Index { get; } = index;
    public int Count { get; } = count;

    public string Title => Count > 1 ? $"{Task.Title} ({Index}/{Count})" : Task.Title;
    public string CategoryClass => Task.CategoryClass;
    public string TimeText => End.HasValue ? $"{Start:h:mm tt} – {End:h:mm tt}" : $"Due {Start:h:mm tt}";
}
