# TaskFlow — Architecture & TDD Refactor

This is a standalone effort layered on top of the working app. Goal: introduce a
clean layered architecture (controllers → services → repositories → EF), a real
test suite, and a Test-Driven Development workflow you follow for all code going
forward — without ever leaving the app in a broken state.

**Guiding rule:** the app builds and runs at the end of every slice. We never tear
the house down. We add rooms while people are still living in it.

---

## ▶ RESUME HERE (current status)

- **Branch:** `feature/slice-c-repositories`
- **Done:** Slice A (test harness) and Slice B (JwtService → `TokenResult`, AuthController
  deduped) are complete. The `TaskStatus` → `WorkflowStatus` rename is complete across all
  files (see Naming Conventions below).
- **In progress:** Slice C — repository layer. `ITaskRepository`/`TaskRepository` exist;
  `TaskRepositoryTests` exists. Finish the remaining repositories (`IUserRepository`,
  `IAgentLogRepository`) + tests, register in DI, then PR.
- **Next after C:** Slice D (service layer).
- **How to work:** follow this document top to bottom. Each slice has explicit file paths,
  RED tests, GREEN code, a pastable PR description, and merge/delete steps. Bring bugs to
  chat; every fix gets recorded back into this document.
- **Standing rules that were violated once and must not be again:** domain types never
  reuse .NET BCL names (see Naming Conventions); fix collisions by renaming at the source,
  not aliasing; result types live in `Common/`.

---

