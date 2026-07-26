using Microsoft.AspNetCore.Mvc.Testing;

namespace TaskFlow.Tests.Integration;

/// <summary>
/// Boots the real API in-process for HTTP-level integration tests.
///
/// Config note: Program.cs reads Jwt:* and the connection string INLINE during builder setup
/// (before Build), so a factory ConfigureAppConfiguration override would be applied too late.
/// Environment variables are the one source CreateBuilder reads early enough, so we set them in
/// the constructor (before the host builds) to point the app at a throwaway SQLite file and
/// supply self-contained JWT settings. Environment "Testing" keeps the developer's user-secrets
/// (their real dev database) out of the test host. Program.cs already calls EnsureCreated on
/// startup, so the schema is built in the temp file automatically.
/// </summary>
public sealed class TestWebAppFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"taskflow-it-{Guid.NewGuid():N}.db");

    public TestWebAppFactory()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", $"Data Source={_dbPath}");
        Environment.SetEnvironmentVariable("Jwt__Key", "integration-test-signing-key-that-is-well-over-32-bytes-long");
        Environment.SetEnvironmentVariable("Jwt__Issuer", "taskflow-tests");
        Environment.SetEnvironmentVariable("Jwt__Audience", "taskflow-tests");
        Environment.SetEnvironmentVariable("Jwt__ExpiryHours", "8");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); }
            catch { /* best-effort cleanup */ }
        }
    }
}
