namespace TaskFlow.Api.Models;

public class JobApplication
{
    public int Id { get; set; }

    public ApplicationState State { get; set; } = ApplicationState.Building;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation property — one JobApplication has many Tasks (the resume + cover-letter siblings)
    public ICollection<TaskItem> Tasks { get; set; } = new List<TaskItem>();
}