> **Baseline note (important):** The starting point for this refactor is the **sprint-8
> merge** (PR #6). An earlier "Chunk 1" experiment that extracted a `ClaudeAgentBase`
> class was reset away during a git cleanup and is NOT in the baseline. That is fine —
> the base-class extraction is folded into **Slice F**, done test-first there, which
> supersedes it. Do not try to hand-restore Chunk 1.

---

## Part 1 — The Target Architecture (the "why")

Right now a controller does three jobs: receives the web request, runs the business
rules, and queries the database. Three responsibilities in one class is exactly what
SOLID's Single Responsibility Principle warns against, and it is why the logic is
hard to test — you cannot exercise a rule without faking a whole web request and a
live database.

We are splitting those jobs into layers. The restaurant analogy:

```
Request →  Controller  →  Service   →  Repository  →  EF Core / DbContext  →  DB
           (waiter)       (chef)        (pantry clerk)  (the pantry)
```

- **Controller (waiter):** takes the HTTP request, returns the HTTP response. No
  cooking. Knows nothing about business rules or SQL.
- **Service (chef):** owns the business rules. "A task cannot be assigned to a user
  who does not exist." "Registration fails if the email is taken." This is where
  decisions live.
- **Repository (pantry clerk):** the only code that talks to EF. Fetch and store,
  nothing more. Knows no rules.
- **EF Core / DbContext (the pantry):** the actual data store.

### Why each layer earns its place

**Dependency Inversion (the D in SOLID).** Each layer depends on an *interface* of
the layer below, not the concrete class. The controller depends on `ITaskService`,
not `TaskService`. The service depends on `ITaskRepository`, not the EF class. This
is what makes testing possible: in a test you hand the service a *fake* repository
and check the service's decisions without any database at all.

**Single Responsibility.** Each class has one reason to change. Change a validation
rule → touch a service. Change how data is stored → touch a repository. Change an
HTTP route → touch a controller. They stop bleeding into each other.

### The folder shape we are building toward

```
TaskFlow.Api/
├── Controllers/        (thin — HTTP only)
├── Services/
│   ├── ITaskService.cs        + TaskService.cs
│   ├── IAuthService.cs        + AuthService.cs
│   └── (JwtService, IAgentNotifier already here)
├── Repositories/
│   ├── ITaskRepository.cs     + TaskRepository.cs
│   ├── IUserRepository.cs     + UserRepository.cs
│   └── IAgentLogRepository.cs + AgentLogRepository.cs
├── Data/ Models/ DTOs/ Agents/ Hubs/ Configuration/   (as today)

TaskFlow.Tests/         (NEW — the test project)
├── Services/           (unit tests: mock the repositories)
├── Repositories/       (integration tests: real SQLite in-memory)
└── Agents/             (tool handlers: SQLite in-memory, Claude mocked)
```

---

## Naming Conventions (standing rules)

These apply to all code from here on:

1. **A domain type must never reuse a name from the .NET base class library.** Names like
   `TaskStatus` (which shadows `System.Threading.Tasks.TaskStatus`) force a `using` alias
   in every file and are a landmine when one is forgotten. Pick a domain name instead.
   - Applied: the workflow enum was renamed `TaskStatus` → **`WorkflowStatus`**. All alias
     band-aids were deleted. `TaskPriority` has no BCL clash and kept its name.
   - No EF migration was needed: the enum is stored as a string and the value names
     (`Todo`/`InProgress`/`Review`/`Done`) did not change — only the C# type name did.
2. **Result/return types live in `TaskFlow.Api/Common/`**, not `Services/` (e.g.
   `TokenResult`, `Result<T>`). `Services/` is for classes with behavior.
3. Fix name collisions by renaming at the source, not by aliasing around them.

## Part 2 — The TDD Loop (the workflow you keep)

Test-Driven Development is three steps, repeated forever:

```
   ┌─────────────────────────────────────────────┐
   │  1. RED    Write a test for behavior that    │
   │            does not exist yet. Run it.       │
   │            It fails (often won't compile).   │
   │                                              │
   │  2. GREEN  Write the SIMPLEST code that      │
   │            makes the test pass. No more.     │
   │            Run it. It passes.                │
   │                                              │
   │  3. REFACTOR  Clean up the code you just     │
   │               wrote, tests still green.      │
   └─────────────────────────────────────────────┘
```

**Why write the test first?** Three reasons that matter:

1. It forces you to define "done" before you build. The test *is* the spec.
2. It guarantees the test actually tests something. A test written after the code
   often just rubber-stamps whatever the code already does, bugs included.
3. It gives you a safety net. Once green, you can refactor fearlessly — if you break
   behavior, a test goes red immediately.

**You run the tests, I write them.** Because tooling runs on your machine, our loop
is: I write a failing test → you run `dotnet test` and confirm RED → I write the code
→ you run `dotnet test` and confirm GREEN. Seeing red first is not a formality; it
proves the test can fail, so a later pass means something.

### The test stack

- **xUnit** — the test framework. `[Fact]` = one test. `[Theory]` = one test run with
  many inputs.
- **Moq** — builds fake implementations of interfaces on the fly, so a service test
  can run without a real repository.
- **FluentAssertions** — readable assertions: `result.Should().Be(5)` instead of
  `Assert.Equal(5, result)`. Failure messages are far clearer.
- **EF Core SQLite in-memory** — a real, disposable SQLite database living in RAM for
  tests that must exercise actual queries (repositories, agent handlers).

Rule of thumb for which to use:
- **No database involved** (JwtService, a pure rule) → mock the dependencies.
- **Database involved** (repositories, agent tool handlers) → SQLite in-memory.

---

## Part 3 — Slice Plan

Each slice leaves the app building and every test green. Order matters: we build the
harness first, learn the loop on the cheapest possible unit, then work up.

| Slice | What | Teaches |
|-------|------|---------|
| **A** | Scaffold `TaskFlow.Tests`, one trivial passing test | The harness runs |
| **B** | `JwtService` returns token + expiry, test-first | The red-green-refactor loop, no DB |
| **C** | Repository layer + interfaces + EF impls | Data access behind seams, SQLite in-memory tests |
| **D** | Service layer, rules moved out of controllers | Business logic test-first with mocked repos |
| **E** | Controllers depend only on services (thin) | Controllers as pure HTTP adapters |
| **F** | Agents depend on repositories, handlers tested | Testing the agent tool handlers |
| **G** | Final DRY/SOLID, docs, security patch | Whole solution builds, all green |

---

## Git Hygiene (resolved issue — recorded for reference)

Early in the refactor two problems showed up and were fixed:

1. **Work was uncommitted on `develop`** instead of on a feature branch. Fix: create the
   feature branch (uncommitted changes travel with you on `git checkout -b`), commit
   there, PR into `develop`. Never commit refactor work directly to `develop` or `main`.

2. **A line-ending flip (CRLF↔LF) made all ~40 files show as "modified"** when only two
   had real changes. Root cause: no `.gitattributes` and `core.autocrlf` unset. Fixes:
   - Added a repo-root `.gitattributes` with `* text=auto` (plus explicit text/binary
     types) so the repo stores LF and Git converts per platform. This prevents recurrence.
   - Reverted the noise files, keeping only real edits:
     ```powershell
     git diff --name-only --diff-filter=M |
       Where-Object { $_ -notmatch 'StaleTaskAgent|TaskPrioritizerAgent' } |
       ForEach-Object { git checkout -- $_ }
     ```
   - To tell noise from a real change on any file:
     `git diff --ignore-all-space -- <file>` — empty output means the diff is pure
     whitespace/line-ending noise and can be safely reverted.

**Also renamed** `taskflow-web/` → `TaskFlow.Web/` in this same cleanup commit to match
the C# project naming convention. Renaming the folder is not enough — two internal names
also carried the old value and were updated:
- `TaskFlow.Web/index.html` `<title>` → `TaskFlow` (this is what the browser tab shows).
- `TaskFlow.Web/package.json` `"name"` → `taskflow-client` (npm names must be lowercase,
  so it cannot literally be `TaskFlow.Web`).

3. **The `TaskFlow.Web` source went missing** after a reset (the rename commit was not in
   the reset branch), leaving only `node_modules`/`dist` on disk. Recovered intact from
   the rename commit, which has all 30 source files under the correct path and no
   `node_modules`:
   ```powershell
   git checkout 6ca203d -- TaskFlow.Web
   git commit -m "fix(web): restore TaskFlow.Web source"
   ```
   Recovery point commit: `6ca203d` ("rename web to TaskFlow.Web"). If the web source ever
   vanishes again, restore from there.

---

## Slice A — Scaffold the Test Project

### A0. Recreate `develop` from `main` (one-time)

After the earlier git cleanup, only `main` remains and it holds all committed code
(including the Chunk 1 agent refactor and the `TaskFlow.Web` rename). Recreate the
integration branch from it, and protect it so an auto-delete can never remove it again.

```bash
cd C:\Users\Sirgimp\Desktop\TaskFlow
git checkout main
git pull origin main
git checkout -b develop
git push -u origin develop
```

Then protect both branches on GitHub so they cannot be deleted. GitHub has two UIs:

- **Rulesets** (Settings → Rules → Rulesets): enable **Restrict deletions**.
- **Classic branch protection** (Settings → Branches → Add rule): there is no explicit
  "deny delete" box. Instead, leave **"Allow deletions" unchecked** at the bottom.
  Unchecked = deletion blocked. Save.

Either way, having ANY protection rule on a branch also makes GitHub's
auto-delete-head-branch feature skip it — which is what stops a `develop → main` merge
from deleting `develop`. Skip required reviews — you are solo.

### A1. Branch for this slice

Per-slice PRs from here on. Each slice gets its own branch off `develop`:

```bash
git checkout develop
git pull origin develop
git checkout -b feature/slice-a-test-harness
```

### A2. Confirm the app builds first

The test project references the API project, so the API must compile. This also
confirms the Chunk 1 agent refactor is green.

```bash
cd TaskFlow.Api
dotnet build
```

If that is not clean, stop and fix it before continuing.

### A3. Create the test project

```bash
cd C:\Users\Sirgimp\Desktop\TaskFlow

# xUnit test project
dotnet new xunit -n TaskFlow.Tests

# Add it to the solution
dotnet sln TaskFlow.slnx add TaskFlow.Tests/TaskFlow.Tests.csproj

# Reference the API project so tests can see its classes.
# CRITICAL: without this, every test errors with CS0234 "TaskFlow.Api does not exist".
# Verify afterward that TaskFlow.Tests.csproj contains a <ProjectReference> to TaskFlow.Api.
dotnet add TaskFlow.Tests reference TaskFlow.Api

# Test-only packages
cd TaskFlow.Tests
dotnet add package Moq

# FluentAssertions v8+ is under a paid Xceed license (prints a nag on every run).
# v7.x is the last free (Apache) release and has the identical .Should() API.
# Pin to 7.x — after installing, open TaskFlow.Tests.csproj and set the version to:
#   <PackageReference Include="FluentAssertions" Version="7.*" />
dotnet add package FluentAssertions

dotnet add package Microsoft.EntityFrameworkCore.Sqlite

# EF Core Sqlite pulls a vulnerable native lib (NU1903). Add the patched lib directly
# to override it, same as the API project.
dotnet add package SQLitePCLRaw.lib.e_sqlite3

# then pin FluentAssertions to 7.* in the csproj (see note above) and:
cd ..
dotnet restore
```

**Two warnings this clears (recorded during Slice A):**
- `NU1903` — vulnerable `SQLitePCLRaw.lib.e_sqlite3` pulled transitively by EF Core
  Sqlite. Fixed by the direct `SQLitePCLRaw.lib.e_sqlite3` reference above.
- FluentAssertions license nag — fixed by pinning to `7.*` (last free version).

**Teaching note.** The test project is a separate assembly that *references* the API.
It can see any `public` type in the API. That is one reason interfaces and services
are public: so tests can construct and exercise them.

### A4. Delete the template's placeholder test

`dotnet new xunit` drops a `UnitTest1.cs`. Delete it:

```bash
del UnitTest1.cs
```

### A4b. Add a proper root `.gitignore` (do this BEFORE `git add`)

The repo had no working `.gitignore`, which caused `node_modules` (6,500+ files),
`bin/`, `obj/`, and `.env.local` to get committed and pushed (a 67 MiB push). Prevent
it with a root `.gitignore` covering .NET + Node/Vite + env + SQLite:

```gitignore
# .NET
bin/
obj/
*.user
.vs/

# Node / Vite
node_modules/
dist/
*.local
.vite/

# Env / secrets
.env
.env.*
!.env.example
appsettings.Secrets.json

# SQLite local db
*.db
*.db-shm
*.db-wal

# IDE / OS
.idea/
.DS_Store
Thumbs.db

# Test / coverage
[Tt]est[Rr]esults/
coverage/
*.trx
```

If junk was already committed, untrack it (keeps files on disk) before committing:

```powershell
git rm -r taskflow-web              # remove the pre-rename duplicate folder
git rm -r --cached . --quiet        # unstage everything
git add .                           # re-add only non-ignored files
# verify: these should print 0
git ls-files "TaskFlow.Web/node_modules/*" | Measure-Object | Select Count
git ls-files "*/bin/*" "*/obj/*"           | Measure-Object | Select Count
```

### A5. Establish the test folder structure + prove the harness runs

The test project mirrors the API's folders so a test's location tells you what it covers.
Create these folders now, even though most fill up in later slices:

```
TaskFlow.Tests/
├── Services/        (JwtService, TaskService, AuthService tests)
├── Repositories/    (repository integration tests)
├── Controllers/     (controller tests)
├── Agents/          (agent tool-handler tests)
└── TestSupport/     (shared fixtures, e.g. SqliteInMemoryContext)
```

The smoke test is the one exception — it is a throwaway that only proves the runner
works, and it is deleted at the very start of Slice B. To keep it from masquerading as a
real test, put it in `TaskFlow.Tests/TestSupport/`:

Create `TaskFlow.Tests/TestSupport/HarnessSmokeTest.cs`:

```csharp
using FluentAssertions;
using Xunit;

namespace TaskFlow.Tests.TestSupport;

// TEMPORARY: proves the runner, xUnit, and FluentAssertions are wired up.
// Deleted in Slice B1 once the first real test exists.
public class HarnessSmokeTest
{
    [Fact]
    public void Harness_is_working()
    {
        var sum = 2 + 2;
        sum.Should().Be(4);
    }
}
```

> Note: `TaskFlow.Web` has the same "flat folder" smell you may have noticed. That is
> addressed in Slice I, which restructures it into `api/ hooks/ components/ features/ lib/`.

### A6. Run it

```bash
cd C:\Users\Sirgimp\Desktop\TaskFlow
dotnet test
```

You should see `Passed!  - Failed: 0, Passed: 1`. If you do, the harness is live and
we can start real TDD in Slice B.

### A7. Commit

```bash
git add .
git commit -m "test: scaffold TaskFlow.Tests with xUnit, Moq, FluentAssertions, SQLite"
git push -u origin feature/slice-a-test-harness
```

**PR description — paste into the PR body:**

```markdown
## What does this PR do?
Scaffolds TaskFlow.Tests (xUnit + Moq + FluentAssertions 7.x + EF SQLite in-memory),
establishes the test folder structure, adds a root .gitignore, and untracks
node_modules/bin/obj/.env.local. Baseline for the TDD refactor.

## Type of change
- [x] Tests / tooling
- [x] Chore (gitignore, project setup)

## How to test it
1. `dotnet test` -> Passed: 1 (harness smoke test)
2. `dotnet build` is clean - no NU1903, no FluentAssertions license nag

## Checklist
- [x] Builds with no warnings
- [x] node_modules / bin / obj not tracked
- [x] Committed on a feature branch
```

Then open the PR into `develop`, merge, delete the branch.

### A8. PR cadence — one PR per slice

We open a small PR at the end of every slice rather than one giant PR at the end.
Each slice is a self-contained, buildable, green improvement, which makes each PR a
clean story ("added the repository layer and its tests") and keeps reviews small.

The rhythm for every slice from here on:

```bash
# start of a slice
git checkout develop && git pull origin develop
git checkout -b feature/slice-X-short-name

# ... do the slice (red -> green -> refactor) ...

git add . && git commit -m "..."
git push -u origin feature/slice-X-short-name
# open PR into develop on GitHub, merge it, delete the branch
```

For Slice A specifically, you already branched as `feature/architecture-and-tdd`;
use that branch name for the PR, merge it, then branch fresh per slice after.

---

---

## Slice B — First Red-Green-Refactor on JwtService

**Why this unit first:** `JwtService` has no database and no other dependencies, so it
is the cheapest possible place to learn the loop. We are going to make it return the
token *and* the expiry time together, so the controller stops re-deriving the expiry
itself (a DRY fix and a Single-Responsibility fix — the service owns "how long is a
token valid").

Branch: `feature/slice-b-jwt-tokenresult`

### B1. RED — write the failing test first

The test decodes the JWT to inspect its claims, so the test project needs the JWT
library directly (the API has it transitively, but the test assembly does not):

```powershell
dotnet add TaskFlow.Tests package System.IdentityModel.Tokens.Jwt
```

> If you skip this you get `CS0246: JwtSecurityTokenHandler could not be found`.

Create `TaskFlow.Tests/Services/JwtServiceTests.cs`:

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using TaskFlow.Api.Models;
using TaskFlow.Api.Services;
using Xunit;

namespace TaskFlow.Tests.Services;

public class JwtServiceTests
{
    // Builds a JwtService backed by throwaway in-memory config — no appsettings,
    // no file system. This is "mock the dependency" in its simplest form.
    private static JwtService CreateSut(int expiryHours = 8)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "test-signing-key-at-least-32-characters-long!!",
                ["Jwt:Issuer"] = "TaskFlowApi",
                ["Jwt:Audience"] = "TaskFlowClient",
                ["Jwt:ExpiryHours"] = expiryHours.ToString()
            })
            .Build();

        return new JwtService(config);
    }

    private static User SampleUser() => new()
    {
        Id = 42,
        Name = "Ada Lovelace",
        Email = "ada@taskflow.dev",
        PasswordHash = "irrelevant"
    };

    [Fact]
    public void GenerateToken_returns_a_nonempty_token()
    {
        var sut = CreateSut();

        var result = sut.GenerateToken(SampleUser());

        result.Token.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void GenerateToken_embeds_the_user_id_and_email_as_claims()
    {
        var sut = CreateSut();

        var result = sut.GenerateToken(SampleUser());

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        jwt.Claims.Should().Contain(c =>
            c.Type == ClaimTypes.NameIdentifier && c.Value == "42");
        jwt.Claims.Should().Contain(c =>
            c.Type == ClaimTypes.Email && c.Value == "ada@taskflow.dev");
    }

    [Fact]
    public void GenerateToken_sets_expiry_to_configured_hours_from_now()
    {
        var sut = CreateSut(expiryHours: 8);
        var before = DateTime.UtcNow;

        var result = sut.GenerateToken(SampleUser());

        // Allow a minute of slack so the test is not flaky on a slow machine.
        result.ExpiresAt.Should().BeCloseTo(before.AddHours(8), TimeSpan.FromMinutes(1));
    }
}
```

```bash
dotnet test
```

**Expect RED.** It will not even compile: `GenerateToken` currently returns a `string`,
so `result.Token` and `result.ExpiresAt` do not exist. A compile failure is a valid
red — the test is describing an API that does not exist yet.

### B2. GREEN — write the simplest code to pass

Every code block below names the **exact file** it goes in. Result-style types live in
`Common/` (this is also where `Result<T>` lands in Slice D), keeping `Services/` for
classes that have behavior — a Single-Responsibility split.

**FILE — create new: `TaskFlow.Api/Common/TokenResult.cs`**

```csharp
namespace TaskFlow.Api.Common;

