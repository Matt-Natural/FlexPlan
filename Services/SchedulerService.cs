using FlexPlan.Data;
using FlexPlan.Models;
using Microsoft.EntityFrameworkCore;

namespace FlexPlan.Services;

public record ConflictPair(TaskItem First, TaskItem Second);

public class ScheduleResult
{
    public int Scheduled { get; set; }
    public int SplitTasks { get; set; }
    public List<TaskItem> Unscheduled { get; } = new();
}

/// <summary>A conflict plus the adjustments FlexPlan can suggest for it.</summary>
public class ConflictAdvice
{
    public TaskItem First { get; set; } = null!;
    public TaskItem Second { get; set; } = null!;
    /// <summary>The less important task, which FlexPlan proposes to move.</summary>
    public TaskItem Mover { get; set; } = null!;
    public TaskItem Other => Mover.Id == First.Id ? Second : First;

    public (DateTime Start, DateTime End)? MoveSlot { get; set; }
    public bool MovePastDeadline { get; set; }

    public TaskItem? SwapWith { get; set; }
    public (DateTime Start, DateTime End)? MoverSwapSlot { get; set; }
    public (DateTime Start, DateTime End)? SwapWithSlot { get; set; }
}

/// <summary>
/// Adaptive scheduling, conflict detection and conflict suggestions.
///
/// Fixed items (start and end time, not flexible) are never moved automatically.
/// Flexible tasks are placed into free time inside the user's preferred hours:
///  - every item keeps a buffer (default 10 min) of breathing room around it,
///  - an optional lunch break (12:00-13:00) is kept free,
///  - tasks marked "allow split" are cut into sessions no longer than the user's
///    maximum session length, spread over as many gaps or days as needed,
///  - tasks are placed by priority (High first), then deadline, then duration,
///    and must finish before their own deadline.
/// </summary>
public class SchedulerService(AppDbContext db)
{
    private TimeSpan _workStart = new(8, 0, 0);
    private TimeSpan _workEnd = new(20, 0, 0);
    private int _buffer = 10;
    private bool _lunch = true;
    private int _maxSession = 90;

    private const int HorizonDays = 90;
    private const int MinChunk = 30; // shortest session we create when splitting

    private async Task LoadSettingsAsync(int userId)
    {
        var u = await db.Users.AsNoTracking()
            .Where(x => x.Id == userId)
            .Select(x => new { x.PreferredSchedule, x.BufferMinutes, x.LunchBreak, x.MaxSessionMinutes })
            .FirstOrDefaultAsync();
        if (u is null) return;

        var (s, e) = Choices.Hours(u.PreferredSchedule);
        _workStart = TimeSpan.FromHours(s);
        _workEnd = TimeSpan.FromHours(e);
        _buffer = Math.Clamp(u.BufferMinutes, 0, 60);
        _lunch = u.LunchBreak;
        _maxSession = Math.Clamp(u.MaxSessionMinutes, 30, 240);
    }

    // ---------------------------------------------------------------- adaptive scheduling
    public async Task<ScheduleResult> RescheduleAsync(int userId, DateTime? now = null)
    {
        await LoadSettingsAsync(userId);
        var from = RoundUpToQuarterHour(now ?? DateTime.Now);

        var open = await db.Tasks
            .Include(t => t.Sessions)
            .Where(t => t.UserId == userId && t.Status != ProgressStatus.Completed)
            .ToListAsync();

        // Sessions only make sense for flexible tasks.
        foreach (var t in open.Where(t => !t.IsFlexible && t.Sessions.Count > 0))
        {
            db.TaskSessions.RemoveRange(t.Sessions.ToList());
            t.Sessions.Clear();
        }

        // Time already taken by fixed items (padded with the buffer) plus daily lunch breaks.
        var busy = open
            .Where(t => !t.IsFlexible && t.HasBlock)
            .Select(t => Pad((t.StartTime!.Value, t.EndTime!.Value)))
            .ToList();
        busy.AddRange(LunchBlocks(from, from.AddDays(HorizonDays)));

        var flexible = open
            .Where(t => t.IsFlexible && t.EstimatedMinutes > 0)
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.DueDate)
            .ThenBy(t => t.EstimatedMinutes)
            .ToList();

