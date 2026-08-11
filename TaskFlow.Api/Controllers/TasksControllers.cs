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
    public async Task<IActionResult> GetAll([FromQuery] string? status, [FromQuery] string? priority)
    {
        if (!this.TryGetCurrentUserId(out var callerId))
            return this.UnauthenticatedIdentity();

        return (await _tasks.GetAllAsync(status, priority, callerId)).ToActionResult();
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id) =>
        (await _tasks.GetByIdAsync(id)).ToActionResult();

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTaskDto dto) =>
        (await _tasks.CreateAsync(dto)).ToActionResult();

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateTaskDto dto) =>
        (await _tasks.UpdateAsync(id, dto)).ToActionResult();

    [HttpPatch("{id:int}/status")]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateTaskStatusDto dto) =>
        (await _tasks.UpdateStatusAsync(id, dto)).ToActionResult();

    // Human sign-off: Review -> Done. The agent path can never reach Done.
    [HttpPost("{id:int}/approve")]
    public async Task<IActionResult> Approve(int id) =>
        (await _tasks.ApproveAsync(id)).ToActionResult();

    // Human rejection: Review -> Todo with a reason (rework).
    [HttpPost("{id:int}/reject")]
    public async Task<IActionResult> Reject(int id, [FromBody] RejectTaskDto dto) =>
        (await _tasks.RejectAsync(id, dto.Reason)).ToActionResult();

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id) =>
        (await _tasks.DeleteAsync(id)).ToActionResult();
}