/// <summary>A freshly minted JWT and the moment it expires.</summary>
public record TokenResult(string Token, DateTime ExpiresAt);
```

**FILE — edit existing: `TaskFlow.Api/Services/JwtService.cs`**

First add this using near the top so the service can see the type from `Common/`:

```csharp
using TaskFlow.Api.Common;
```

Then replace the whole `GenerateToken` method (the one that returns `string`) with the
version below, which returns both the token and the expiry:

```csharp
// Returns the token AND its expiry together, so callers stop re-deriving the expiry.
public TokenResult GenerateToken(User user)
{
    var claims = new[]
    {
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim(ClaimTypes.Email, user.Email),
        new Claim(ClaimTypes.Name, user.Name)
    };

    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
    var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

    var expiresAt = DateTime.UtcNow.AddHours(
        double.Parse(_config["Jwt:ExpiryHours"] ?? "8"));

    var token = new JwtSecurityToken(
        issuer: _config["Jwt:Issuer"],
        audience: _config["Jwt:Audience"],
        claims: claims,
        expires: expiresAt,
        signingCredentials: credentials);

    return new TokenResult(
        new JwtSecurityTokenHandler().WriteToken(token),
        expiresAt);
}
```

This breaks `AuthController`, which still expects a string — so the project will not
build yet. That is fine; fixing the caller is part of GREEN.

### B3. GREEN — fix the caller (and DRY it while we are here)

**FILE — edit existing: `TaskFlow.Api/Controllers/AuthController.cs`**

First add this using near the top (AuthController references `TokenResult`, which lives
in `Common/`). Without it you get `CS0246: TokenResult could not be found`:

```csharp
using TaskFlow.Api.Common;
```

`AuthController.Register` and `Login` both parse the expiry and build the same DTO.
Collapse that into one helper and use the new `TokenResult`.

**In `Register`, DELETE the old token/expiry tail** — these lines:

```csharp
var token = _jwtService.GenerateToken(user);
var expiryHours = double.Parse(_config["Jwt:ExpiryHours"] ?? "8");
return CreatedAtAction(nameof(Register), new AuthResponseDto { ... });
```

**and REPLACE with:**

```csharp
var result = _jwtService.GenerateToken(user);
return CreatedAtAction(nameof(Register), BuildAuthResponse(user, result));
```

**In `Login`, DELETE the old token/expiry tail:**

```csharp
var token = _jwtService.GenerateToken(user);
var expiryHours = double.Parse(_config["Jwt:ExpiryHours"] ?? "8");
return Ok(new AuthResponseDto { ... });
```

**and REPLACE with:**

```csharp
var result = _jwtService.GenerateToken(user);
return Ok(BuildAuthResponse(user, result));
```

> Critical: actually DELETE the old `var token` and `var expiryHours` lines. If you only
> add the new lines and leave the old ones, `_config` still shows references and will not
> be removable in the next step.

**Add the shared helper** (one place that maps a user + token into the response DTO):

```csharp
private static AuthResponseDto BuildAuthResponse(User user, TokenResult token) => new()
{
    Token = token.Token,
    Name = user.Name,
    Email = user.Email,
    ExpiresAt = token.ExpiresAt
};
```

Now `_config` is unused (the expiry parsing was its only use). Remove all three spots:
the field `private readonly IConfiguration _config;`, the `IConfiguration config,`
constructor parameter, and the `_config = config;` assignment. If the IDE still shows
references, you left an old `var expiryHours` line behind — find and delete it.

```bash
dotnet test
```

**Expect GREEN.** All three JwtService tests pass and the harness smoke test still passes.

### B4. REFACTOR

The code is already clean. Confirm nothing else references the old string return.
Tests stay green.

### B5. Commit + PR

```bash
git add .
git commit -m "refactor(auth): JwtService returns token+expiry; dedupe AuthController"
git push -u origin feature/slice-b-jwt-tokenresult
```

**PR description — paste into the PR body:**

```markdown
## What does this PR do?
JwtService now returns a TokenResult (token + expiry) instead of a bare string, so the
controller no longer re-derives the expiry. Dedupes AuthController's register/login into
one BuildAuthResponse helper. First real TDD slice.

## Type of change
- [x] Refactor
- [x] Tests

## How to test it
1. `dotnet test` -> JwtService tests pass (token non-empty, claims present, expiry correct)
2. Register + login still return a token and ExpiresAt via Swagger

## Checklist
- [x] Tests written first (red), then code (green)
- [x] Builds with no warnings
```

Open PR into `develop`, merge, delete branch. **Slice B teaches the whole loop in
miniature.** Everything after is the same rhythm on bigger units.

---

## Slice C — Repository Layer

**Goal:** introduce the "pantry clerk." Interfaces plus EF implementations for the
three entities, registered in DI, covered by SQLite in-memory tests. Controllers and
agents are NOT changed yet — the repositories exist alongside the current code so the
app keeps building. We wire callers onto them in Slices D–F.

Branch: `feature/slice-c-repositories`

### C1. A reusable SQLite in-memory fixture (test infrastructure)

Every data test needs a fresh disposable database. Write that once.

Create `TaskFlow.Tests/TestSupport/SqliteInMemoryContext.cs`:

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Data;

namespace TaskFlow.Tests.TestSupport;

/// <summary>
/// Creates an AppDbContext backed by a private in-memory SQLite database.
/// The connection is held open for the lifetime of the object; when disposed,
/// the database vanishes. Real SQLite, so foreign keys and constraints apply.
/// </summary>
public sealed class SqliteInMemoryContext : IDisposable
{
    private readonly SqliteConnection _connection;

    public AppDbContext Context { get; }

    public SqliteInMemoryContext()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        Context = new AppDbContext(options);
        Context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}
```

