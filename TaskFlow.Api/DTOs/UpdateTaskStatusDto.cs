using System.ComponentModel.DataAnnotations;
using TaskFlow.Api.Models;

namespace TaskFlow.Api.DTOs;

public class UpdateTaskStatusDto
{
    [Required]
    public WorkflowStatus Status { get; set; }
}