        var result = new ScheduleResult();
        foreach (var task in flexible)
        {
            db.TaskSessions.RemoveRange(task.Sessions.ToList());
            task.Sessions.Clear();

            var placed = Place(task, busy, from);
            if (placed is null)
            {
                task.StartTime = null;
                task.EndTime = null;
                result.Unscheduled.Add(task);
                continue;
            }

            foreach (var p in placed)
                task.Sessions.Add(new TaskSession { StartTime = p.Start, EndTime = p.End });

            task.StartTime = placed[0].Start;
            task.EndTime = placed[0].End;
            busy.AddRange(placed.Select(Pad));
            result.Scheduled++;
            if (placed.Count > 1) result.SplitTasks++;
        }

        await db.SaveChangesAsync();
        return result;
    }

    /// <summary>Finds one slot, or (for splittable tasks) several sessions, for a task.</summary>
    private List<(DateTime Start, DateTime End)>? Place(
        TaskItem task, List<(DateTime Start, DateTime End)> busy, DateTime from)
    {
        var total = task.EstimatedMinutes;

        if (!task.AllowSplit)
        {
            var slot = FindSlot(busy, TimeSpan.FromMinutes(total), from, task.DueDate);
            return slot is null ? null : new List<(DateTime Start, DateTime End)> { slot.Value };
        }

        var working = new List<(DateTime Start, DateTime End)>(busy);
        var sessions = new List<(DateTime Start, DateTime End)>();
        var remaining = total;
        var cursor = from;

        while (remaining > 0)
        {
            var needed = Math.Min(MinChunk, remaining);
            (DateTime Start, DateTime End)? gap = null;
            foreach (var g in FreeGaps(working, cursor, task.DueDate))
            {
                if ((g.End - g.Start).TotalMinutes >= needed) { gap = g; break; }
            }
            if (gap is null) return null; // can't finish before the deadline

            var gapLen = (int)(gap.Value.End - gap.Value.Start).TotalMinutes;
            var len = Math.Min(Math.Min(remaining, _maxSession), gapLen);

            // Avoid leaving a tiny leftover session.
            var rest = remaining - len;
            if (rest > 0 && rest < MinChunk && len - (MinChunk - rest) >= MinChunk)
                len -= MinChunk - rest;

            var session = (Start: gap.Value.Start, End: gap.Value.Start.AddMinutes(len));
            sessions.Add(session);
            working.Add(Pad(session));
            remaining -= len;
            cursor = session.End;
        }
        return sessions;
    }

    // ---------------------------------------------------------------- calendar blocks
    /// <summary>Everything to draw between two dates: fixed events, work sessions and bare deadlines.</summary>
    public async Task<List<Block>> GetBlocksAsync(int userId, DateTime from, DateTime to, bool includeCompleted)
    {
        var query = db.Tasks.Include(t => t.Sessions).Where(t => t.UserId == userId);
        if (!includeCompleted) query = query.Where(t => t.Status != ProgressStatus.Completed);

        var tasks = await query
            .Where(t => (t.DueDate >= from && t.DueDate < to)
                        || (t.StartTime >= from && t.StartTime < to)
                        || t.Sessions.Any(s => s.StartTime >= from && s.StartTime < to))
            .ToListAsync();

        var blocks = new List<Block>();
        foreach (var t in tasks)
        {
            if (t.IsFlexible && t.Sessions.Count > 0)
            {
                var ordered = t.Sessions.OrderBy(s => s.StartTime).ToList();
                for (var i = 0; i < ordered.Count; i++)
                {
                    var s = ordered[i];
                    if (s.StartTime >= from && s.StartTime < to)
                        blocks.Add(new Block(t, s.StartTime, s.EndTime, i + 1, ordered.Count));
                }
            }
            else if (t.HasBlock)
            {
                if (t.StartTime >= from && t.StartTime < to)
                    blocks.Add(new Block(t, t.StartTime!.Value, t.EndTime, 1, 1));
            }
            else if (t.DueDate >= from && t.DueDate < to)
            {
                blocks.Add(new Block(t, t.DueDate, null, 1, 1));
            }
        }
        return blocks.OrderBy(b => b.Start).ToList();
    }

    // ---------------------------------------------------------------- conflict detection
    public async Task<List<ConflictPair>> GetConflictsAsync(int userId)
    {
        var blocks = await OpenBlocksAsync(userId);
        var conflicts = new List<ConflictPair>();
        for (var i = 0; i < blocks.Count; i++)
        {
            for (var j = i + 1; j < blocks.Count; j++)
            {
                if (blocks[j].StartTime >= blocks[i].EndTime) break; // sorted by start
                conflicts.Add(new ConflictPair(blocks[i], blocks[j]));
            }
        }
        return conflicts;
    }

    // ---------------------------------------------------------------- conflict suggestions
    public async Task<List<ConflictAdvice>> GetAdviceAsync(int userId, DateTime? now = null)
    {
        await LoadSettingsAsync(userId);
        var from = RoundUpToQuarterHour(now ?? DateTime.Now);
        var blocks = await OpenBlocksAsync(userId);
        var advice = new List<ConflictAdvice>();

        foreach (var c in await GetConflictsAsync(userId))
        {
            var mover = ChooseMover(c.First, c.Second);
            var other = mover.Id == c.First.Id ? c.Second : c.First;
            var a = new ConflictAdvice { First = c.First, Second = c.Second, Mover = mover };

            var mStart = mover.StartTime!.Value;
            var duration = mover.EndTime!.Value - mStart;

            // 1) Move the less important task to the next free slot.
            var busy = blocks.Where(b => b.Id != mover.Id)
                .Select(b => Pad((b.StartTime!.Value, b.EndTime!.Value))).ToList();
            busy.AddRange(LunchBlocks(from, from.AddDays(HorizonDays)));

            var slot = FindSlot(busy, duration, from, mover.DueDate);
            if (slot is null)
            {
                slot = FindSlot(busy, duration, from, from.AddDays(14));
                a.MovePastDeadline = slot is not null;
            }
            a.MoveSlot = slot;

            // 2) Swap with another task if both fit in each other's place.
            var candidates = blocks
                .Where(b => b.Id != mover.Id && b.Id != other.Id && b.StartTime >= from)
                .OrderBy(b => Math.Abs((b.StartTime!.Value - mStart).Ticks));

            foreach (var z in candidates)
            {
                var zStart = z.StartTime!.Value;
                var zDur = z.EndTime!.Value - zStart;
                var moverNew = (Start: zStart, End: zStart + duration);
                var zNew = (Start: mStart, End: mStart + zDur);

                if (zNew.Start < from) continue;
                if (moverNew.End > mover.DueDate || zNew.End > z.DueDate) continue;
                if (moverNew.Start < zNew.End && zNew.Start < moverNew.End) continue;

                var rest = blocks.Where(b => b.Id != mover.Id && b.Id != z.Id).ToList();
                if (rest.Any(b => Overlaps(b, moverNew) || Overlaps(b, zNew))) continue;

                a.SwapWith = z;
                a.MoverSwapSlot = moverNew;
                a.SwapWithSlot = zNew;
                break;
            }
            advice.Add(a);
        }
        return advice;
    }

    /// <summary>Applies the user's chosen adjustment to one conflict. Returns a message to show.</summary>
    public async Task<(bool Ok, string Message)> ApplyAsync(int userId, int firstId, int secondId, string choice)
    {
        var a = (await GetAdviceAsync(userId)).FirstOrDefault(x =>
            (x.First.Id == firstId && x.Second.Id == secondId) || (x.First.Id == secondId && x.Second.Id == firstId));

        if (a is null) return (false, "That conflict no longer exists.");

        switch (choice)
        {
            case "move" when a.MoveSlot is { } m:
                a.Mover.StartTime = m.Start;
                a.Mover.EndTime = m.End;
                await db.SaveChangesAsync();
                await RescheduleAsync(userId);
                return (true, $"Moved “{a.Mover.Title}” to {m.Start:MMM d, h:mm tt} – {m.End:h:mm tt}.");

            case "swap" when a.SwapWith is not null && a.MoverSwapSlot is { } ms && a.SwapWithSlot is { } ws:
                a.Mover.StartTime = ms.Start; a.Mover.EndTime = ms.End;
                a.SwapWith.StartTime = ws.Start; a.SwapWith.EndTime = ws.End;
                await db.SaveChangesAsync();
                await RescheduleAsync(userId);
                return (true, $"Swapped “{a.Mover.Title}” with “{a.SwapWith.Title}”.");

            case "keep":
                return (true, "Both tasks kept. The overlap stays highlighted until you change one of them.");

            default:
                return (false, "That adjustment isn't available for this conflict.");
        }
    }

    // ---------------------------------------------------------------- helpers
    private Task<List<TaskItem>> OpenBlocksAsync(int userId) =>
        db.Tasks.Where(t => t.UserId == userId
                            && t.Status != ProgressStatus.Completed
                            && t.StartTime != null && t.EndTime != null)
            .OrderBy(t => t.StartTime)
            .ToListAsync();

    private static TaskItem ChooseMover(TaskItem a, TaskItem b)
    {
        if (a.Priority != b.Priority) return a.Priority < b.Priority ? a : b;
        if (a.IsFlexible != b.IsFlexible) return a.IsFlexible ? a : b;
        return b; // later start moves
    }

    private static bool Overlaps(TaskItem t, (DateTime Start, DateTime End) s) =>
        t.StartTime < s.End && s.Start < t.EndTime;

    /// <summary>Adds the buffer on both sides of a busy interval.</summary>
    private (DateTime Start, DateTime End) Pad((DateTime Start, DateTime End) b) =>
        (b.Start.AddMinutes(-_buffer), b.End.AddMinutes(_buffer));

    private List<(DateTime Start, DateTime End)> LunchBlocks(DateTime from, DateTime to)
    {
        var list = new List<(DateTime Start, DateTime End)>();
        if (!_lunch) return list;
        for (var day = from.Date; day <= to.Date; day = day.AddDays(1))
            list.Add((day.AddHours(12), day.AddHours(13)));
        return list;
    }

    /// <summary>Free stretches of time inside working hours, in order, between busy intervals.</summary>
    private IEnumerable<(DateTime Start, DateTime End)> FreeGaps(
        List<(DateTime Start, DateTime End)> busy, DateTime from, DateTime due)
    {
        var lastDay = new[] { due.Date, from.Date.AddDays(HorizonDays) }.Min();

        for (var day = from.Date; day <= lastDay; day = day.AddDays(1))
        {
            var cursor = day + _workStart;
            if (cursor < from) cursor = from;
            var end = day + _workEnd;
            if (due < end) end = due;
            if (cursor >= end) continue;

            var dayEnd = end;
            var dayStart = cursor;
            var todays = busy.Where(b => b.End > dayStart && b.Start < dayEnd).OrderBy(b => b.Start).ToList();

            foreach (var b in todays)
            {
                if (b.Start > cursor) yield return (cursor, b.Start);
                if (b.End > cursor) cursor = b.End;
            }
            if (cursor < end) yield return (cursor, end);
        }
    }

    private (DateTime Start, DateTime End)? FindSlot(
        List<(DateTime Start, DateTime End)> busy, TimeSpan duration, DateTime from, DateTime due)
    {
        foreach (var g in FreeGaps(busy, from, due))
        {
            if (g.End - g.Start >= duration) return (g.Start, g.Start + duration);
        }
        return null;
    }

    private static DateTime RoundUpToQuarterHour(DateTime dt)
    {
        var ticks = TimeSpan.FromMinutes(15).Ticks;
        return new DateTime((dt.Ticks + ticks - 1) / ticks * ticks, dt.Kind);
    }
}
