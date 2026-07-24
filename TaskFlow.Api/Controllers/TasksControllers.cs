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

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? status, [FromQuery] string? priority) =>
        (await _tasks.GetAllAsync(status, priority)).ToActionResult();

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

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id) =>
        (await _tasks.DeleteAsync(id)).ToActionResult();
}