**Teaching note.** SQLite's in-memory database lives only as long as a connection to it
is open. That is why we open the connection ourselves and hold it — otherwise EF would
close it between calls and the schema would disappear.

### C2. RED — repository tests

Start with the task repository. Create `TaskFlow.Tests/Repositories/TaskRepositoryTests.cs`:

```csharp
using FluentAssertions;
using TaskFlow.Api.Models;
using TaskFlow.Api.Repositories;
using TaskFlow.Tests.TestSupport;
using Xunit;

namespace TaskFlow.Tests.Repositories;

public class TaskRepositoryTests
{
    [Fact]
    public async Task AddAsync_then_GetByIdAsync_roundtrips_a_task()
    {
        using var db = new SqliteInMemoryContext();
        var sut = new TaskRepository(db.Context);

        var task = new TaskItem { Title = "Write tests" };
        await sut.AddAsync(task);
        await sut.SaveChangesAsync();

        var found = await sut.GetByIdAsync(task.Id);
        found.Should().NotBeNull();
        found!.Title.Should().Be("Write tests");
    }

    [Fact]
    public async Task GetStaleAsync_returns_only_open_tasks_older_than_cutoff()
    {
        using var db = new SqliteInMemoryContext();
        var sut = new TaskRepository(db.Context);

        var cutoff = DateTime.UtcNow.AddHours(-48);
        await sut.AddAsync(new TaskItem { Title = "fresh", UpdatedAt = DateTime.UtcNow });
        await sut.AddAsync(new TaskItem { Title = "stale", UpdatedAt = cutoff.AddHours(-1) });
        await sut.AddAsync(new TaskItem { Title = "done-stale", Status = WorkflowStatus.Done, UpdatedAt = cutoff.AddHours(-1) });
        await sut.SaveChangesAsync();

        var stale = await sut.GetStaleAsync(cutoff);

        stale.Should().ContainSingle(t => t.Title == "stale");
    }
}
```

(`WorkflowStatus` is the task workflow enum; the test has `using TaskFlow.Api.Models;`
so no namespace prefix is needed. See the naming convention in Part 1.)

```bash
dotnet test
```

**Expect RED** — `TaskRepository` and `ITaskRepository` do not exist yet.

### C3. GREEN — interfaces and EF implementations

Create the interfaces and implementations under `TaskFlow.Api/Repositories/`. The
method sets below are exactly what the current controllers and agents need (I derived
them from the existing code), nothing speculative.

`ITaskRepository.cs`:

```csharp
using TaskFlow.Api.Models;

namespace TaskFlow.Api.Repositories;

public interface ITaskRepository
{
    Task<TaskItem?> GetByIdAsync(int id, bool includeAssignee = false, CancellationToken ct = default);
    Task<List<TaskItem>> GetAllAsync(WorkflowStatus? status, TaskPriority? priority, CancellationToken ct = default);
    Task<List<TaskItem>> GetOpenAsync(CancellationToken ct = default);
    Task<List<TaskItem>> GetStaleAsync(DateTime cutoff, CancellationToken ct = default);
    Task<Dictionary<int, int>> GetOpenCountsByUserAsync(CancellationToken ct = default);
    Task AddAsync(TaskItem task, CancellationToken ct = default);
    void Remove(TaskItem task);
    Task SaveChangesAsync(CancellationToken ct = default);
}
```

`TaskRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Data;
using TaskFlow.Api.Models;

namespace TaskFlow.Api.Repositories;

/// <summary>EF Core implementation of <see cref="ITaskRepository"/>.</summary>
public class TaskRepository : ITaskRepository
{
    private readonly AppDbContext _db;
    public TaskRepository(AppDbContext db) => _db = db;

    public async Task<TaskItem?> GetByIdAsync(int id, bool includeAssignee = false, CancellationToken ct = default)
    {
        var query = _db.Tasks.AsQueryable();
        if (includeAssignee) query = query.Include(t => t.AssignedTo);
        return await query.FirstOrDefaultAsync(t => t.Id == id, ct);
    }

    public async Task<List<TaskItem>> GetAllAsync(WorkflowStatus? status, TaskPriority? priority, CancellationToken ct = default)
    {
        var query = _db.Tasks.Include(t => t.AssignedTo).AsQueryable();
        if (status.HasValue)   query = query.Where(t => t.Status == status.Value);
        if (priority.HasValue) query = query.Where(t => t.Priority == priority.Value);
        return await query.OrderBy(t => t.DueDate).ThenBy(t => t.Priority).ToListAsync(ct);
    }

    public Task<List<TaskItem>> GetOpenAsync(CancellationToken ct = default) =>
        _db.Tasks.Include(t => t.AssignedTo)
            .Where(t => t.Status != WorkflowStatus.Done)
            .OrderBy(t => t.Id)
            .ToListAsync(ct);

    public Task<List<TaskItem>> GetStaleAsync(DateTime cutoff, CancellationToken ct = default) =>
        _db.Tasks.Include(t => t.AssignedTo)
            .Where(t => t.Status != WorkflowStatus.Done && t.UpdatedAt < cutoff)
            .OrderBy(t => t.UpdatedAt)
            .ToListAsync(ct);

    public async Task<Dictionary<int, int>> GetOpenCountsByUserAsync(CancellationToken ct = default) =>
        await _db.Tasks
            .Where(t => t.Status != WorkflowStatus.Done && t.AssignedToId != null)
            .GroupBy(t => t.AssignedToId!.Value)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

    public async Task AddAsync(TaskItem task, CancellationToken ct = default) =>
        await _db.Tasks.AddAsync(task, ct);

    public void Remove(TaskItem task) => _db.Tasks.Remove(task);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
```

`IUserRepository.cs` / `UserRepository.cs` — the members the code uses today.
Note the `using` directives at the top of each; without them you get `CS0246: User`
and `CS0246: AppDbContext could not be found`:

```csharp
using TaskFlow.Api.Models;

namespace TaskFlow.Api.Repositories;

public interface IUserRepository
{
    Task<bool> ExistsAsync(int id, CancellationToken ct = default);
    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<List<User>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(User user, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
```

```csharp
using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Data;
using TaskFlow.Api.Models;

namespace TaskFlow.Api.Repositories;

public class UserRepository : IUserRepository
{
    private readonly AppDbContext _db;
    public UserRepository(AppDbContext db) => _db = db;

    public Task<bool> ExistsAsync(int id, CancellationToken ct = default) =>
        _db.Users.AnyAsync(u => u.Id == id, ct);

    public Task<User?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        _db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

    public Task<List<User>> GetAllAsync(CancellationToken ct = default) =>
        _db.Users.ToListAsync(ct);

    public async Task AddAsync(User user, CancellationToken ct = default) =>
        await _db.Users.AddAsync(user, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
```

`IAgentLogRepository.cs` / `AgentLogRepository.cs`:

```csharp
using TaskFlow.Api.Models;

namespace TaskFlow.Api.Repositories;

public interface IAgentLogRepository
{
    Task<List<AgentLog>> GetRecentAsync(string? agentName, int limit, CancellationToken ct = default);
    Task<List<AgentLog>> GetTaskScopedSinceAsync(string agentName, DateTime since, int limit, CancellationToken ct = default);
    Task AddAsync(AgentLog log, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
```

```csharp
using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Data;
using TaskFlow.Api.Models;

namespace TaskFlow.Api.Repositories;

public class AgentLogRepository : IAgentLogRepository
{
    private readonly AppDbContext _db;
    public AgentLogRepository(AppDbContext db) => _db = db;

    public async Task<List<AgentLog>> GetRecentAsync(string? agentName, int limit, CancellationToken ct = default)
    {
        var query = _db.AgentLogs.AsQueryable();
        if (!string.IsNullOrWhiteSpace(agentName))
            query = query.Where(l => l.AgentName == agentName);
        return await query.OrderByDescending(l => l.CreatedAt)
            .Take(Math.Clamp(limit, 1, 200)).ToListAsync(ct);
    }

    public Task<List<AgentLog>> GetTaskScopedSinceAsync(string agentName, DateTime since, int limit, CancellationToken ct = default) =>
        _db.AgentLogs
            .Where(l => l.AgentName == agentName && l.CreatedAt > since && l.TaskId != null)
            .OrderByDescending(l => l.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

    public async Task AddAsync(AgentLog log, CancellationToken ct = default) =>
        await _db.AgentLogs.AddAsync(log, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
```

