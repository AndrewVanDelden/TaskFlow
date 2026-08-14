# TaskFlow — Epic 6: Per-User LLM Credentials

**Epic map, stated once here to prevent mix-ups across these three cross-referencing docs:**
this is **Epic 6**. `TaskFlow_Epic4_PostgresMigration.md` is **Epic 4** — its Data Protection
keyring-persistence groundwork is a hard prerequisite for this epic, see below.
`TaskFlow_Epic5_DeploymentInfrastructure.md` is **Epic 5** — unrelated to this epic's actual work,
not referenced further here.

Same standing framework as Epics 2, 3, 4, and 5: strict TDD (RED before GREEN, confirmed by an
actual test run before any implementation), clean code, SOLID, DRY. Not restated in full here — see
`CLAUDE.md` and `TaskFlow_Epic3_ResumeBuilder.md`'s "Standing rules" section, which this epic
inherits unmodified. Follows the same sprint structure as Epic 3: decisions recorded before
dispatching any engineer, tasks carry explicit RED/GREEN descriptions, nothing is marked done
without being actually checked.

**Why this is its own epic, not a Resume Builder sprint:** this is account/platform-scoped work —
it changes how *any* Claude-calling feature resolves its credentials, not something specific to
resume tailoring. It would matter the same way for a future, unrelated epic. Same reasoning that
already put deployment infrastructure in its own doc (Epic 5) rather than folding it into Epic 3.

**Why this comes after the Postgres migration, not before or in parallel:** the Data Protection
key-persistence design below depends on Postgres being the running database (see Epic 4,
`TaskFlow_Epic4_PostgresMigration.md`). Building it against SQLite first and redoing it after the
migration would be wasted work, not genuine parallelism.

---

## The problem, stated plainly

Today there is exactly one Anthropic API key, configured once at the deployment level
(`Anthropic:ApiKey`, read from `IConfiguration`), shared by every Claude call regardless of which
logged-in user's work is being processed. If one deployment ever serves more than one person — the
app already supports multiple authenticated users via JWT auth, and Epic 3's ownership model
already scopes each person's data — every one of those people's tailoring runs currently bills to
whoever configured that one key. The goal: each logged-in user configures, verifies, and uses their
**own** Anthropic API key and preferred Claude model, from a Setup/Preferences page, tied to their
own identity.

**Scope, locked before any design work below:**
- **Claude model selection only** (Sonnet/Opus/Haiku/Fable), not a multi-provider abstraction.
  Nothing in this codebase points at needing a non-Anthropic provider; building an `ILlmClient`
  layer over multiple SDKs is a real, separate, much larger project not being taken on here.
- **Per-user data isolation within one shared database**, not separate physical databases per
  user. Each deployment (exe/Docker/Kubernetes instance, per Epic 5) already gets its own database
  by construction — that's a deployment-target property, unrelated to this epic. What's missing and
  what this epic actually builds is per-user *credentials* layered on the ownership model that
  already exists.

---

## Confirmed against the repo (2026-08-11), before any design below

Read in full rather than assumed, since the whole design here hinges on how the Claude client is
currently constructed:

| Claim | Status |
|---|---|
| `IClaudeClient` (`TaskFlow.Api/Services/IClaudeClient.cs`) — `bool IsConfigured`, `Task<MessageResponse> SendAsync(MessageParameters, CancellationToken)` | **Confirmed**, exact shape. |
| `ClaudeClient` (`TaskFlow.Api/Services/ClaudeClient.cs`) — constructor takes `IConfiguration`, reads `config["Anthropic:ApiKey"]` internally, constructs `new AnthropicClient(apiKey)` if non-blank, `null` client (and `IsConfigured == false`) otherwise. The only `new AnthropicClient(` call site in the repo. | **Confirmed**, exact code read. |
| Model is *not* passed to `ClaudeClient`'s constructor — it's read separately, per call, in `ClaudeAgentBase`'s tool-conversation helper from `Config["Anthropic:Model"]`. | **Confirmed.** This matters: per-user model selection needs its own design, not just a key swap (see "Decisions owned here" below). |
| DI registration: `builder.Services.AddScoped<IClaudeClient, ClaudeClient>();` (`Program.cs`, line 111) | **Confirmed**, Scoped lifetime. |
| `AgentRunner` creates a **fresh DI scope per agent, per polling cycle** (`using var scope = _scopeFactory.CreateScope();` inside each agent's own timer loop) | **Confirmed.** This means "construct a client freshly per cycle" is already this codebase's existing pattern — a per-user factory is a natural extension of a lifetime model already in place, not a new one being imposed. |
| `ClaudeAgentBase` exposes `protected IClaudeClient Claude { get; }`, set via constructor injection; `TaskPrioritizerAgent` and `GenericExecutorAgent` extend it directly, each taking `IClaudeClient` in their own constructor and forwarding it to `base(...)`; `TailoringAgentBase` (→ `ResumeTailoringAgent`, `CoverLetterAgent`) does the same one level deeper. | **Confirmed**, exact inheritance chain. |
| `ClaudeJobPostingParser`, `ClaudeIngestionParser`, and their shared base `ClaudeJsonExtractionParserBase` each take `IClaudeClient` via their own constructor injection, independent of the agents. | **Confirmed.** |
| No ASP.NET Core Data Protection configuration exists anywhere in the repo — no `AddDataProtection()`, no key-persistence call, nothing. | **Confirmed** by a repo-wide search. Running on the framework default: local-disk keyring, per-machine/per-container. |
| `TaskItem.ClaimedBy` identifies the **claiming agent** (a string like an agent `Name`), not a human user. `TaskItem.AssignedToId` is an optional assignee, unrelated to ownership/visibility. The **only** ownership mechanism is `TaskItem.ApplicationId` → `JobApplication.OwnerId`, and it structurally never applies to `TaskKind.Generic` tasks — `TaskService.CreateAsync` never sets `ApplicationId`, so `TaskRepository.GetAllAsync`'s own filter (`t.ApplicationId == null || t.Application!.OwnerId == callerId`) always passes every caller through for `Generic` work. | **Confirmed**, exact code and comments read — this is the structural basis for the ownership boundary decided below, not an assumption. |

---

## Decisions owned here, before dispatching any engineer (2026-08-11)

- **BYOK applies only to owner-scoped Claude calls — `TailoringAgentBase`'s two agents and the two
  job-posting parsers — not to `TaskPrioritizerAgent`/`GenericExecutorAgent`.** Confirmed
  structurally correct above, not just reasoned about in the abstract: `TaskKind.Generic` tasks have
  no owner to resolve a personal key from, by construction. Those two agents keep using the
  existing default/deployment-level `IClaudeClient` path, entirely unchanged — zero risk to
  already-shipped Epic 2 behavior, and a task below (T-E6.6) proves this boundary holds with a
  regression test rather than leaving it as an assumption from "we didn't touch those files."
- **Encryption via ASP.NET Core's Data Protection API (`IDataProtector`), not hashing.** The app
  needs the real key back to call Anthropic with it — this is a secret to retrieve, not a password
  to verify, so a one-way hash is the wrong tool even though it's the more familiar pattern for
  "don't store this in plaintext." Confirmed no Data Protection setup exists yet — this is new
  configuration, not a change to something already there.
- **The Data Protection keyring is persisted to Postgres (`PersistKeysToDbContext`), not left on
  the default local disk.** The default keyring is per-machine/per-container — a fresh container on
  restart (Docker) or a second pod (Kubernetes) would get a different keyring, permanently unable
  to decrypt anything encrypted before it. **Not the only fix available** — `PersistKeysToFileSystem`
  pointed at the same persisted volume as the database file would solve the identical restart-
  survival problem with no client-server database involved at all, and was the honest answer when
  Epic 4's own necessity was questioned directly (see that epic's "Why this exists," revisited
  2026-08-11: Postgres isn't technically required by anything either epic actually needs). Since
  Epic 4 is being built anyway, for its own learning-value reasons, reusing it here avoids a second
  persistence mechanism to configure and reason about — a DRY call made with the alternative named,
  not a case of Postgres being the only option. This is exactly why this epic is sequenced after
  Epic 4, not before it — that sequencing still holds even though the dependency is now "reuse what
  Epic 4 built" rather than "Epic 4 is structurally required."
- **Verify is a separate action from Save, and persists nothing.** `POST
  .../verify` takes a candidate key/model, makes one real, minimal call to Anthropic, and returns
  pass/fail without touching the repository — so a user can test a key repeatedly with no side
  effects before ever saving it, and a bad key is caught immediately rather than on the first real
  tailoring run.
- **Client resolution: a new `IClaudeClientFactory`, added alongside the existing `IClaudeClient`/
  `ClaudeClient` — not a replacement for them.** `ClaudeClient`'s existing `IConfiguration`-driven
  constructor stays exactly as it is today, still used by the default/fallback path
  (`TaskPrioritizerAgent`/`GenericExecutorAgent` — zero change to either class). The factory adds a
  second construction path: resolve a specific user's stored, decrypted key and model, and build a
  `ClaudeClient` scoped to it. This is additive, not a rewrite of the existing client — matching
  SOLID's open/closed instinct (extend, don't modify working behavior) more than it's a deliberate
  citation of the principle.
