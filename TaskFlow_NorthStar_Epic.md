# TaskFlow — North Star Epic: Document-Driven Autonomous Execution

This is a standalone working document for the next epic, built on top of the completed
architecture-and-TDD refactor (Slices A–L, shipped and green: 39 backend tests + 14 frontend).
That refactor is the finished record; **this** document is the live source of truth going
forward. The unit of work here is a **sprint**, not a slice.

**The one-sentence goal:** hand TaskFlow a specification document (like this one), and it parses
the document into tasks on its *own* Kanban board, then executor agents pick those tasks up and
do the work live while you watch on the dashboard.

**Guiding rule (unchanged):** the app builds and runs at the end of every sprint. We never tear
the house down. We add rooms while people are still living in it.

---

# Rules to follow for AI who are reading this

- **TDD is how we build everything.** Red (failing test) → green (simplest code) → refactor.
  When adding code, add its test coverage in the same change, up front, not later.
- **Strict DRY and SOLID.** Fix name collisions and duplication at the source, never by
  band-aiding (no aliasing a collision, no copy-pasted helper).
- **Do not agree on everything.** Come back with sound advice from the principles.
- **Do not band-aid.** If it is wrong, we fix it properly.
- **How to work:** follow this document top to bottom. Each sprint has explicit file paths,
  RED tests, GREEN code, a pastable PR description, and merge/delete steps. Bring bugs to chat;
  every fix gets recorded back into this document so the chat stays disposable.
- **Tooling boundary (important):** Claude can create and edit files directly in the repo, but
  Claude's sandbox CANNOT run `git` and does not run `dotnet` or `npm`. Claude writes the code
  and tests into the repo; YOU run every `git` / `dotnet` / `npm` command on your machine and
  report the result. This matches the TDD loop: you run, Claude writes.

**Standing rules (carried from the refactor, do not repeat the mistakes):**

- **Never claim you verified something you did not actually check.** Confirm against the real
  artifact: exact file name, real contents, actual test output. A false verification is worse
  than admitting you have not checked.
- **Separate facts from inferences; never state an inference as fact.** Say what you actually
  checked; label the rest as an inference for the user to confirm. The truth is whatever
  `dotnet test` / `npm run test` prints.
- **Never assume progress or mark work done without confirmation.** Do not tick off a step
  unless the repo or the user confirms it.
- **Enforce TDD order; halt the moment implementation lands before its failing test.** Call it
  out and stop, even if the user is moving ahead.
- **When the deliverable is code, deliver the actual code** with file path, namespace, and
  usings, paste-ready. Prose is for the test to encode, not a substitute for the class.
- **Never hand over anything you claim works but have not tested.** Test it, or state plainly it
  is untested and why.
- **Hold the whole map.** Read the whole document before advising so guidance fits overall scope.

**Naming conventions (carried forward):** domain types never reuse a .NET BCL name (the old
`TaskStatus` collided with `System.Threading.Tasks.TaskStatus`, hence `WorkflowStatus`); result
types live in `Common/`; npm package names stay lowercase.

---

# The Vision

Hand TaskFlow a specification document. TaskFlow:

1. **Parses** the document into discrete, well-formed work items.
2. **Creates** them as tasks on the Kanban board (To Do column).
3. **Agents pick them up** autonomously, move them across the board
   (To Do → In Progress → Review → Done), and actually do the work.
4. **You watch it happen live** on the dashboard via the SignalR feed already built.

In other words: the reactive agents (prioritize, detect staleness) grow into *executing* agents
(ingest, plan, act).

**The key point: it feeds TaskFlow's *own* board.** Ingestion does not build a separate pipeline
or a new task store. It writes through the *same repositories* into the *same `Tasks` table*
that backs the *same Kanban board* you already built. The executor agent is the *same agent
layer*. You watch on the *same React board* over the *same SignalR feed*. The document simply
becomes cards on your board, and the agents work them.

```mermaid
flowchart LR
    DOC["Spec document<br/>(a doc like this)"]
    ING["IngestionService<br/>parse → task drafts<br/>(a Service)"]
    RP["Repositories<br/>AddAsync"]
    BOARD[("TaskFlow board<br/>same Tasks table + Kanban")]
    EX["Executor agent<br/>claims + does work<br/>IClaudeClient"]
    CLAUDE["Claude API<br/>reason step"]
    YOU["You<br/>watching live"]

    DOC -->|1. hand it in| ING
    ING -->|2. write tasks| RP
    RP -->|3. tasks appear| BOARD
    BOARD -->|4. claim a To Do card| EX
    EX -. reason .-> CLAUDE
    EX -->|5. do work, move card| BOARD
    BOARD -. 6. SignalR live push .-> YOU
```