### C4. Register in DI

First add the repositories namespace to the usings at the top of `Program.cs`
(without it you get `CS0246: ITaskRepository could not be found`):

```csharp
using TaskFlow.Api.Repositories;
```

Then, near the other service registrations:

```csharp
builder.Services.AddScoped<ITaskRepository, TaskRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IAgentLogRepository, AgentLogRepository>();
```

```bash
dotnet test
```

**Expect GREEN.** Repository tests pass; nothing else changed so the app still builds
and runs exactly as before.

### C5. Commit + PR

```bash
git add .
git commit -m "feat(data): add repository layer with SQLite in-memory tests"
git push -u origin feature/slice-c-repositories
```

**PR description — paste into the PR body:**

```markdown
## What does this PR do?
Adds the repository layer: ITaskRepository / IUserRepository / IAgentLogRepository with
EF Core implementations, registered in DI. Introduces the SqliteInMemoryContext test
fixture. Controllers and agents are unchanged; the repos exist in parallel for now.

## Type of change
- [x] New feature (data layer)
- [x] Tests

## How to test it
1. `dotnet test` -> repository tests pass against real in-memory SQLite
2. `dotnet build` clean; app still runs unchanged (Swagger endpoints behave as before)

## Checklist
- [x] Tests written first (red), then code (green)
- [x] No controller/agent behavior changed this slice
```

PR into `develop`, merge, delete.

---

## Slice D — Service Layer (business logic, test-first)

**Goal:** move the decisions out of the controllers into services that depend on the
repositories. Services return a small `Result<T>` so they can report "not found" or
"conflict" without knowing anything about HTTP. Tests mock the repositories with Moq.

Branch: `feature/slice-d-services`

### D1. A transport-agnostic result type

Create `TaskFlow.Api/Common/Result.cs`:

```csharp
namespace TaskFlow.Api.Common;

public enum ResultStatus { Ok, NotFound, Conflict, Validation }

/// <summary>
/// Outcome of a service operation, free of any HTTP concept. Controllers translate
/// the status into a status code; services never reference IActionResult.
/// </summary>
public record Result<T>(ResultStatus Status, T? Value, string? Error)
{
    public bool IsSuccess => Status == ResultStatus.Ok;

    public static Result<T> Ok(T value)               => new(ResultStatus.Ok, value, null);
    public static Result<T> NotFound(string error)    => new(ResultStatus.NotFound, default, error);
    public static Result<T> Conflict(string error)    => new(ResultStatus.Conflict, default, error);
    public static Result<T> Invalid(string error)     => new(ResultStatus.Validation, default, error);
}
```

**Teaching note.** This is the Dependency Inversion payoff: the service depends on an
abstract idea of success/failure, not on ASP.NET. That is what lets you unit-test the
rules with no web server in sight.

### D2. RED — service tests (mock the repositories)

Example: creating a task must fail when the assignee does not exist. Create
`TaskFlow.Tests/Services/TaskServiceTests.cs`:

```csharp
using FluentAssertions;
using Moq;
using TaskFlow.Api.Common;
using TaskFlow.Api.DTOs;
using TaskFlow.Api.Models;
using TaskFlow.Api.Repositories;
using TaskFlow.Api.Services;
using Xunit;

namespace TaskFlow.Tests.Services;

public class TaskServiceTests
{
    private readonly Mock<ITaskRepository> _tasks = new();
    private readonly Mock<IUserRepository> _users = new();

    private TaskService CreateSut() => new(_tasks.Object, _users.Object);

    [Fact]
    public async Task Create_fails_validation_when_assignee_does_not_exist()
    {
        _users.Setup(u => u.ExistsAsync(99, It.IsAny<CancellationToken>()))
              .ReturnsAsync(false);
        var sut = CreateSut();

        var result = await sut.CreateAsync(new CreateTaskDto { Title = "x", AssignedToId = 99 });

        result.Status.Should().Be(ResultStatus.Validation);
        _tasks.Verify(t => t.AddAsync(It.IsAny<TaskItem>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_succeeds_and_defaults_status_to_Todo()
    {
        _users.Setup(u => u.ExistsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(true);
        var sut = CreateSut();

        var result = await sut.CreateAsync(new CreateTaskDto { Title = "x", AssignedToId = 1 });

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(nameof(WorkflowStatus.Todo));
        _tasks.Verify(t => t.AddAsync(It.IsAny<TaskItem>(), It.IsAny<CancellationToken>()), Times.Once);
        _tasks.Verify(t => t.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

```bash
dotnet test
```

**Expect RED** — `TaskService`/`ITaskService` do not exist.

### D3. GREEN — the service

`ITaskService.cs` and `TaskService.cs` (representative shape — final method set
confirmed as we port each endpoint):

```csharp
public interface ITaskService
{
    Task<Result<TaskResponseDto>> CreateAsync(CreateTaskDto dto, CancellationToken ct = default);
    Task<Result<TaskResponseDto>> UpdateAsync(int id, UpdateTaskDto dto, CancellationToken ct = default);
    Task<Result<TaskResponseDto>> UpdateStatusAsync(int id, UpdateTaskStatusDto dto, CancellationToken ct = default);
    Task<Result<TaskResponseDto>> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Result<IReadOnlyList<TaskResponseDto>>> GetAllAsync(string? status, string? priority, CancellationToken ct = default);
    Task<Result<bool>> DeleteAsync(int id, CancellationToken ct = default);
}
```

```csharp
public class TaskService : ITaskService
{
    private readonly ITaskRepository _tasks;
    private readonly IUserRepository _users;

    public TaskService(ITaskRepository tasks, IUserRepository users)
    {
        _tasks = tasks;
        _users = users;
    }

