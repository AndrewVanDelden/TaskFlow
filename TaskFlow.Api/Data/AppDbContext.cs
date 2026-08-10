using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Models;

namespace TaskFlow.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<AgentLog> AgentLogs => Set<AgentLog>();
    public DbSet<JobApplication> JobApplications => Set<JobApplication>();
    public DbSet<ResumeContext> ResumeContexts => Set<ResumeContext>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── User configuration ────────────────────────────────────────────────
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(u => u.Email).IsUnique();
        });

        // ── TaskItem configuration ────────────────────────────────────────────
        modelBuilder.Entity<TaskItem>(entity =>
        {
            // Store enums as strings so the DB is human-readable
            entity.Property(t => t.Status)
                  .HasConversion<string>();

            entity.Property(t => t.Priority)
                  .HasConversion<string>();

            entity.Property(t => t.Kind)
                  .HasConversion<string>();

            // Relationship: Task -> User (optional assignment)
            entity.HasOne(t => t.AssignedTo)
                  .WithMany(u => u.Tasks)
                  .HasForeignKey(t => t.AssignedToId)
                  .OnDelete(DeleteBehavior.SetNull);

            // Relationship: Task -> JobApplication (optional; resume/cover-letter siblings only).
            // Cascade so deleting a JobApplication removes its sibling tasks rather than orphaning them.
            entity.HasOne(t => t.Application)
                  .WithMany(a => a.Tasks)
                  .HasForeignKey(t => t.ApplicationId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ── JobApplication configuration ────────────────────────────────────────
        modelBuilder.Entity<JobApplication>(entity =>
        {
            // Store enum as string so the DB is human-readable
            entity.Property(a => a.State)
                  .HasConversion<string>();

            // Sprint 3R's agents will query JobApplication by this pair to resolve the
            // ResumeContext (task.ApplicationId -> this row -> ResumeContext lookup), mirroring
            // ResumeContext's own index on the same pair.
            entity.HasIndex(a => new { a.IngestionSessionId, a.OwnerId });
        });

        // ── ResumeContext configuration ─────────────────────────────────────────
        modelBuilder.Entity<ResumeContext>(entity =>
        {
            // Every read/delete queries by this pair together (ownership scoping), so index it.
            // Unique because ResumeContextService.SaveAsync upserts by this same pair - without a
            // DB-level constraint, a race between two concurrent saves for the same session could
            // still land two rows even though the service checks-then-acts.
            entity.HasIndex(r => new { r.IngestionSessionId, r.OwnerId }).IsUnique();
        });

        // ── Seed data ─────────────────────────────────────────────────────────
        SeedData(modelBuilder);
    }

    private static void SeedData(ModelBuilder modelBuilder)
    {
        // Seed users — passwords will be real hashes on Day 4
        modelBuilder.Entity<User>().HasData(
            new User
            {
                Id = 1,
                Name = "Andrew Van Delden",
                Email = "andrew@taskflow.dev",
                PasswordHash = "placeholder",
                CreatedAt = new DateTime(2026, 5, 14, 0, 0, 0, DateTimeKind.Utc)
            },
            new User
            {
                Id = 2,
                Name = "Demo User",
                Email = "demo@taskflow.dev",
                PasswordHash = "placeholder",
                CreatedAt = new DateTime(2026, 5, 14, 0, 0, 0, DateTimeKind.Utc)
            }
        );

        // Seed tasks spread across all statuses and priorities
        modelBuilder.Entity<TaskItem>().HasData(
            // Trivial, self-contained demo tasks the executor can actually finish: it reasons,
            // records progress, and requests review, then a human approves each to Done.
            new TaskItem
            {
                Id = 1,
                Title = "Write a haiku about autumn",
                Description = "Compose a three-line haiku (5-7-5 syllables) about the fall season.",
                Status = Models.WorkflowStatus.Todo,
                Priority = TaskPriority.Low,
                DueDate = null,
                AssignedToId = null,
                CreatedAt = new DateTime(2026, 7, 27, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2026, 7, 27, 0, 0, 0, DateTimeKind.Utc)
            },
            new TaskItem
            {
                Id = 2,
                Title = "List three uses for a paperclip",
                Description = "Give three short, creative uses for a paperclip.",
                Status = Models.WorkflowStatus.Todo,
                Priority = TaskPriority.Low,
                DueDate = null,
                AssignedToId = null,
                CreatedAt = new DateTime(2026, 7, 27, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2026, 7, 27, 0, 0, 0, DateTimeKind.Utc)
            },
            new TaskItem
            {
                Id = 3,
                Title = "Name a friendly robot",
                Description = "Suggest one name for a friendly helper robot, with a one-line reason.",
                Status = Models.WorkflowStatus.Todo,
                Priority = TaskPriority.Low,
                DueDate = null,
                AssignedToId = null,
                CreatedAt = new DateTime(2026, 7, 27, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2026, 7, 27, 0, 0, 0, DateTimeKind.Utc)
            },
            new TaskItem
            {
                Id = 4,
                Title = "Describe a to-do app in one sentence",
                Description = "Write a single clear sentence explaining what a to-do app does.",
                Status = Models.WorkflowStatus.Todo,
                Priority = TaskPriority.Low,
                DueDate = null,
                AssignedToId = null,
                CreatedAt = new DateTime(2026, 7, 27, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2026, 7, 27, 0, 0, 0, DateTimeKind.Utc)
            },
            new TaskItem
            {
                Id = 5,
                Title = "Share a fun fact about the number 7",
                Description = "Provide one interesting fact about the number seven.",
                Status = Models.WorkflowStatus.Todo,
                Priority = TaskPriority.Low,
                DueDate = null,
                AssignedToId = null,
                CreatedAt = new DateTime(2026, 7, 27, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2026, 7, 27, 0, 0, 0, DateTimeKind.Utc)
            }
        );
    }
}