# TaskFlow — System Architecture Overview

**What this doc is:** a snapshot of TaskFlow's current architecture as a whole — how the API,
web client, background agents, and data layer fit together. It is not an epic doc (no sprints, no
RED/GREEN tasks) and it does not replace any of them. When a fact here conflicts with an epic doc,
the epic doc wins for anything in flight; this doc should be refreshed after major structural
changes land, not treated as eternally current. Written 2026-08-24 against `develop`.

**Companion docs** (unchanged by this one): `TaskFlow_Epic4_PostgresMigration.md`,
`TaskFlow_Epic5_DeploymentInfrastructure.md`, `TaskFlow_Epic6_UserCredentials.md`,
`TaskFlow_Epic3.2_JobPostingUrlImport.md` describe planned or in-flight structural changes. Where
this doc says "today"/"currently," check those before assuming it still holds.

---

## 1. What TaskFlow is

A single-tenant, personal-scale job-application tracker with a Kanban board at its center, plus an
Epic-3 workflow that turns a job posting + a base resume into a tailored resume and cover letter,
using Claude both as an autonomous task executor and as a document-tailoring writer. Two kinds of
"worker" populate the board: the human user, and a set of always-on background agents that claim,
work, and hand back tasks the same way a human collaborator would — visible in real time over
SignalR rather than requiring a page refresh.

## 2. Component map

```
┌─────────────────────────────┐        HTTPS (JWT bearer)        ┌───────────────────────────────┐
│   TaskFlow.Web (React/Vite)  │ ────────────────────────────────▶│   TaskFlow.Api (ASP.NET Core)  │
│                               │                                   │                                │
│  features/ (Board, Ingest,   │  WebSocket (SignalR, JWT via      │  Controllers → Services →      │
│  Activity, Archive, Login)   │◀──────────query string)──────────▶│  Repositories → AppDbContext   │
│  hooks/ (useAgentFeed,       │        /hubs/agents               │                                │
│  useBoardTasks, ...)         │                                   │  Agents/ (ITaskFlowAgent x5,   │
│  lib/agentHub.tsx (1 shared  │                                   │  AgentRunner background loop)  │
│  HubConnection for the app)  │                                   │                                │
└─────────────────────────────┘                                   │  Export/ (Typst → PDF/MD)      │
                                                                    │  Ingestion/ (parsers, SSRF-safe│
                                                                    │  URL fetch)                    │
                                                                    └───────────────┬────────────────┘
                                                                                     │ EF Core
                                                                                     ▼
                                                                          ┌─────────────────────┐
                                                                          │  SQLite (today)      │
                                                                          │  → Postgres (Epic 4) │
                                                                          └─────────────────────┘
```

## 3. Backend (`TaskFlow.Api`)

ASP.NET Core 10.0 Web API. Layering is Controller → Service → Repository → `AppDbContext`
(EF Core), with a `Result<T>` type (`Common/Result.cs`) as the shared success/failure vocabulary
between Service and Controller layers (`Ok`/`NotFound`/`Invalid`/`Conflict`/`Unauthorized`/
`InternalError`) instead of throwing for expected failure cases.

### Controllers
- `AuthController` — login/register, issues JWTs. No `[Authorize]` (by necessity).
- `HealthController` — health check. No `[Authorize]`.
- `TasksControllers.cs` (class `TasksController`) — CRUD and status transitions for the Kanban board.
- `JobApplicationsController` — the Epic 3 application resource: assembly, approve/reject, export.
- `IngestionController` — document/job-posting ingestion endpoints.
- `AgentsController` (`api/agents`) — executor enable/disable switch and agent control.
- `AgentDiagnosticsController` — agent inspection/diagnostics.
- `AgentLogsController` — read access to the `AgentLog` activity feed.
- `FilesController` — resume/cover-letter file downloads.

Every controller except `AuthController`/`HealthController` requires a valid JWT.

### Services, Export, Ingestion
- **Services** (`Services/`): `AuthService`, `JwtService`, `ClaudeClient` (Anthropic SDK wrapper,
  `IsConfigured` gate), `DailyExecutorSpendGuard` (daily Claude-spend cap), `JobApplicationService`,
  `ResumeContextService`, `TaskService`, `ExecutorSwitch` (agent enable/disable + wake signal),
  `SignalRAgentNotifier` (the one place that pushes agent events onto the hub).
- **Export** (`Export/`): `ExportService` renders a tailored resume/cover-letter to Markdown
  (raw bytes) or PDF (Markdown → Typst via `TailoredContentTypstRenderer`, compiled by
  `ProcessTypstCompiler` shelling out to a `typst` binary, using a cached template from
  `FileTemplateProvider`). Filenames are self-identifying (`Name_Document_Company.ext`), sanitized
  against a fixed Windows-reserved-character set unioned with `Path.GetInvalidFileNameChars()` so
  the result is safe regardless of which OS is hosting the API.
