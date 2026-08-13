# TaskFlow — Epic 4: Database Migration (SQLite to PostgreSQL)

**Epic map, stated once here to prevent mix-ups across these three cross-referencing docs:**
this is **Epic 4**. `TaskFlow_Epic5_DeploymentInfrastructure.md` is **Epic 5** — the companion
covering how each deploy target *runs* the Postgres this epic migrates to. Epic 6
(`TaskFlow_Epic6_UserCredentials.md`) depends on this epic's Data Protection keyring-persistence
groundwork and is sequenced after it.

Cross-epic, like Epic 5, which this doc is a companion to — that doc covers *how each deploy
target runs* Postgres; this doc covers the actual work of moving `TaskFlow.Api`'s persistence off
SQLite. Same standing framework as everywhere else in this project: strict TDD (RED before GREEN,
confirmed), clean code, SOLID, DRY. Every task below that touches real C# code follows that
discipline exactly like an Epic 3 sprint; the couple of tasks that are closer to infrastructure
(regenerating migration files) are marked as such rather than dressed up as literal unit tests,
matching the honesty standard Epic 5 already set.

**Why this exists:** originally raised while architecting Kubernetes for Epic 5, when SQLite's
single-writer, single-file model looked like it would block running more than one `TaskFlow.Api`
replica. A full architectural discussion followed (see "Scaling beyond one replica" below) and
concluded TaskFlow should **not** chase horizontal scaling — single-tenant, personal-scale, no real
traffic justifies that weight. Kubernetes stays at one replica, by decision, permanently.

**That resolved the original technical problem without needing a new database at all.** At one
replica, SQLite plus a persisted volume works fine — there is no multi-writer conflict to avoid.
**Revisited directly, 2026-08-11: is this migration even still needed?** Honestly, no, not
technically. The other real driver considered — Epic 6's Data Protection keyring needing to survive
container restarts — has a SQLite-compatible answer too (`PersistKeysToFileSystem`, pointed at the
same persisted volume as the database file), no client-server database required.

**Decision: keep the migration anyway, explicitly for the learning value** — the same bucket
Docker and Kubernetes themselves are already in. Running a real client-server database, with its
own `StatefulSet` in Kubernetes (Epic 5), is genuinely valuable hands-on experience, independent of
whether this specific app needs it. Recorded here plainly rather than left implied: **this epic
exists because it's worth building, not because the architecture requires it.** Every "why Postgres"
justification below should be read with that in mind — Postgres is still the right engine to pick
*for the learning exercise* (open-source, first-class EF Core support via Npgsql, the standard
Docker/Kubernetes pairing), not because this app's actual requirements demand it.

---

## Decisions owned here

- **Engine: PostgreSQL.** Settled in the deployment-target conversation; not re-litigated here.
  Kept on being asked directly whether it's still needed after scaling was declined (2026-08-11) —
  see "Why this exists" above for the honest answer.
- **Standardized everywhere, no dual-provider SQLite/Postgres split.** The standalone exe track
  (Epic 5, Track A) also moved to requiring a reachable Postgres connection string rather than
  keeping SQLite as a parallel-maintained path — one engine, one migration history, one behavior
  profile to reason about, at the cost of the exe no longer being a fully zero-dependency download.
  That tradeoff was made deliberately, not assumed.
- **Migrations are squashed to one fresh Postgres baseline, not ported.** Confirmed repeatedly
  throughout `TaskFlow_Epic3_ResumeBuilder.md`: no real user data exists in any environment yet.
  Replaying ~12 accumulated SQLite-era migrations against a different SQL dialect isn't
  meaningfully possible anyway (migration history is provider-specific under the hood) — the
  practical and only-available-now move is dropping them and generating one clean initial migration
  against the current entity model. This gets meaningfully more expensive the moment real data
  exists anywhere, so it's being done now rather than deferred.
- **Testing continues this repo's existing habit of testing against the real engine, not a
  mock — via Testcontainers, not a new philosophy.** The Epic 3 doc's own RED-test descriptions
  repeatedly specify "real SQLite" rather than an in-memory fake. `Testcontainers.PostgreSql` (a
  mature .NET package that spins up a real, throwaway Postgres instance in Docker per test run) is
  the direct continuation of that habit for the new engine, not a departure from it. This needs
  Docker available to run the test suite locally — already true once Epic 5's Track B work exists,
  and reasonable to require before then too, since Docker Desktop is free and this project already
  leans on it for Epic 5's Track A exe Postgres dependency.
