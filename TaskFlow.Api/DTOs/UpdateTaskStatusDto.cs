using System.ComponentModel.DataAnnotations;
using TaskFlow.Api.Models;
using TaskStatus = TaskFlow.Api.Models.TaskStatus;

namespace TaskFlow.Api.DTOs;

public class UpdateTaskStatusDto
{
    [Required]
    public TaskStatus Status { get; set; }
}