    public async Task<Result<TaskResponseDto>> CreateAsync(CreateTaskDto dto, CancellationToken ct = default)
    {
        if (dto.AssignedToId.HasValue && !await _users.ExistsAsync(dto.AssignedToId.Value, ct))
            return Result<TaskResponseDto>.Invalid($"User {dto.AssignedToId} does not exist.");

        var task = new TaskItem
        {
            Title = dto.Title,
            Description = dto.Description,
            Priority = dto.Priority,
            DueDate = dto.DueDate,
            AssignedToId = dto.AssignedToId,
            Status = Models.WorkflowStatus.Todo,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _tasks.AddAsync(task, ct);
        await _tasks.SaveChangesAsync(ct);

        return Result<TaskResponseDto>.Ok(TaskResponseDto.FromEntity(task));
    }

    // Update / UpdateStatus / GetById / GetAll / Delete follow the same pattern:
    // load via repository, apply the rule, return the right Result status.
}
```

Also add `IAuthService`/`AuthService` moving the register/login rules (email-taken →
Conflict, bad credentials → the generic 401 message) out of `AuthController`, using
`IUserRepository` and `JwtService`. Its tests mock `IUserRepository` and assert the
conflict/også success paths. (Full code confirmed at execution.)

Register the services in `Program.cs`:

```csharp
builder.Services.AddScoped<ITaskService, TaskService>();
builder.Services.AddScoped<IAuthService, AuthService>();
```

```bash
dotnet test
```

**Expect GREEN.** Controllers are still using the old inline logic at this point — that
is fine, the services exist in parallel. We switch the controllers over in Slice E.

### D4. Commit + PR

```bash
git add .
git commit -m "feat(services): add task and auth services with unit tests"
git push -u origin feature/slice-d-services
```

**PR description — paste into the PR body:**

```markdown
## What does this PR do?
Adds the service layer: ITaskService / IAuthService holding the business rules (assignee
must exist, email-taken -> Conflict, bad credentials -> generic 401). Introduces the
transport-agnostic Result<T> type. Services depend on repositories; tests mock the repos
with Moq. Controllers still use inline logic; they switch over in Slice E.

## Type of change
- [x] New feature (service layer)
- [x] Tests

## How to test it
1. `dotnet test` -> service tests pass (validation, conflict, success paths)
2. `dotnet build` clean; app behavior unchanged this slice

## Checklist
- [x] Tests written first (red), then code (green)
- [x] Services contain no HTTP/IActionResult references
```

PR into `develop`, merge, delete.

---

## Slice E — Thin Out the Controllers

**Goal:** controllers become pure HTTP adapters. They call a service, then translate
the `Result` status into a status code. No EF, no rules. Controller tests mock the
service and assert the HTTP mapping.

Branch: `feature/slice-e-thin-controllers`

### E1. A shared Result → IActionResult mapper (DRY)

Every controller needs the same translation, so write it once. Create
`TaskFlow.Api/Common/ResultExtensions.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;

namespace TaskFlow.Api.Common;

public static class ResultExtensions
{
    /// <summary>Maps a service Result onto the conventional HTTP status codes.</summary>
    public static IActionResult ToActionResult<T>(this Result<T> result) => result.Status switch
    {
        ResultStatus.Ok         => new OkObjectResult(result.Value),
        ResultStatus.NotFound   => new NotFoundObjectResult(new { message = result.Error }),
        ResultStatus.Conflict   => new ConflictObjectResult(new { message = result.Error }),
        ResultStatus.Validation => new BadRequestObjectResult(new { message = result.Error }),
        _                       => new StatusCodeResult(500)
    };
}
```

### E2. RED — controller test with a mocked service

Create `TaskFlow.Tests/Controllers/TasksControllerTests.cs`:

```csharp
[Fact]
public async Task Create_returns_400_when_service_reports_validation_error()
{
    var service = new Mock<ITaskService>();
    service.Setup(s => s.CreateAsync(It.IsAny<CreateTaskDto>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(Result<TaskResponseDto>.Invalid("bad"));
    var sut = new TasksController(service.Object);

    var result = await sut.Create(new CreateTaskDto { Title = "x" });

    result.Should().BeOfType<BadRequestObjectResult>();
}
```

**Expect RED** — `TasksController` still takes `AppDbContext`, not `ITaskService`.

### E3. GREEN — rewrite the controller thin

```csharp
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

    // PUT, PATCH status, DELETE follow the same one-line pattern.
}
```

Do the same for `AuthController` (inject `IAuthService`). Rename `TaskControllers.cs`
to `TasksController.cs` while we are here (filename should match the class):

```bash
git mv TaskFlow.Api/Controllers/TaskControllers.cs TaskFlow.Api/Controllers/TasksController.cs
```

```bash
dotnet test
```

**Expect GREEN**, and the app now runs with the full chain: controller → service →
repository → EF. Smoke-test the API in Swagger to confirm behavior is unchanged.

### E4. Commit + PR

```bash
git add .
git commit -m "refactor(api): thin controllers onto services; rename TasksController file"
git push -u origin feature/slice-e-thin-controllers
```

**PR description — paste into the PR body:**

```markdown
## What does this PR do?
Controllers become thin HTTP adapters: they call a service and map the Result onto a
status code via a shared ToActionResult extension. No EF or business rules left in
controllers. Renames TaskControllers.cs -> TasksController.cs. Full chain now:
controller -> service -> repository -> EF.

## Type of change
- [x] Refactor
- [x] Tests

## How to test it
1. `dotnet test` -> controller tests pass (correct status code per Result)
2. Swagger: every Tasks/Auth endpoint behaves exactly as before

## Checklist
- [x] Tests written first (red), then code (green)
- [x] Controllers contain no DbContext or business logic
```

PR into `develop`, merge, delete.

---

## Slice F — Agents onto Repositories (and make Claude testable)

**Goal:** agents stop using `AppDbContext` directly and use the repositories. To make
the tool handlers unit-testable, we put a thin interface in front of the Claude client
so a test can feed the agent a canned tool call. This is the Dependency Inversion
principle applied to the one remaining hard-to-test dependency.

**This slice also absorbs the old "Chunk 1" work** (the `ClaudeAgentBase` extraction that
was reset away from the baseline). Both agents currently duplicate the whole Claude
tool-use loop, client setup, `RecordActionAsync`, and result helpers. We extract that
into a base class here — but this time test-first and built on the repository + client
seams, so it lands correct and covered rather than as a blind copy.

Branch: `feature/slice-f-agents`

### F0. What the extraction produces (the DRY win, done properly)

- `ClaudeAgentBase` (abstract) owns: the tool-use loop, `TryCreateClaudeClient` (now via
  `IClaudeClient`), `RecordActionAsync` (now via `IAgentLogRepository`), `ToolResult`,
  `WasSuccessful`, `DefineTool`, and the cycle-start/complete broadcasts.
- `AgentConstants` (`AgentPhases`, `AgentActions`) and `AnthropicDefaults` (default model
  + token limit) — the magic-string and default-value homes.
- Each concrete agent keeps only its policy: which tools, what prompt, per-tool handlers.

These were the right ideas the first time; the difference now is they sit on top of the
tested repository/service layers and get their own tests.

### F1. Abstract the Claude client

Create `TaskFlow.Api/Services/IClaudeClient.cs` — a minimal seam over the SDK call the
base agent makes:

```csharp
using Anthropic.SDK.Messaging;

namespace TaskFlow.Api.Services;

public interface IClaudeClient
{
    Task<MessageResponse> SendAsync(MessageParameters parameters, CancellationToken ct = default);
}
```

Implement it as a wrapper over `AnthropicClient` (`ClaudeClient.cs`), registered in DI.
`ClaudeAgentBase` takes `IClaudeClient` instead of newing up `AnthropicClient`, and its
data access goes through `ITaskRepository` / `IAgentLogRepository` injected into the
concrete agents. (Exact signatures confirmed at execution — this depends on the final
Slice C repository surface, which is why we do it after C is merged.)

### F2. RED — a tool-handler test with real SQLite and a stub Claude

The high-value test: when Claude asks the stale agent to escalate a task, the task's
priority actually becomes High and an `AgentLog` is written. Using `SqliteInMemoryContext`
for data and a Moq `IClaudeClient` returning a single canned `tool_use` for
`escalate_task`, assert the database result.

```bash
dotnet test
```

**Expect RED** until the agents are refactored onto the seams.

### F3. GREEN — refactor the agents

Point `ClaudeAgentBase` and both agents at `IClaudeClient` and the repositories. The
tool-use loop is unchanged in shape; only the client call and the data calls move
behind interfaces. Tests go green; run the app to confirm the agents still fire.

### F4. Commit + PR

```bash
git commit -m "refactor(agents): extract ClaudeAgentBase on IClaudeClient + repositories; add handler tests"
git push -u origin feature/slice-f-agents
```

**PR description — paste into the PR body:**

```markdown
## What does this PR do?
Extracts ClaudeAgentBase (tool-use loop, client setup, record/notify, helpers) plus
AgentConstants and AnthropicDefaults, so both agents stop duplicating the mechanics.
Agents now depend on IClaudeClient (seam over AnthropicClient) and the repositories
instead of AppDbContext/AnthropicClient directly. Absorbs the old Chunk 1 work, done
test-first this time.

## Type of change
- [x] Refactor
- [x] Tests

## How to test it
1. `dotnet test` -> agent tool-handler tests pass (real SQLite, stubbed IClaudeClient:
   an escalate tool_use actually sets priority High and writes an AgentLog)
2. Run the app: both agents still fire and broadcast to the dashboard

## Checklist
- [x] Tests written first (red), then code (green)
- [x] No agent references AppDbContext or AnthropicClient directly
```

PR into `develop`, merge, delete.

---

## Slice G — Final Backend Cleanup

Branch: `feature/slice-g-cleanup`

- Fold in remaining constant use (SignalR event names `"AgentAction"`/`"AgentCycle"`
  into a shared `HubEvents` constants class used by hub and notifier).
- Unify the diagnostics controller's model default onto `AnthropicDefaults.Model`.
- Add XML-doc summaries to any public type/member still missing one.
- ~~Bump the vulnerable SQLite native package to clear the `NU1903` warning.~~
  **Done early** (during Slice A). Fix was, from `TaskFlow.Api`:
  `dotnet add package SQLitePCLRaw.lib.e_sqlite3` (adds a direct reference to the latest
  patched native lib, overriding the vulnerable 2.1.11 pulled in transitively by EF Core
  Sqlite). Fallback if it does not clear: `dotnet add package SQLitePCLRaw.bundle_e_sqlite3`.
- Full run: `dotnet build` then `dotnet test` — everything green.

```bash
git add .
git commit -m "chore: constant cleanup, xml docs, security patch; backend refactor complete"
git push -u origin feature/slice-g-cleanup
```

**PR description — paste into the PR body:**

```markdown
## What does this PR do?
Final backend cleanup: shared HubEvents constants for SignalR event names, diagnostics
model default unified onto AnthropicDefaults, XML-doc summaries filled in, and the
vulnerable SQLite package bumped (NU1903 cleared). Closes out the backend refactor.

## Type of change
- [x] Chore / docs
- [x] Security

## How to test it
1. `dotnet build` -> zero warnings (NU1903 gone)
2. `dotnet test` -> all tests green

## Checklist
- [x] No magic strings left for actions/phases/events
- [x] Full solution builds and all tests pass
```

PR into `develop`, merge, delete. Backend refactor complete.

---

# Part 4 — The Frontend Revamp (TaskFlow.Web)

We finish the backend (Slices A–G) first, then give the React app the same treatment.
The principles carry over; the vocabulary changes.

## What DRY / SOLID / TDD mean in React

**DRY.** Shared logic that is currently copy-pasted or scattered — the status and
priority color maps, date formatting, the agent action-style map — moves into small
helper modules imported everywhere they are needed.

**SOLID, adapted.** React does not have classes and interfaces the same way, so the
equivalent is *separation of concerns*:

- **Presentational components** — dumb. They take props and render markup. No fetching,
  no global state. `TaskCard`, `KanbanColumn`, `AgentFeed` become pure. Easy to test:
  give props, assert on what renders.
- **Container components / hooks** — own the data fetching, state, and side effects.
  `Dashboard` and custom hooks like `useAgentFeed` hold the messy parts.
- **Transport layer** — `api.ts` is the only code that knows about URLs and fetch.

Same dependency direction as the backend: UI depends on hooks, hooks depend on the api
layer, the api layer depends on the network. Each can be tested by faking the layer
below.

**TDD.** Identical red-green-refactor loop. Different tools:

- **Vitest** — the test runner, Vite-native (so it shares your existing config).
- **React Testing Library (RTL)** — renders a component into a virtual DOM and lets you
  assert on what a *user* would see ("the button labeled Sign in is disabled"), not on
  internal implementation.
- **MSW (Mock Service Worker)** — intercepts `fetch` at the network layer and returns
  fake responses. So a test can render the real `Login` component, let it make a real
  `fetch`, and MSW answers with a canned token — no backend running.

## Target folder shape

```
TaskFlow.Web/src/
├── api/            transport only (client, endpoints, token storage)
├── hooks/          useAuth, useAgentFeed, useTasks — state + effects
├── components/     presentational: TaskCard, KanbanColumn, AgentFeed, ...
├── features/       container components: Dashboard, Login (compose the above)
├── lib/            shared helpers: formatting, color maps, constants
├── types.ts        shared TS types (mirror the C# DTOs)
└── test/           setup, MSW handlers, test utilities
```

## Frontend slice plan

| Slice | What | Teaches |
|-------|------|---------|
| **H** | Scaffold Vitest + RTL + MSW, one smoke test | The frontend harness runs |
| **I** | Restructure `src/` into layered folders, extract shared helpers | DRY + separation of concerns |
| **J** | Split presentational vs container, test pure components | Component testing with RTL |
| **K** | Test the api layer and hooks against MSW | Faking the network, testing effects |
| **L** | Login-flow and board-render integration tests, final cleanup | End-to-end confidence |

The same loop applies: I write a failing Vitest test, you run `npm run test` and confirm
red, I write the component/hook, you confirm green. Same per-slice PR cadence as the
backend.

---

## Slice H — Scaffold the Frontend Test Harness

Branch: `feature/slice-h-fe-test-harness`

### H1. Install the test tooling

```bash
cd C:\Users\Sirgimp\Desktop\TaskFlow\TaskFlow.Web
npm install -D vitest @testing-library/react @testing-library/jest-dom @testing-library/user-event jsdom msw
```

- `vitest` — runner. `jsdom` — a fake browser DOM so components can render in Node.
- `@testing-library/react` + `jest-dom` — render components, assert on visible output.
- `user-event` — simulate real typing/clicking.
- `msw` — intercept fetch in tests.

### H2. Configure Vitest

Add a `test` block to `vite.config.ts`:

```ts
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: { port: 5173 },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: './src/test/setup.ts',
    css: true,
  },
})
```

Create `src/test/setup.ts`:

```ts
import '@testing-library/jest-dom'
```

Add a script to `package.json`:

```json
"scripts": {
  "test": "vitest"
}
```

### H3. Smoke test

Create `src/test/smoke.test.tsx`:

```tsx
import { render, screen } from '@testing-library/react'
import { describe, it, expect } from 'vitest'