Everything in that diagram already exists except the two new boxes, `IngestionService` and the
executor agent, and both are built on seams the refactor already put in place. Nothing new is
invented at the data or transport layer.

---

# The Seams This Epic Builds On (already shipped and tested)

This epic does not start from scratch. It plugs into work that already exists and is covered:

- **Service layer.** The `Result<T>` type (`Common/`), interface-plus-implementation services,
  and mocked-repository unit tests. New services (like ingestion) follow this exact pattern.
- **Repositories.** `ITaskRepository` / `IUserRepository` / `IAgentLogRepository` with
  `AddAsync` / `SaveChangesAsync`. All task writes go through these. `TaskItem` uses
  `WorkflowStatus` (Todo / InProgress / Review / Done) and `TaskPriority`.
- **The Claude seam.** `IClaudeClient` (with `IsConfigured` + `SendAsync`) and the abstract
  `ClaudeAgentBase` that owns the tool-use loop, action logging, and lifecycle broadcasts.
  Executor agents subclass `ClaudeAgentBase`. Agents are unit-testable with `StubClaude`.
- **Agent runner.** `AgentRunner` (an `IHostedService`) discovers every `ITaskFlowAgent` and
  runs it on its interval.
- **Live feed.** `AgentHub` (SignalR) + `IAgentNotifier` + `HubEvents` (`AgentAction`,
  `AgentCycle`). The React dashboard already renders this feed via `useAgentFeed`.
- **Frontend layers.** `api/ hooks/ components/ features/ lib/`, with a Vitest + RTL + MSW test
  harness (`__mocks__/@microsoft/signalr.ts` for the SignalR stub).

If a sprint needs a seam that does not exist yet, that is a signal to build the seam test-first,
not to reach around it.

---

# The TDD Loop and Git Workflow (per sprint)

The loop is identical to the refactor:

1. Claude writes a failing test (RED) with exact file path, namespace, and usings.
2. You run `dotnet test` / `npm run test` and confirm it is red.
3. Claude writes the simplest code to pass (GREEN).
4. You run again and confirm green.
5. Refactor if needed, tests staying green.

**Per-sprint PR cadence.** One branch and one PR per sprint into `develop`, merged and deleted.
`develop → main` is the release point at natural milestones. Branch names use
`feature/sprint-N-short-name`. Both `main` and `develop` are protected, so releases go through a
PR, never a local fast-forward.

---

# Sprint Plan

The old refactor mapped this destination as future slices M–R. Renumbered fresh for this epic:

| Sprint | What | Leans on |
|--------|------|----------|
| **1** | `IDocumentIngestionService`: parse a spec doc into task drafts | Service-layer pattern |
| **2** | Ingestion endpoint + UI to upload a document and preview drafts | Thin controller + frontend layers |
| **3** | Task drafts → board tasks with provenance (which doc, which section) | Repositories |
| **4** | Executor agent: claims a To Do task, plans sub-steps, works it | `IClaudeClient` + `ClaudeAgentBase` |
| **5** | Board transitions driven by the executor, streamed live | Repositories + SignalR |
| **6** | Guardrails: human approval gates, cost caps, rollback on failure | All of the above |

Each sprint is specified in full, small and test-first, when we reach it. The stubs below fix
the destination and the seams; they are not yet the RED/GREEN code.

---

## Sprint 1 — Document Ingestion Service

**Goal:** a testable service that turns a specification document into a list of well-formed task
drafts, with no HTTP and no Claude in the test.

**Produces:** `IDocumentIngestionService` + implementation (service-layer style, returning
`Result<IReadOnlyList<TaskDraft>>`), and a `TaskDraft` shape (title, description, provenance
placeholder). Unit tests over the parsing/splitting rules using plain in-memory input.

**Test approach:** feed representative document text, assert the draft count and fields. The
parsing rule (see open questions) is the unit under test, so it must be deterministic; if a
Claude-assisted parse is chosen, put it behind the `IClaudeClient` seam so the test uses
`StubClaude`.

