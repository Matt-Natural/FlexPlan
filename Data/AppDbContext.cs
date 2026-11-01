using FlexPlan.Models;
using Microsoft.EntityFrameworkCore;

namespace FlexPlan.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<TaskSession> TaskSessions => Set<TaskSession>();
    public DbSet<Subtask> Subtasks => Set<Subtask>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        modelBuilder.Entity<TaskItem>()
            .HasOne(t => t.User)
            .WithMany(u => u.Tasks)
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TaskItem>()
            .HasIndex(t => new { t.UserId, t.DueDate });

        modelBuilder.Entity<TaskSession>()
            .HasOne(s => s.TaskItem)
            .WithMany(t => t.Sessions)
            .HasForeignKey(s => s.TaskItemId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Subtask>()
            .HasOne(s => s.TaskItem)
            .WithMany(t => t.Subtasks)
            .HasForeignKey(s => s.TaskItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
