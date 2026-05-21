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

            // Relationship: Task -> User (optional assignment)
            entity.HasOne(t => t.AssignedTo)
                  .WithMany(u => u.Tasks)
                  .HasForeignKey(t => t.AssignedToId)
                  .OnDelete(DeleteBehavior.SetNull);
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
            new TaskItem
            {
                Id = 1,
                Title = "Set up CI/CD pipeline",
                Description = "Configure GitHub Actions to run tests and deploy to Azure on push to main.",
                Status = Models.TaskStatus.Todo,
                Priority = TaskPriority.High,
                DueDate = new DateTime(2026, 5, 20, 0, 0, 0, DateTimeKind.Utc),
                AssignedToId = 1,
                CreatedAt = new DateTime(2026, 5, 14, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2026, 5, 14, 0, 0, 0, DateTimeKind.Utc)
            },
            new TaskItem
            {
                Id = 2,
                Title = "Design database schema",
                Description = "Finalize the entity relationships and run migrations.",
                Status = Models.TaskStatus.Done,
                Priority = TaskPriority.High,
                DueDate = new DateTime(2026, 5, 14, 0, 0, 0, DateTimeKind.Utc),
                AssignedToId = 1,
                CreatedAt = new DateTime(2026, 5, 13, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2026, 5, 14, 0, 0, 0, DateTimeKind.Utc)
            },
            new TaskItem
            {
                Id = 3,
                Title = "Write API integration tests",
                Description = "Cover CRUD endpoints and auth flows with xUnit.",
                Status = Models.TaskStatus.InProgress,
                Priority = TaskPriority.Medium,
                DueDate = new DateTime(2026, 5, 18, 0, 0, 0, DateTimeKind.Utc),
                AssignedToId = 2,
                CreatedAt = new DateTime(2026, 5, 14, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2026, 5, 14, 0, 0, 0, DateTimeKind.Utc)
            },
            new TaskItem
            {
                Id = 4,
                Title = "Build Kanban board UI",
                Description = "React drag-and-drop board with columns for each workflow state.",
                Status = Models.TaskStatus.Todo,
                Priority = TaskPriority.Medium,
                DueDate = new DateTime(2026, 5, 21, 0, 0, 0, DateTimeKind.Utc),
                AssignedToId = null,
                CreatedAt = new DateTime(2026, 5, 14, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2026, 5, 14, 0, 0, 0, DateTimeKind.Utc)
            },
            new TaskItem
            {
                Id = 5,
                Title = "Add JWT authentication",
                Description = "Register and login endpoints, protect all task routes.",
                Status = Models.TaskStatus.Review,
                Priority = TaskPriority.High,
                DueDate = new DateTime(2026, 5, 17, 0, 0, 0, DateTimeKind.Utc),
                AssignedToId = 1,
                CreatedAt = new DateTime(2026, 5, 13, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2026, 5, 14, 0, 0, 0, DateTimeKind.Utc)
            }
        );
    }
}