describe('harness', () => {
  it('renders and asserts', () => {
    render(<h1>TaskFlow</h1>)
    expect(screen.getByText('TaskFlow')).toBeInTheDocument()
  })
})
```

```bash
npm run test
```

**Expect GREEN** (one passing test). Press `q` to quit the watcher.

```bash
git add .
git commit -m "test(web): scaffold Vitest + React Testing Library + MSW"
git push -u origin feature/slice-h-fe-test-harness
```

**PR description — paste into the PR body:**

```markdown
## What does this PR do?
Adds the frontend test harness: Vitest + React Testing Library + MSW + jsdom, wired into
vite.config with a setup file. One smoke test proves the runner works.

## Type of change
- [x] Tests / tooling (frontend)

## How to test it
1. `npm run test` -> 1 passing test
2. `npm run dev` still starts normally

## Checklist
- [x] Test config isolated in vite.config test block + src/test/setup.ts
```

Commit + PR into `develop`, merge, delete.

---

## Slice I — Restructure into Layers + Shared Helpers

Branch: `feature/slice-i-fe-restructure`

**Goal:** move the flat `src/` into the layered folder shape and extract duplicated
logic into `lib/`. This is mostly moving files and fixing imports, so it is low-risk,
but the smoke tests from H (and any component tests) guard it.

### I1. Create the folders and move files

```
src/
├── api/         <- api.ts (split into client.ts, auth.ts, tasks.ts, agentLogs.ts)
├── hooks/       <- useAgentFeed.ts, useAuth (from AuthContext), useTasks (new)
├── components/  <- TaskCard, KanbanColumn, AgentFeed, AgentStatus (presentational)
├── features/    <- Dashboard, Login (containers)
├── lib/         <- formatting.ts, styles.ts, constants.ts
├── types.ts
└── test/
```

### I2. Extract shared helpers (the DRY pass)

Create `src/lib/styles.ts` — the color maps currently inline in components:

```ts
export const priorityStyles: Record<string, string> = {
  High: 'bg-red-500/15 text-red-300 border-red-500/30',
  Medium: 'bg-amber-500/15 text-amber-300 border-amber-500/30',
  Low: 'bg-slate-500/15 text-slate-300 border-slate-500/30',
}

export const actionStyles: Record<string, string> = {
  Escalated: 'bg-red-500/15 text-red-300 border-red-500/30',
  Reassigned: 'bg-blue-500/15 text-blue-300 border-blue-500/30',
  FlaggedForReview: 'bg-amber-500/15 text-amber-300 border-amber-500/30',
  PriorityUpdated: 'bg-emerald-500/15 text-emerald-300 border-emerald-500/30',
  PrioritiesUpdated: 'bg-emerald-500/15 text-emerald-300 border-emerald-500/30',
  NoChangesNeeded: 'bg-slate-500/15 text-slate-400 border-slate-500/30',
  NoActionNeeded: 'bg-slate-500/15 text-slate-400 border-slate-500/30',
  CycleActions: 'bg-violet-500/15 text-violet-300 border-violet-500/30',
}

export const neutralStyle = 'bg-slate-500/15 text-slate-400 border-slate-500/30'
```

Create `src/lib/formatting.ts`:

```ts
export const formatDate = (iso: string) => new Date(iso).toLocaleDateString()
export const formatTime = (iso: string) => new Date(iso).toLocaleTimeString()
```

Components import from these instead of defining their own copies. Update every import
path touched by the move. `npm run test` and `npm run dev` both still work.

```bash
git add .
git commit -m "refactor(web): restructure src into api/hooks/components/features/lib"
git push -u origin feature/slice-i-fe-restructure
```

**PR description — paste into the PR body:**

```markdown
## What does this PR do?
Restructures the flat src/ into layered folders (api, hooks, components, features, lib)
and extracts duplicated color maps and date formatters into src/lib. No behavior change,
just organization + DRY.

## Type of change
- [x] Refactor (frontend)

## How to test it
1. `npm run test` -> existing tests still pass
2. `npm run dev` -> app looks and behaves identically

## Checklist
- [x] No component defines its own color map or date formatter anymore
- [x] All import paths updated
```

Commit + PR into `develop`, merge, delete.

---

## Slice J — Presentational Components + Their Tests

Branch: `feature/slice-j-fe-components`

**Goal:** make the leaf components pure (props in, markup out) and cover each with an
RTL test. Pure components are trivial to test because they have no side effects.

### J1. RED — a test for TaskCard

`src/components/TaskCard.test.tsx`:

```tsx
import { render, screen } from '@testing-library/react'
import { describe, it, expect } from 'vitest'
import { DndContext } from '@dnd-kit/core'
import { TaskCard } from './TaskCard'
import type { TaskItem } from '../types'

const task: TaskItem = {
  id: 1, title: 'Ship it', description: 'now', status: 'Todo',
  priority: 'High', dueDate: null, createdAt: '', updatedAt: '',
  assignedToId: null, assignedToName: null,
}

