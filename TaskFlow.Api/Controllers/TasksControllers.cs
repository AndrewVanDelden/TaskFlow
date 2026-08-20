using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Api.Common;
using TaskFlow.Api.DTOs;
using TaskFlow.Api.Services;

namespace TaskFlow.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TasksController : ControllerBase
{
    private readonly ITaskService _tasks;
    public TasksController(ITaskService tasks) => _tasks = tasks;

    // Scoped by caller: Epic 3 sibling tasks (personal résumé/cover-letter content) are visible
    // only to their owner; generic tasks stay visible to everyone (PR #45 review finding - see
    // ITaskRepository.GetAllAsync).
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? status, [FromQuery] string? priority, [FromQuery] bool archived = false)
    {
        if (!this.TryGetCurrentUserId(out var callerId))
            return this.UnauthenticatedIdentity();

        return (await _tasks.GetAllAsync(status, priority, archived, callerId)).ToActionResult();
    }

    // Scoped by caller (T5.0): mirrors GetAll - an Epic 3 sibling task is visible only to its
    // JobApplication's owner; generic tasks stay visible to everyone.
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        if (!this.TryGetCurrentUserId(out var callerId))
            return this.UnauthenticatedIdentity();

        return (await _tasks.GetByIdAsync(id, callerId)).ToActionResult();
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTaskDto dto) =>
        (await _tasks.CreateAsync(dto)).ToActionResult();

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateTaskDto dto)
    {
        if (!this.TryGetCurrentUserId(out var callerId))
            return this.UnauthenticatedIdentity();

        return (await _tasks.UpdateAsync(id, dto, callerId)).ToActionResult();
    }

    [HttpPatch("{id:int}/status")]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateTaskStatusDto dto)
    {
        if (!this.TryGetCurrentUserId(out var callerId))
            return this.UnauthenticatedIdentity();

        return (await _tasks.UpdateStatusAsync(id, dto, callerId)).ToActionResult();
    }

    // Human sign-off: Review -> Done. The agent path can never reach Done.
    [HttpPost("{id:int}/approve")]
    public async Task<IActionResult> Approve(int id)
    {
        if (!this.TryGetCurrentUserId(out var callerId))
            return this.UnauthenticatedIdentity();

        return (await _tasks.ApproveAsync(id, callerId)).ToActionResult();
    }

    // Human rejection: Review -> Todo with a reason (rework).
    [HttpPost("{id:int}/reject")]
    public async Task<IActionResult> Reject(int id, [FromBody] RejectTaskDto dto)
    {
        if (!this.TryGetCurrentUserId(out var callerId))
            return this.UnauthenticatedIdentity();

        return (await _tasks.RejectAsync(id, dto.Reason, callerId)).ToActionResult();
    }

    // Board Done-column "archive" action: soft-archives a single Done task so it drops off the
    // default board view but stays restorable via /unarchive.
    [HttpPost("{id:int}/archive")]
    public async Task<IActionResult> Archive(int id)
    {
        if (!this.TryGetCurrentUserId(out var callerId))
            return this.UnauthenticatedIdentity();

        return (await _tasks.ArchiveAsync(id, callerId)).ToActionResult();
    }

    // Restores a previously archived task back to the default board view.
    [HttpPost("{id:int}/unarchive")]
    public async Task<IActionResult> Unarchive(int id)
    {
        if (!this.TryGetCurrentUserId(out var callerId))
            return this.UnauthenticatedIdentity();

        return (await _tasks.UnarchiveAsync(id, callerId)).ToActionResult();
    }

    // Board Done-column "clear all" bulk action: archives every Done task the caller can see.
    [HttpPost("archive-done")]
    public async Task<IActionResult> ArchiveDone()
    {
        if (!this.TryGetCurrentUserId(out var callerId))
            return this.UnauthenticatedIdentity();

        var result = await _tasks.ArchiveAllDoneAsync(callerId);
        if (!result.IsSuccess)
            return result.ToActionResult();

        return Ok(new { archivedCount = result.Value });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (!this.TryGetCurrentUserId(out var callerId))
            return this.UnauthenticatedIdentity();

        return (await _tasks.DeleteAsync(id, callerId)).ToActionResult();
    }
}