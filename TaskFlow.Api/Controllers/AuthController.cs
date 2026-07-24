using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Data;
using TaskFlow.Api.DTOs;
using TaskFlow.Api.Models;
using TaskFlow.Api.Services;
using TaskFlow.Api.Common;

namespace TaskFlow.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly JwtService _jwtService;
    private readonly IConfiguration _config;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        AppDbContext db,
        JwtService jwtService,
        IConfiguration config,
        ILogger<AuthController> logger)
    {
        _db = db;
        _jwtService = jwtService;
        _config = config;
        _logger = logger;
    }

    // ── POST /api/auth/register ───────────────────────────────────────────────
    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto)
    {
        // Check if email is already taken
        var emailTaken = await _db.Users.AnyAsync(u => u.Email == dto.Email.ToLower());
        if (emailTaken)
            return Conflict(new { message = "An account with that email already exists." });

        // Hash the password — BCrypt handles the salt automatically
        // The "12" is the work factor — higher = slower to crack, slower to compute
        // 12 is the industry standard for 2024+
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password, workFactor: 12);

        var user = new User
        {
            Name = dto.Name,
            Email = dto.Email.ToLower(),
            PasswordHash = passwordHash,
            CreatedAt = DateTime.UtcNow
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        _logger.LogInformation("New user registered: {Email}", user.Email);

        var result = _jwtService.GenerateToken(user);
        return CreatedAtAction(nameof(Register), BuildAuthResponse(user, result));
    }

    // ── POST /api/auth/login ──────────────────────────────────────────────────
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Email == dto.Email.ToLower());

        // Always use the same error message for "user not found" and "wrong password"
        // Telling attackers which one is wrong helps them enumerate valid emails
        if (user is null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            return Unauthorized(new { message = "Invalid email or password." });

        _logger.LogInformation("User logged in: {Email}", user.Email);

        var result = _jwtService.GenerateToken(user);
        return Ok(BuildAuthResponse(user, result));
    }

    // new private helper — one place that maps a user + token into the response DTO:
    private static AuthResponseDto BuildAuthResponse(User user, TokenResult token) => new()
    {
        Token = token.Token,
        Name = user.Name,
        Email = user.Email,
        ExpiresAt = token.ExpiresAt
    };
}