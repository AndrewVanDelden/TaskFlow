using System.ComponentModel.DataAnnotations;

namespace TaskFlow.Api.Models;

public class User
{
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Email { get; set; } = string.Empty;

    // Placeholder — replaced with a real bcrypt hash on Day 4
    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation property — one User has many Tasks
    public ICollection<TaskItem> Tasks { get; set; } = new List<TaskItem>();
}