- **A few Postgres-specific behavior differences are called out as their own verification tasks
  below, not assumed safe by the provider swap alone:** `[MaxLength]` enforcement becomes real at
  the DB level (SQLite's `TEXT` columns don't enforce it; Postgres's `varchar(n)` mapping does),
  `DateTime.Kind` handling is stricter under Npgsql, and any implicit case-insensitivity assumption
  in existing string-comparison queries needs auditing. Each gets its own task rather than being
  folded silently into "swap the provider."

---

## Goal

`TaskFlow.Api` persists to PostgreSQL instead of SQLite, with no behavior regression, a clean
single-baseline migration history, and the provider-specific gotchas above verified rather than
assumed away.

## Files involved

- `TaskFlow.Api/TaskFlow.Api.csproj` (edit — swap `Microsoft.EntityFrameworkCore.Sqlite` for
  `Npgsql.EntityFrameworkCore.PostgreSQL`)
- `TaskFlow.Api/Program.cs` (edit — `UseNpgsql(...)` replacing `UseSqlite(...)`, connection string
  key, `EnableRetryOnFailure()`)
- `TaskFlow.Api/Migrations/` (existing SQLite-era migrations removed; one new baseline migration
  added)
- `TaskFlow.Tests/TaskFlow.Tests.csproj` (edit — add `Testcontainers.PostgreSql`)
- `TaskFlow.Tests/Fixtures/PostgresFixture.cs` (new — shared Testcontainers fixture)
- Existing repository/service test files that currently instantiate a real SQLite context —
  updated to use the new fixture rather than rewritten from scratch, per DRY (one fixture, reused,
  not copy-pasted per test class)

## Tasks

**TP.1 — Testcontainers.PostgreSql test fixture.** Infrastructure, not itself a failing-test-first
task — this is what later RED tests in this doc run against. `PostgresFixture` (xUnit
`IAsyncLifetime`/collection fixture) starts a real Postgres container for the test run and exposes
its connection string. Verification: the fixture starts and stops cleanly; a trivial connection
against it succeeds.

**TP.2 — EF Core provider swap.** RED: a schema/migration test using TP.1's fixture — apply
migrations against a real Postgres, assert the expected tables exist — fails today because the app
is still wired to SQLite. GREEN: `Npgsql.EntityFrameworkCore.PostgreSQL` package,
`UseNpgsql(...)` in `Program.cs`, connection string reshaped from a SQLite file path to
`ConnectionStrings:Default` (host/port/database/credentials).

**TP.3 — Squash to one Postgres baseline migration.** Not conventional RED/GREEN — matches this
project's own precedent for static/generated artifacts (Sprint 5's Typst templates, verified by a
real smoke run rather than a unit test). Delete the SQLite-era migration files, run
`dotnet ef migrations add InitialCreate --project TaskFlow.Api` fresh against the current entity
model. Verification: `dotnet ef database update` against TP.1's fixture applies cleanly in one
step and produces the complete current schema — every table, column, constraint, and index
currently defined by the entity model, not just the ones exercised by existing tests.

**TP.4 — `[MaxLength]` enforcement is now real; prove it.** RED: insert a `TailoredContent` value
exceeding `TaskItem.TailoredContentMaxLength` directly at the repository layer, bypassing
`ToolOutputValidator` entirely, against real Postgres (TP.1's fixture) — assert the database itself
rejects it. This test could not have meaningfully existed under SQLite (there was nothing at the DB
level to test) — it's new, not ported. GREEN: none expected if Npgsql's default `[MaxLength]`
mapping already enforces it via `varchar(n)` — confirmed by the test passing, not assumed in
advance.

**TP.5 — `DateTime.Kind` round-trip verification.** RED: persist an entity with `CreatedAt =
DateTime.UtcNow` (the pattern already used consistently throughout this codebase — confirmed) and
read it back from real Postgres; assert no Npgsql `DateTime.Kind`-mismatch exception and the
round-tripped value is correct. Expected to pass without production code changes given existing
`UtcNow` usage, but stated as a test to run, not a fact to assume — Npgsql is measurably stricter
about this than SQLite ever was.

**TP.6 — Case-sensitivity audit.** Not a specific known bug — an explicit audit task, since
Postgres's default collation is case-sensitive where SQLite's `LIKE`/`==` translation can behave
more leniently. Confirm during this task whether any existing query (e.g. a login lookup by
username/email) relies on implicit case-insensitivity; write a RED test only if a real gap is
found, rather than presuming one exists.

**TP.7 — Connection resiliency.** `EnableRetryOnFailure()` added to the Npgsql provider
registration — a network-backed database introduces transient-failure modes an in-process SQLite
file never had. Verified by code review and the Testcontainers integration suite's continued
reliability across runs, not a dedicated adversarial test — stated honestly as a lighter
verification bar than the tasks above, not dressed up as more than it is.

## Definition of done

- `TaskFlow.Api` persists to Postgres; no SQLite package reference or `UseSqlite` call remains
  anywhere in the solution.
- One clean baseline migration applies the full current schema in one step.
- `[MaxLength]`, `DateTime.Kind`, and case-sensitivity behavior are each verified against the real
  engine, not assumed carried over from SQLite.
- The test suite runs against real Postgres via Testcontainers, continuing this project's existing
  test-against-the-real-thing convention rather than introducing mocking.

## Prerequisites

None within this doc — this is foundational work. It unblocks Epic 5's Tracks A/B/C (all three now
depend on Postgres being the running database) and Epic 6 (its Data Protection keyring persistence
design assumes Postgres is already in place).