describe('TaskCard', () => {
  it('shows the title and priority badge', () => {
    render(<DndContext><TaskCard task={task} /></DndContext>)
    expect(screen.getByText('Ship it')).toBeInTheDocument()
    expect(screen.getByText('High')).toBeInTheDocument()
  })

  it('shows Unassigned when there is no assignee', () => {
    render(<DndContext><TaskCard task={task} /></DndContext>)
    expect(screen.getByText('Unassigned')).toBeInTheDocument()
  })
})
```

(The `DndContext` wrapper is needed because `TaskCard` uses `useSortable`.)

```bash
npm run test
```

If `TaskCard` already renders these, this may pass immediately — that is fine, it locks
the behavior in before we refactor. Add equivalent tests for `KanbanColumn`, `AgentFeed`
(asserts an action label renders), and `AgentStatus` (Running vs Idle badge).

### J2. Refactor to pure components

Ensure none of these fetch data or read global state — they only take props. Any that
currently reach into a hook get that data lifted up to their container parent. Tests
stay green.

```bash
git add .
git commit -m "refactor(web): make leaf components presentational; add RTL tests"
git push -u origin feature/slice-j-fe-components
```

**PR description — paste into the PR body:**

```markdown
## What does this PR do?
Makes TaskCard, KanbanColumn, AgentFeed, and AgentStatus pure presentational components
(props in, markup out) and covers each with a React Testing Library test asserting on
user-visible output.

## Type of change
- [x] Refactor (frontend)
- [x] Tests

## How to test it
1. `npm run test` -> component tests pass
2. `npm run dev` -> board and feed render identically

## Checklist
- [x] No leaf component fetches data or reads global state
- [x] Each covered by an RTL test
```

Commit + PR into `develop`, merge, delete.

---

## Slice K — API Layer + Hooks Against MSW

Branch: `feature/slice-k-fe-api-hooks`

**Goal:** test the transport layer and the hooks by faking the backend at the network
level with MSW. This is where the app's trickiest logic (auth header, 401 handling,
SignalR fallbacks) gets covered.

### K1. MSW handlers

Create `src/test/handlers.ts` and `src/test/server.ts`:

```ts
// handlers.ts
import { http, HttpResponse } from 'msw'

export const handlers = [
  http.post('*/api/Auth/login', async () =>
    HttpResponse.json({ token: 'fake.jwt.token', name: 'Ada', email: 'ada@x.dev', expiresAt: '' })),
  http.get('*/api/Tasks', () => HttpResponse.json([])),
]
```

```ts
// server.ts
import { setupServer } from 'msw/node'
import { handlers } from './handlers'
export const server = setupServer(...handlers)
```

Wire it into `src/test/setup.ts`:

```ts
import '@testing-library/jest-dom'
import { server } from './server'
import { beforeAll, afterEach, afterAll } from 'vitest'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())
```

### K2. RED/GREEN — test the api layer

`src/api/auth.test.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { login } from './auth'

describe('login', () => {
  it('returns the token from the API', async () => {
    const res = await login('ada@x.dev', 'pw')
    expect(res.token).toBe('fake.jwt.token')
  })
})
```

Add a test that a 401 from a protected call clears the stored token (override the
handler in that one test to return 401). Then test `useAgentFeed` seeds from
`getAgentLogs` and exposes `connected`.

```bash
git add .
git commit -m "test(web): cover api layer and hooks against MSW"
git push -u origin feature/slice-k-fe-api-hooks
```

**PR description — paste into the PR body:**

```markdown
## What does this PR do?
Adds MSW handlers and tests the transport layer and hooks against a faked backend:
login returns the token, a 401 clears the stored token, and useAgentFeed seeds from
getAgentLogs and exposes connected.

## Type of change
- [x] Tests (frontend)

## How to test it
1. `npm run test` -> api and hook tests pass
2. Handlers live in src/test/handlers.ts; server resets between tests

## Checklist
- [x] Tests hit the fetch layer (MSW), not a mocked api module
- [x] 401 handling covered
```

Commit + PR into `develop`, merge, delete.

---

## Slice L — Integration Tests + Final Cleanup

Branch: `feature/slice-l-fe-integration`

**Goal:** a couple of tests that render a real feature end-to-end through MSW, proving
the pieces work together, plus a final DRY sweep.

### L1. Login flow integration test

`src/features/Login.test.tsx`:

```tsx
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect } from 'vitest'
import { AuthProvider } from '../hooks/AuthProvider'
import { Login } from './Login'

describe('Login flow', () => {
  it('signs in and stores the session', async () => {
    render(<AuthProvider><Login /></AuthProvider>)

    await userEvent.type(screen.getByPlaceholderText('Email'), 'ada@x.dev')
    await userEvent.type(screen.getByPlaceholderText('Password'), 'password1')
    await userEvent.click(screen.getByRole('button', { name: /sign in/i }))

    // MSW returns a token; the app should move past the login form.
    expect(await screen.findByText('Ada')).toBeInTheDocument()
  })
})
```

### L2. Final sweep

- Confirm no component still defines its own color map or date formatter.
- `npm run test` all green; `npm run build` clean; `npm run dev` smoke check.

```bash
git add .
git commit -m "test(web): add login-flow integration test; final DRY sweep"
git push -u origin feature/slice-l-fe-integration
```

**PR description — paste into the PR body:**

```markdown
## What does this PR do?
Adds an end-to-end login-flow integration test (real components through MSW) and a final
DRY sweep. Completes the frontend revamp.

## Type of change
- [x] Tests (frontend)
- [x] Chore (final cleanup)

## How to test it
1. `npm run test` -> all green including the login-flow integration test
2. `npm run build` clean; `npm run dev` smoke check

## Checklist
- [x] Login flow covered end-to-end
- [x] No leftover duplicated helpers
```

PR into `develop`, merge, delete. Frontend revamp complete.

---

# Part 5 — North Star: Document-Driven Autonomous Execution

This is the direction the whole project is aimed at, recorded here so the refactor
stays pointed at it. It is NOT part of Slices A–L; it is what those slices make
possible. We build it only after the architecture and tests are in place.

## The vision

Hand TaskFlow a specification document (exactly like this one). TaskFlow:

1. **Parses** the document into discrete, well-formed work items.
2. **Creates** them as tasks on the Kanban board (To Do column).
3. **Agents pick them up** autonomously, move them across the board
   (To Do → In Progress → Review → Done), and actually do the work.
4. **You watch it happen live** on the dashboard via the SignalR feed already built.

In other words: the reactive agents (prioritize, detect staleness) grow into
*executing* agents (ingest, plan, act).

## Why the current refactor is the prerequisite, not a detour

Each piece of this vision leans directly on something Slices A–L put in place:

- **Document → tasks** needs a testable ingestion service. That is a new service in
  the Slice-D style: `IDocumentIngestionService`, mocked-repository unit tests for the
  parsing/splitting rules, no HTTP or Claude in the test.
- **Agents that execute** need the `IClaudeClient` seam from Slice F, so an executor
  agent's decision loop can be tested with canned Claude responses instead of live
  calls — essential when the agent is doing real work, not just re-prioritizing.
- **Writing results back to the board** goes through the repositories from Slice C, so
  every state transition is covered and observable.
- **Watching live** reuses the SignalR feed from Sprint 8 unchanged.

Trying to build this on the pre-refactor codebase (controllers talking straight to EF,
agents newing up the Claude client) would be untestable and unsafe. That is the whole
argument for doing the architecture work first.

## Rough shape of the future slices (post-L, not yet detailed)

These will be specified in full — small, test-first — when we reach them. Listed here
only so the destination is on the map:

| Future slice | What | Leans on |
|--------------|------|----------|
| **M** | `IDocumentIngestionService`: parse a spec doc into task drafts | Slice D pattern |
| **N** | Ingestion endpoint + UI to upload a document and preview drafts | Slice E, frontend |
| **O** | Task drafts → board tasks with provenance (which doc, which section) | Slice C repos |
| **P** | Executor agent: claims a To Do task, plans sub-steps, works it | Slice F `IClaudeClient` |
| **Q** | Board transitions driven by the executor, streamed live | Slice C repos + SignalR |
| **R** | Guardrails: human approval gates, cost caps, rollback on failure | all of the above |

**Design note carried forward:** an executor agent doing real work needs firm limits —
a per-task iteration cap (we already have one on the tool loop), a spend ceiling, and a
human approval gate before anything destructive. Slice R exists so those are designed
in, not bolted on. This is the same "give the agent an escape hatch and a leash"
principle from the Sprint 7 stale-task agent, scaled up.

## Open questions to resolve before starting Slice M

Recorded now so they are not forgotten; answered when we get there, not before:

- How granular should document parsing be — one task per heading, per checklist item,
  or Claude's judgment?
- Do executor agents write code/files, or only orchestrate and report? (Scope + safety.)
- What is the human-in-the-loop checkpoint — approve each task, approve the batch, or
  fully autonomous with a kill switch?

---

# Definition of Done

- Backend: controllers → services → repositories → EF, every layer behind an interface.
- Every business rule and repository query covered by a test; agents' tool handlers
  covered with real SQLite and a stubbed Claude client.
- Frontend: `api/ hooks/ components/ features/ lib/` layers; presentational components,
  hooks, api layer, and one integration flow all tested.
- `dotnet test` and `npm run test` both green; both apps build and run.
- Each slice shipped as its own small PR into `develop`.
