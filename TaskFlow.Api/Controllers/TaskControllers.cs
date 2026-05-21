using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Data;
using TaskFlow.Api.DTOs;
using TaskFlow.Api.Models;
using TaskStatus = TaskFlow.Api.Models.TaskStatus;

namespace TaskFlow.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TasksController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<TasksController> _logger;

    public TasksController(AppDbContext db, ILogger<TasksController> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ── GET /api/tasks ────────────────────────────────────────────────────────
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<TaskResponseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? status,
        [FromQuery] string? priority)
    {
        var query = _db.Tasks
            .Include(t => t.AssignedTo)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<TaskStatus>(status, ignoreCase: true, out var parsedStatus))
                return BadRequest(new
                {
                    message = $"Invalid status '{status}'.",
                    validValues = Enum.GetNames<TaskStatus>()
                });

            query = query.Where(t => t.Status == parsedStatus);
        }

        if (!string.IsNullOrWhiteSpace(priority))
        {
            if (!Enum.TryParse<TaskPriority>(priority, ignoreCase: true, out var parsedPriority))
                return BadRequest(new
                {
                    message = $"Invalid priority '{priority}'.",
                    validValues = Enum.GetNames<TaskPriority>()
                });

            query = query.Where(t => t.Priority == parsedPriority);
        }

        var tasks = await query
            .OrderBy(t => t.DueDate)
            .ThenBy(t => t.Priority)
            .Select(t => TaskResponseDto.FromEntity(t))
            .ToListAsync();

        return Ok(tasks);
    }
    // ── GET /api/tasks/{id} ───────────────────────────────────────────────────
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(TaskResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id)
    {
        var task = await _db.Tasks
            .Include(t => t.AssignedTo)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (task is null)
            return NotFound(new { message = $"Task {id} not found." });

        return Ok(TaskResponseDto.FromEntity(task));
    }

    // ── POST /api/tasks ───────────────────────────────────────────────────────
    [HttpPost]
    [ProducesResponseType(typeof(TaskResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateTaskDto dto)
    {
        // Validate AssignedToId if provided
        if (dto.AssignedToId.HasValue)
        {
            var userExists = await _db.Users.AnyAsync(u => u.Id == dto.AssignedToId.Value);
            if (!userExists)
                return BadRequest(new { message = $"User {dto.AssignedToId} does not exist." });
        }

        var task = new TaskItem
        {
            Title = dto.Title,
            Description = dto.Description,
            Priority = dto.Priority,
            DueDate = dto.DueDate,
            AssignedToId = dto.AssignedToId,
            Status = TaskStatus.Todo,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Tasks.Add(task);
        await _db.SaveChangesAsync();

        // Reload with navigation property so the response includes AssignedToName
        await _db.Entry(task).Reference(t => t.AssignedTo).LoadAsync();

        _logger.LogInformation("Task created: {Id} - {Title}", task.Id, task.Title);

        return CreatedAtAction(nameof(GetById), new { id = task.Id }, TaskResponseDto.FromEntity(task));
    }

    // ── PUT /api/tasks/{id} ───────────────────────────────────────────────────
    // Full update — replaces all editable fields
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(TaskResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateTaskDto dto)
    {
        var task = await _db.Tasks
            .Include(t => t.AssignedTo)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (task is null)
            return NotFound(new { message = $"Task {id} not found." });

        if (dto.AssignedToId.HasValue)
        {
            var userExists = await _db.Users.AnyAsync(u => u.Id == dto.AssignedToId.Value);
            if (!userExists)
                return BadRequest(new { message = $"User {dto.AssignedToId} does not exist." });
        }

        task.Title = dto.Title;
        task.Description = dto.Description;
        task.Status = dto.Status;
        task.Priority = dto.Priority;
        task.DueDate = dto.DueDate;
        task.AssignedToId = dto.AssignedToId;
        task.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await _db.Entry(task).Reference(t => t.AssignedTo).LoadAsync();

        return Ok(TaskResponseDto.FromEntity(task));
    }

    // ── PATCH /api/tasks/{id}/status ──────────────────────────────────────────
    // Partial update — only moves the workflow status.
    // This is the endpoint the Kanban board will call on Day 8 (drag-and-drop).
    [HttpPatch("{id:int}/status")]
    [ProducesResponseType(typeof(TaskResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateTaskStatusDto dto)
    {
        var task = await _db.Tasks
            .Include(t => t.AssignedTo)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (task is null)
            return NotFound(new { message = $"Task {id} not found." });

        var previousStatus = task.Status;
        task.Status = dto.Status;
        task.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Task {Id} status changed: {From} -> {To}",
            task.Id, previousStatus, dto.Status);

        return Ok(TaskResponseDto.FromEntity(task));
    }

    // ── DELETE /api/tasks/{id} ────────────────────────────────────────────────
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id)
    {
        var task = await _db.Tasks.FindAsync(id);

        if (task is null)
            return NotFound(new { message = $"Task {id} not found." });

        _db.Tasks.Remove(task);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Task deleted: {Id}", id);

        return NoContent();
    }
}