**Design points to settle here:** granularity of parsing (one task per heading vs per checklist
item vs Claude's judgment), and whether parsing is rules-based, Claude-assisted, or both.

---

## Sprint 2 — Ingestion Endpoint + Upload/Preview UI

**Goal:** hand a document to the app over HTTP and preview the drafts before they hit the board.

**Produces:** a thin controller endpoint (calls the Sprint 1 service, `.ToActionResult()`), and a
frontend upload-and-preview screen in `features/` that calls a new `api/` function and renders
the drafts. Controller test with a mocked service; frontend api/hook tests against MSW; a preview
component test in the RTL style.

**Leans on:** the thin-controller pattern and the `api/ hooks/ components/ features/` split.

**Design point:** preview is the natural human checkpoint before anything is written, so this
sprint sets up the approval surface that Sprint 6 hardens.

---

## Sprint 3 — Drafts Become Board Tasks (with provenance)

**Goal:** approved drafts are written as real tasks in the To Do column, each carrying provenance
(which document and section it came from).

**Produces:** persistence of drafts through `ITaskRepository.AddAsync`, a provenance field on the
task model (migration + `WorkflowStatus.Todo` default), and repository/service tests over the
write path using real in-memory SQLite.

**Leans on:** the repositories and the existing `Tasks` table. This is the point where the
document truly becomes cards on the same board.

**Design point:** provenance shape (document id + section anchor) so a task can be traced back,
and so the executor in Sprint 4 has context to work from.

---

## Sprint 4 — Executor Agent

**Goal:** an agent that claims a To Do task, plans sub-steps, and does the work, tested with
canned Claude responses rather than live calls.

**Produces:** a new `ClaudeAgentBase` subclass registered as an `ITaskFlowAgent`, its tool set
(claim, record progress, request review), and agent tests using `StubClaude` and in-memory
SQLite, asserting real board/log side effects.

**Leans on:** `IClaudeClient`, `ClaudeAgentBase`, `AgentRunner`. This is the reactive-to-executing
jump: the same agent layer, now doing work instead of only re-prioritizing.

**Design point (carried forward):** an executor doing real work needs firm limits. We already
have a per-task iteration cap on the tool loop; this sprint keeps the leash short and defers the
spend ceiling and destructive-action gate to Sprint 6.

---

## Sprint 5 — Live Board Transitions

**Goal:** as the executor works, the task moves across the board (To Do → In Progress → Review →
Done) and the dashboard shows it live.

**Produces:** status transitions written through the repositories and broadcast over
`IAgentNotifier` / `HubEvents`, plus any dashboard adjustment to render executor activity
distinctly from the existing prioritizer/stale feeds.

**Leans on:** repositories + SignalR (`AgentHub`, `useAgentFeed`). No new transport is invented;
the transitions ride the feed that already exists.

**Design point:** what counts as Review vs Done for an autonomous executor, and whether Review
always requires a human (ties into Sprint 6).

---

## Sprint 6 — Guardrails

**Goal:** make autonomous execution safe: human approval gates, a spend ceiling, and rollback on
failure, designed in rather than bolted on.

**Produces:** a human-approval checkpoint (approve each task, approve the batch, or fully
autonomous with a kill switch, per the open question), a cost cap on Claude usage, and a
rollback path when an executor step fails, all covered by tests.

**Leans on:** everything above. This is the "give the agent an escape hatch and a leash"
principle from the stale-task agent, scaled up to an agent that changes real state.

---

# Open Questions to Resolve Before Sprint 1

Recorded now so they are not forgotten; answered when we reach the relevant sprint, not before:

- How granular should document parsing be: one task per heading, per checklist item, or Claude's
  judgment?
- Do executor agents write code/files, or only orchestrate and report? (Scope and safety.)
- What is the human-in-the-loop checkpoint: approve each task, approve the batch, or fully
  autonomous with a kill switch?
- Is parsing rules-based, Claude-assisted, or a hybrid? (Determines how Sprint 1 is tested.)

---

# Definition of Done (this epic)

- A specification document handed to TaskFlow becomes tasks on its own Kanban board.
- An executor agent claims those tasks and moves them across the board, doing real work.
- Every new service, repository write, and agent tool handler is covered by a test in the
  established style (mocked repositories for services, real in-memory SQLite for repositories and
  agents, `StubClaude` for the Claude seam, MSW for the frontend).
- Guardrails (approval gate, spend cap, rollback) are in place and tested before execution is
  turned loose on anything destructive.
- `dotnet test` and `npm run test` both green; both apps build and run.
- Each sprint shipped as its own small PR into `develop`.