- **Model selection is resolved by the same factory as the API key, not a separate mechanism that
  could drift out of sync with it.** `IClaudeClient` gains a `Model` property — the default/
  fallback path still reads `Config["Anthropic:Model"]` exactly as today (no behavior change for
  `TaskPrioritizerAgent`/`GenericExecutorAgent`); the per-user path carries the resolved
  `UserLlmSettings.Model`. `ClaudeAgentBase`'s tool-conversation helper reads `Claude.Model` instead
  of reading `IConfiguration` directly — a small cohesion improvement (the client fully owns "which
  credentials, which model" instead of splitting that across two places) that falls out of solving
  the per-user problem properly, not a separate refactor bolted on.
- **The exact mechanism for `TailoringAgentBase` to swap to a per-owner-resolved client mid-cycle
  is deliberately left to T-E6.6's RED test, not prescribed here.** Two reasonable shapes exist (a
  settable `Claude` property reassigned once the claimed task's owner is known, or an explicit
  client parameter threaded through the tool-conversation helper) and the choice doesn't change
  this epic's design intent either way — matching this project's own precedent of leaving
  implementation-level mechanics to be settled when the test is written rather than over-specified
  in the architecture doc (Sprint 5 left Typst's exact escape-character set the same way).
- **No `{id}` in the settings route.** Every action resolves "my own settings" directly from the
  caller's JWT identity — there's no separate identifier for a caller to spoof, so there's no IDOR
  surface here structurally, simpler than `JobApplicationService`'s fetch-then-check pattern because
  there's nothing to check against beyond authentication itself.
- **`JobApplicationAssemblyService.AssembleAsync` refuses assembly when the caller has no
  configured `UserLlmSettings`.** Checked early, before any persistence — matches this project's
  established pattern of refusing clearly and early (`Result.Invalid`) rather than letting a task
  fail deep inside a claimed agent cycle for a problem that was knowable at ingestion time.
- **No migration/rollout handling for existing users.** Confirmed no real data exists in any
  environment yet — this ships clean, not with a transition period.

---

## Goal

A logged-in user configures, verifies, and saves their own Anthropic API key and preferred Claude
model from a Setup/Preferences page. Owner-scoped tailoring work (Epic 3's agents, job-posting
ingestion) uses that user's own credentials; the shared/generic board is unaffected.

## Files involved

- `TaskFlow.Api/Models/UserLlmSettings.cs` (new)
- `TaskFlow.Api/Repositories/IUserLlmSettingsRepository.cs`, `UserLlmSettingsRepository.cs` (new)
- `TaskFlow.Api/Services/IUserLlmSettingsService.cs`, `UserLlmSettingsService.cs` (new — owns
  encrypt/decrypt via `IDataProtector`)
- `TaskFlow.Api/Services/IClaudeClientFactory.cs`, `ClaudeClientFactory.cs` (new)
- `TaskFlow.Api/Services/IClaudeClient.cs` (edit — add `Model` property)
- `TaskFlow.Api/Services/ClaudeClient.cs` (edit — second, key/model-parameterized construction
  path alongside the existing `IConfiguration`-driven one)
- `TaskFlow.Api/Agents/ClaudeAgentBase.cs` (edit — tool-conversation helper reads `Claude.Model`)
- `TaskFlow.Api/Agents/TailoringAgentBase.cs` (edit — resolves a per-owner client via the factory;
  exact mechanism decided at T-E6.6)
- `TaskFlow.Api/Ingestion/ClaudeJobPostingParser.cs`, `ClaudeIngestionParser.cs`,
  `ClaudeJsonExtractionParserBase.cs` (edit — resolve a per-caller client via the factory)
- `TaskFlow.Api/Services/JobApplicationAssemblyService.cs` (edit — block without configured
  settings)
- `TaskFlow.Api/Controllers/UserLlmSettingsController.cs` (new)
- `TaskFlow.Api/Program.cs` (edit — `AddDataProtection().PersistKeysToDbContext<AppDbContext>()`,
  new DI registrations)
- `TaskFlow.Web/src/features/SettingsPage.tsx` (new, exact location to confirm against the current
  frontend routing structure)
- Tests: `UserLlmSettingsRepositoryTests.cs`, `UserLlmSettingsServiceTests.cs`,
  `ClaudeClientFactoryTests.cs`, `UserLlmSettingsControllerTests.cs`, `TailoringAgentBaseTests.cs`
  additions, `SettingsPage.test.tsx`

## Tasks

**T-E6.1 — `UserLlmSettings` entity, migration, repository.** RED: persist settings for a user,
retrieve by user id, content round-trips, scoped to owner — mirrors Sprint 0's `T0.1`
(`ResumeContext`) exactly, same shape, same precedent. GREEN: entity, EF configuration, migration,
repository.

**T-E6.2 — Encryption at rest, with a shared keyring.** RED: save a key through the service, read
the raw DB column directly (bypassing the service), assert it is **not** the plaintext value; read
it back through the service, assert it matches the original. A second RED test simulates a
container restart (a fresh DI container/app host between save and read) and asserts decryption
still succeeds — this is the test that actually proves the keyring-persistence decision matters,
not just that encryption exists at all. GREEN: `IDataProtector`-based encrypt/decrypt in
`UserLlmSettingsService`, `AddDataProtection().PersistKeysToDbContext<AppDbContext>()` in
`Program.cs`.

**T-E6.3 — Verify endpoint.** RED: given a valid key/model, verify returns success and persists
nothing (query the DB afterward, assert no row created or changed — matching this project's
established habit of proving nothing persisted on a non-save path, from Sprint 2's `AssembleAsync`
tests). Given an invalid key, verify returns a clear failure. GREEN: `POST
/api/UserLlmSettings/verify`, constructs a throwaway client, makes one minimal real call, never
touches the repository.

**T-E6.4 — Save endpoint.** RED: save persists encrypted settings scoped entirely to the caller's
own JWT identity, no route parameter involved at all. GREEN: `POST /api/UserLlmSettings`, resolves
the caller via the existing shared `TryGetCurrentUserId` helper (`ControllerBaseExtensions`,
confirmed already shared between `TasksController` and `JobApplicationsController` — reused here
too, not duplicated a third time).

**T-E6.5 — `IClaudeClientFactory`.** RED: given two different users with different stored
keys/models, `CreateForUserAsync` produces clients that each carry their own decrypted key and
model — asserted via `ClaudeClient`'s new `Model` property and a capturing test double at the SDK
boundary, mirroring how existing tests assert on `StubClaude.LastRequest`. GREEN: factory resolves
`UserLlmSettings` by owner, decrypts via `IDataProtector`, constructs a `ClaudeClient` through its
new key/model-parameterized path.

**T-E6.6 — Wire the factory into owner-scoped call sites only.** RED: `TailoringAgentBase`-driven
agents use the resolved owner's client for their tool conversation, not the default; the two
job-posting parsers resolve from the authenticated caller. A second RED test proves the ownership
boundary decided above actually holds: `TaskPrioritizerAgent`/`GenericExecutorAgent` are confirmed
unaffected — still constructed with the existing default `IClaudeClient` path — via a regression
test, not left as an assumption from "those files weren't touched." GREEN: `TailoringAgentBase`'s
constructor takes `IClaudeClientFactory` in place of a pre-resolved `IClaudeClient`; the exact
mid-cycle client-swap mechanism is decided here, not prescribed above.

**T-E6.7 — Block assembly without a configured key.** RED: `JobApplicationAssemblyService
.AssembleAsync` refuses (`Result.Invalid`, clear message) when the caller has no `UserLlmSettings`
row. GREEN: check added early in `AssembleAsync`, before any persistence.

**T-E6.8 — Frontend Setup/Preferences page.** RED: the page renders an API-key field, a model
selector, a Verify button (calls T-E6.3, shows pass/fail inline, saves nothing on its own) and a
Save button (calls T-E6.4) — Verify is a confidence check, not a gate; Save works without a prior
Verify. GREEN: a Settings page component and its API calls.

## Definition of done

- A logged-in user can configure, verify, and save their own Anthropic API key and Claude model
  from a Setup/Preferences page.
- The stored key is encrypted at rest; the encryption keyring itself persists in Postgres,
  confirmed via T-E6.2's restart test to survive a process/container restart, not assumed to.
- Owner-scoped Claude calls (Epic 3's tailoring agents, job-posting ingestion) use the resolved
  caller's own key and model; the shared/generic board's agents are confirmed unaffected by a
  regression test, not just assumed to be because those files weren't touched.
- A `JobApplication` cannot be assembled without a configured, saved key for the caller.
- No plaintext API key ever appears in a log, in a response body after save, or in a committed
  file.

## Prerequisites

- `TaskFlow_Epic4_PostgresMigration.md` (Epic 4) must land first — the Data Protection keyring
  persistence design assumes Postgres is already the running database.

## Open decisions log

1. **Exact `TailoringAgentBase` client-swap mechanism** — deliberately deferred to T-E6.6's RED
   test, not decided here.
2. **Whether `HealthController` should also report per-user-settings-service health** — not
   addressed, out of scope for this epic; revisit only if it becomes relevant.
