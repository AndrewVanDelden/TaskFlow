using TaskFlow.Api.Common;
using TaskFlow.Api.DTOs;

namespace TaskFlow.Api.Services;

public interface IAuthService
{
    Task<Result<AuthResponseDto>> RegisterAsync(RegisterDto dto, CancellationToken ct = default);
    Task<Result<AuthResponseDto>> LoginAsync(LoginDto dto, CancellationToken ct = default);
}