- **Ingestion** (`Ingestion/`): tiered parsers — a free rule-based pass first
  (`SpecDocumentParser`/`JobPostingParser`), escalating to Claude
  (`ClaudeIngestionParser`/`ClaudeJobPostingParser`) only when the free pass can't produce a clean
  result (`TieredIngestionParser`/`JobPostingIngestionParser`). URL-based job-posting import adds an
  SSRF-safe fetch path: `SsrfSafeConnectCallback` + `IDnsResolver` + `UrlValidation` block
  metadata-endpoint, private-network, loopback, and DNS-rebinding targets before any outbound
  request is made (Epic 3.2).

### Agents — the execution model
`Agents/ITaskFlowAgent` is the contract every background worker implements: a `Name`, an
`Interval`, `RunAsync` (one observe → reason → act cycle), and `WaitForWakeSignalAsync` (lets a
human action — e.g. re-enabling the executor — shorten the wait instead of idling out the full
interval).

`AgentRunner` is a single `BackgroundService` that, on startup, resolves every registered
`ITaskFlowAgent` and runs one independent loop per agent concurrently. Each loop iteration opens a
fresh DI scope (so `AppDbContext` and friends are scoped-fresh per cycle), catches and logs
exceptions without killing the loop, and waits for `min(Interval, wake signal)` between cycles via
`Task.WhenAny` racing against a linked, cancelled-afterward `CancellationTokenSource` (so a losing
wait doesn't leak in the semaphore's queue — a real bug fixed in PR #70).

`ClaudeAgentBase` (abstract) is the shared base for every Claude-backed agent: it drives a bounded
(max 10 iterations) tool-use conversation against `IClaudeClient`, dispatches each `tool_use` block
to a caller-supplied dispatcher, and provides `RecordActionAsync`/`RecordCycleSummaryAsync`
(persist `AgentLog` + broadcast over SignalR) and cycle-started/completed notifications.

Five registered agents, each a `ClaudeAgentBase` subclass with its own prompt/tool set:
`GenericExecutorAgent` (claims the oldest Todo+Generic task, works it, hands to Review — never
Done, approval stays human-only), `ResumeTailoringAgent` / `CoverLetterAgent` (Epic 3's paired
tailoring agents), `TaskPrioritizerAgent`, `StaleTaskAgent`.

Three additional plain `BackgroundService`s (not `ITaskFlowAgent`s, no Claude involved) run
best-effort recovery sweeps: `StaleClaimReaperService`, `JobApplicationPromotionReconcilerService`,
`JobApplicationApprovalReconcilerService`.

### Real-time: SignalR
`Hubs/AgentHub.cs` (`/hubs/agents`, `[Authorize]`) is server-push only — no client-invokable RPCs
beyond connection lifecycle. On connect, it reads the JWT's `NameIdentifier` claim and joins the
connection to a per-user group (`user-{id}`), so Epic 3 activity can target just the owning user.
`Hubs/HubEvents.cs` is the event-name contract shared with the client: `AgentAction`, `AgentCycle`,
`TaskMoved`.

### Auth
JWT Bearer, `HmacSha256`, key/issuer/audience/expiry from config (`Jwt:*`). Claims: `NameIdentifier`
(user id), `Email`, `Name`. Because a WebSocket handshake can't carry an `Authorization` header, a
custom `OnMessageReceived` handler pulls the token from the `access_token` query string specifically
for requests under `/hubs`.

### Data (EF Core)
One `DbContext` (`Data/AppDbContext.cs`) with five `DbSet`s: `Users`, `Tasks`, `AgentLogs`,
`JobApplications`, `ResumeContexts`. Notable model configuration: `TaskItem.Status`/`Priority`/
`Kind` and `JobApplication.State` are stored as strings (not int enums) for readability in raw
queries; `TaskItem → JobApplication` cascades on delete, `TaskItem → User` (assignee) sets null;
`JobApplication` and `ResumeContext` both carry a unique composite index on
`(IngestionSessionId, OwnerId)`. `TaskItem.OwnerId` is a computed, non-mapped property that reads
through `Application.OwnerId` and throws if the caller forgot to `.Include(Application)` —
deliberately loud rather than silently wrong.

**Current engine: SQLite** (`options.UseSqlite(...)`, confirmed in `Program.cs` and the `.csproj`'s
package references — no Npgsql reference exists yet). Migration to PostgreSQL is planned, not yet
started (see `TaskFlow_Epic4_PostgresMigration.md`) — kept deliberately for learning value after the
original scaling motivation was explicitly declined (TaskFlow stays single-replica by design).
Migrations run automatically at boot (`db.Database.Migrate()` in `Program.cs`).

### Middleware pipeline order (`Program.cs`, comment: "ORDER MATTERS")
Swagger (dev only) → HTTPS redirection → CORS (`DevPolicy`, locked to `http://localhost:5173`,
credentials allowed, `Content-Disposition` exposed for cross-origin download filenames) →
Authentication → Authorization → `MapHub<AgentHub>("/hubs/agents")` → `MapControllers()`.

## 4. Frontend (`TaskFlow.Web`)

React + TypeScript on Vite, Tailwind-based "Nocturne" dark design system (`lib/tokens.ts`).

- **`features/`** — one component per screen: `KanbanBoard`, `Dashboard`, `Activity` (agent feed
  page), `Archive`, `IngestDocument`, `Login`, `Navigation`.
- **`components/`** — board/task presentation (`TaskCard`, `TaskCardView`, `KanbanColumn`),
  Epic 3 review UI (`ApplicationReviewCard`, `ReviewActions`, `ExportDownloadControls`,
  `TailorButton`), agent visibility (`AgentStatus`, `AgentFeedList`, `ExecutorControl`), plus
  `components/ui/` shared primitives (`Button`, `ColumnHeader`).
- **`hooks/`** — one hook per piece of live/derived state: `useAgentFeed` (activity feed),
  `useBoardTasks` (board state, kept live via the `TaskMoved` SignalR event), `useArchivedTasks`,
  `useExportDownload`, `useIntakeFlow`/`useIngestion`, `useApplicationReview`,
  `useExecutorControl`, `useBaseResumeCapture`/`useBaseResumeReuse`, `AuthContext`/`AuthProvider`.
- **`api/`** — thin fetch wrappers per resource (`client.ts` base + `auth`, `tasks`,
  `jobApplications`, `ingestion`, `agentLogs`, `executor`, `files`), all reading the JWT via
  `client.ts`'s `getToken()`.
- **`lib/`** — `agentHub.tsx` builds and owns the **single, app-wide** SignalR `HubConnection`
  (`accessTokenFactory` supplies the JWT), exposed via `AgentHubContext` so every feature hook
  subscribes to one shared connection instead of each opening its own; `board.ts` (drop-target
  resolution, task grouping/pairing, `displayTitle`, `canDownloadExport` — shared board logic
  extracted specifically to avoid duplicating conditions across components); `hubEvents.ts` mirrors
  the backend's `HubEvents` contract; `tokens.ts` (design tokens); `taskKind.ts`, `formatting.ts`,
  `intakeSteps.ts`, `openTextInNewTab.ts`, `devAuthReset.ts`.

## 5. Key flows

**Generic task execution (the original loop):** a `Todo`+`Generic` task sits on the board →
`GenericExecutorAgent` claims it on its next cycle (if the executor switch is on, Claude is
configured, and the spend guard allows it) → works it via a tool-use conversation, recording
progress → moves it to `Review` → a human Approves or Rejects. Every step broadcasts over SignalR so
the board updates live without a refresh.

**Epic 3 resume/cover-letter tailoring:** a job posting + base resume are ingested (free parse,
escalating to Claude only if needed) → a `JobApplication` plus a paired `ResumeTailoring` +
`CoverLetterTailoring` task are assembled → `ResumeTailoringAgent`/`CoverLetterAgent` run in
parallel → once **both** siblings reach `Review`, the board renders them as one combined
`ApplicationReviewCard` (not two separate cards) → Approve/Reject acts on the pair together →
once `Done` and the application is `Approved`, `ExportDownloadControls` can produce a
named PDF or Markdown file via `ExportService`.

## 6. Cross-cutting concerns

- **Authorization boundary:** every owned resource (`JobApplication`, and `TaskItem` through it)
  checks caller ownership in the Service layer, collapsing "not found" and "not yours" into the
  same `NotFound` result — avoids leaking existence of other users' data via a `403` vs `404`
  distinction.
- **Cost control:** `DailyExecutorSpendGuard` gates every Claude-backed agent cycle behind a daily
  spend cap, checked before any paid call is made.
- **SSRF safety:** any server-side fetch of a user-supplied URL (job-posting URL import) goes
  through DNS-resolution validation and a custom `SocketsHttpHandler.ConnectCallback**
  (`SsrfSafeConnectCallback`) that re-checks the resolved IP at actual connection time, closing the
  classic "resolve-then-check" DNS-rebinding gap.
- **Resilience in the agent loop:** one agent's exception doesn't stop its own future cycles or any
  other agent's loop; a crashed claim gets rolled back rather than left stuck; a separate reaper
  sweep recovers claims an agent process never got to roll back itself (crash, restart).

## 7. Current deployment state and what's planned

Today: local dev only — `dotnet run`/`npm run dev` via the root `.\run` convenience script,
SQLite file on disk, no containerization. Planned, not yet built (see the respective epic docs for
full designs):

| Epic | Change |
|---|---|
| Epic 4 | SQLite → PostgreSQL (learning-value migration; squashed to one fresh baseline, no data to port) |
| Epic 5 | Standalone Windows executable **and** a Docker/Kubernetes track (single-replica, by decision — no horizontal-scaling backplane is being built) |
| Epic 6 | Per-user Anthropic API key + model selection, replacing the single shared deployment-level key (depends on Epic 4's Data Protection keyring persistence) |
| Epic 3.2 | Job-posting URL import via SSRF-safe server-side fetch (in progress/recently shipped — verify against its own doc for current status) |

---

*Refresh this doc after a structural change lands (new agent, new controller, DB engine swap,
etc.) rather than letting it drift — it's meant to be a reliable map, not a historical record like
the completed epic docs.*