---

## Scaling beyond one replica

Recorded in full here, once, so this reasoning doesn't need to be re-derived if the question comes
up again — and so it isn't silently lost the way a "considered and declined" decision often is
when it's only ever spoken, not written down.

**Postgres alone does not make `TaskFlow.Api` horizontally scalable.** It removes the
database-side blocker (SQLite's single-writer model), but a second, unrelated blocker remains,
found during this same discussion: **SignalR connections are pinned to whichever replica a client
connected to.** Run two replicas behind a load balancer, and a broadcast triggered on replica A
never reaches a browser connected to replica B — live board updates would silently and randomly
stop working depending on which pod a given user happened to land on. The standard fix is a
backplane (`Microsoft.AspNetCore.SignalR.StackExchangeRedis`, i.e. Redis) rebroadcasting messages
across every replica.

**Encouragingly, the agent/claim-loop side of this app was already built to tolerate multiple
concurrent workers, without anyone setting out to build that.** `TryClaimNextAsync`'s atomic
guarded `UPDATE`, `TryPromoteToReviewReadyAsync`, `TryApprovePairAsync`/`TryRejectPairAsync` — all
race-safe by construction (confirmed throughout `TaskFlow_Epic3_ResumeBuilder.md`'s Sprint 2–4R
history). N replicas of `ResumeTailoringAgent`/`CoverLetterAgent`/`StaleClaimReaperService`/
`JobApplicationPromotionReconcilerService` polling concurrently is exactly what those guards were
built to make safe. SignalR is the one piece that doesn't scale for free.

**A third real constraint: the Anthropic API rate limit is shared across every replica.** More
replicas running agent loops means more concurrent Claude calls against the same account limit.
Real scale needs actual request queuing/backoff here, not just more pods — this is exactly the work
Epic 2 Sprint 8 ("Claude retry/resilience") deferred indefinitely. That deferred work stops being
optional the moment multi-replica scale is seriously pursued.

**On the database tier itself:** a single Postgres instance has its own ceiling — connection count
first (fixed by **PgBouncer**, a pooler, once replica count grows enough to pressure it), and the
honest industry-standard answer for real scale is that you don't self-host the database in
Kubernetes at all — you run a **managed service** (RDS, Cloud SQL, Azure Database for PostgreSQL)
outside the cluster and keep only the stateless app tier in K8s. Self-hosting a stateful database
in Kubernetes (Epic 5's `StatefulSet`) is genuinely good to have *built once* for the learning
value; it's not what a team chasing real production scale would run long-term.

**Decision: none of the above is being pursued right now.** TaskFlow is single-tenant, personal-
scale, with no real traffic behind it — building a Redis backplane, PgBouncer, and a managed
database migration for an app with two seed users is real, ongoing operational weight for no
corresponding benefit. Kubernetes stays at one replica (see Epic 5). If this is ever revisited, the
order that would actually matter is: (1) SignalR Redis backplane — required before running any
additional replica at all, not optional; (2) PgBouncer — once replica count actually pressures the
connection limit; (3) a managed database — the real "how would this be done at scale" answer,
traded against the `StatefulSet` learning exercise already done.

---

## Open decisions log

1. **Whether/when to pursue the three scaling items above** — explicitly not decided now, not
   silently ruled out either. Revisit only if a real reason to run more than one replica shows up.
2. **Case-sensitivity audit (TP.6)** — whether a real gap exists isn't known yet; confirm during
   that task rather than assuming either answer.
3. **~~Whether this epic is still needed at all, now that horizontal scaling is declined~~ —
   settled 2026-08-11.** Directly asked and directly answered: no, not technically — SQLite plus
   `PersistKeysToFileSystem` on a persisted volume would satisfy everything Epic 5 and Epic 6
   actually require at one replica. Kept anyway, explicitly for the learning value. Not an open
   item; recorded here so the question isn't re-asked from scratch later without this answer in
   view.
