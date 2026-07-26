using TaskFlow.Api.Common;
using TaskFlow.Api.DTOs;
using TaskFlow.Api.Models;
using TaskFlow.Api.Repositories;

namespace TaskFlow.Api.Services;

/// <summary>
/// Registration and login rules. Lifted out of AuthController: email is lowercased,
/// passwords use BCrypt (work factor 12), and a failed login returns a single generic
/// message so an attacker cannot tell "no such email" from "wrong password".
/// </summary>
public class AuthService : IAuthService
{
    private readonly IUserRepository _users;
    private readonly JwtService _jwt;

    public AuthService(IUserRepository users, JwtService jwt)
    {
        _users = users;
        _jwt = jwt;
    }

    public async Task<Result<AuthResponseDto>> RegisterAsync(RegisterDto dto, CancellationToken ct = default)
    {
        var existing = await _users.GetByEmailAsync(dto.Email.ToLower(), ct);
        if (existing is not null)
            return Result<AuthResponseDto>.Conflict("An account with that email already exists.");

        var user = new User
        {
            Name = dto.Name,
            Email = dto.Email.ToLower(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password, workFactor: 12),
            CreatedAt = DateTime.UtcNow
        };

        await _users.AddAsync(user, ct);
        await _users.SaveChangesAsync(ct);

        var token = _jwt.GenerateToken(user);
        return Result<AuthResponseDto>.Ok(BuildAuthResponse(user, token));
    }

    public async Task<Result<AuthResponseDto>> LoginAsync(LoginDto dto, CancellationToken ct = default)
    {
        var user = await _users.GetByEmailAsync(dto.Email.ToLower(), ct);

        // Verify can throw on a malformed hash (e.g. old seed placeholder) — treat as a
        // failed login rather than a 500.
        var valid = false;
        if (user is not null)
        {
            try { valid = BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash); }
            catch (BCrypt.Net.SaltParseException) { valid = false; }
        }

        if (!valid)
            return Result<AuthResponseDto>.Unauthorized("Invalid email or password.");

        var token = _jwt.GenerateToken(user!);
        return Result<AuthResponseDto>.Ok(BuildAuthResponse(user!, token));
    }

    private static AuthResponseDto BuildAuthResponse(User user, TokenResult token) => new()
    {
        Token = token.Token,
        Name = user.Name,
        Email = user.Email,
        ExpiresAt = token.ExpiresAt
    };
}
