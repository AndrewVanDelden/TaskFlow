# TaskFlow — Epic 5: Deployment and Infrastructure

**Epic map, stated once here to prevent mix-ups across these three cross-referencing docs:**
this is **Epic 5**. `TaskFlow_Epic4_PostgresMigration.md` is **Epic 4** — the database migration
this epic's Tracks A/B/C all depend on being done first. `TaskFlow_Epic6_UserCredentials.md` is
**Epic 6** — unrelated to deployment mechanics, referenced here only because its Data Protection
key-persistence design depends on Postgres (Epic 4) being the running database.

Cross-epic, not scoped to any single feature epic. Covers how TaskFlow actually gets built and run
outside a dev machine: a standalone Windows executable, and a Docker/Kubernetes track built
explicitly for hands-on learning value alongside its practical use. This doc follows the same
convention as the epic docs: decisions are made and recorded before work starts, tasks carry
explicit verification criteria, and nothing is marked done without being actually checked. Same
standing framework as everywhere else in this project — strict TDD, clean code, SOLID, DRY. Where a
task in this doc changes real C# code (config binding, connection wiring), it follows the same
RED-before-GREEN discipline as the epic docs. Where a task is purely infrastructure (a Dockerfile
building, a Kubernetes manifest applying cleanly), there is no unit test to write, so an explicit
verification criterion stands in for RED/GREEN — stated as such, not silently treated as equivalent.

**Companion docs, cross-referenced rather than duplicated (DRY at the doc level, not just the code
level):**
- **`TaskFlow_Epic4_PostgresMigration.md`** — the actual work of moving `TaskFlow.Api` off SQLite
  onto PostgreSQL. This doc assumes that migration is done and describes how each deploy target
  *runs* Postgres; it does not re-describe the EF Core provider swap, migration squash, or
  testing-strategy changes that doc owns.
- **`TaskFlow_Epic6_UserCredentials.md`** — per-user Anthropic API keys/model selection. Unrelated
  to deployment mechanics; referenced here only because its Data Protection key-persistence design
  depends on Postgres being the running database (see F2 below).

**Why this exists now:** Sprint 5 (Artifact Export, see `TaskFlow_Epic3_ResumeBuilder.md`) needed to
decide how the `typst` binary gets deployed alongside `TaskFlow.Api`, and deliberately deferred that
decision because no real deploy target existed yet. This doc is that deploy target, decided.

---

## Goals, stated plainly

- A **standalone Windows executable** — publish a folder, double-click, the whole app runs. No
  `dotnet` runtime install, no separate frontend dev server.
- **Docker and Kubernetes**, built for learning value as much as practical deployment — no prior
  hands-on experience with either, and that experience is an explicit goal of building this out.
- Both tracks share one foundation and are not mutually exclusive — TaskFlow ends up distributable
  two ways: a local executable, and a containerized/orchestrated deployment.

**Honest scope note, revised 2026-08-11 after a full architecture discussion, stated here rather
than left implicit:** the original version of this doc treated SQLite as fixed and named "moving off
SQLite" as an out-of-scope future decision blocking any real horizontal scaling story. That
discussion happened — see Epic 4 for the full reasoning — and it concluded two things worth stating
plainly:

1. **TaskFlow is moving to PostgreSQL**, which removes the SQLite-specific blocker to running more
   than one replica.
2. **"Massive scale" was explicitly considered and explicitly declined as a goal**, not silently
   dropped. TaskFlow is single-tenant by design, built for personal/portfolio use — chasing a
   multi-replica-with-Redis-backplane-and-managed-database architecture for an app with no real
   traffic behind it is disproportionate engineering weight for one person to maintain, for a
   benefit this project doesn't actually need. What Postgres unlocks (and what remains
   deliberately unbuilt — a SignalR backplane, connection pooling, a managed database service) is
   recorded in full in Epic 4's "Scaling beyond one replica" section, specifically so this
   reasoning doesn't have to be re-derived if the question comes up again. **Kubernetes here
   remains one replica** — Postgres removes one blocker to scaling past that, but doesn't by itself
   make multi-replica correct (SignalR's connection-pinning problem is unrelated to the database
   and unresolved either way).
3. **Revisited directly, 2026-08-11: strictly, Postgres isn't technically required by this doc
   either.** A one-replica architecture runs fine on SQLite plus a persisted volume — that's what
   this doc originally described before Epic 4 existed. Epic 4 keeps the migration anyway,
   explicitly for the learning value (see Epic 4's own "Why this exists," revisited the same day),
   and every track below reflects that choice as a result: the exe requires a reachable connection
   string, Track B gets a `postgres` compose service, Track C gets a `StatefulSet` — real
   infrastructure, honestly optional rather than load-bearing, kept because building it is worth
   doing, not because this app needs it.

---

## Shared foundation (needed before either track)

Today `TaskFlow.Api` and `TaskFlow.Web` are two separate processes (the API, and Vite's dev server).
Neither a standalone exe nor a Docker image means much until the app is one deployable unit.

### F1 — Serve the built frontend from the API process

`npm run build` produces static files. `TaskFlow.Api` serves them via ASP.NET Core's static-file
middleware plus SPA fallback routing (unmatched non-API routes serve `index.html`, so client-side
routing keeps working), out of `wwwroot/`. One process, one port, one artifact, for every deploy
target from here on. Verification: `dotnet publish`, run the published output, hit `/` and confirm
the React app loads and can reach `/api/...` without a separate dev server running.

### F2 — Configurable connection string and binary paths

**Revised from the original version of this doc**, which described a configurable *SQLite file
path*. With the Postgres migration (Epic 4), this simplifies rather than complicates — one config
shape across all three tracks instead of SQLite-for-the-exe/Postgres-for-containers:

- A Postgres connection string, provided via `ConnectionStrings:Default` (or equivalent), resolved
  the same way — an environment variable — regardless of deploy target. No deploy-target-specific
  branching in code; this is the DRY point of doing it this way rather than keeping SQLite around
  for one track.
- `Export:TypstBinaryPath` (already designed in Sprint 5) still resolves per deploy target: a
  sibling `typst.exe` for the standalone exe, a fixed image path for Docker/Kubernetes. This part
  is unaffected by the database change.
- `HealthController` already exists (confirmed in the repo) — reused as-is for Kubernetes
  liveness/readiness probes in Track C. No new health-check code needed. A useful side effect of
  moving to a real client-server database: `HealthController` can now report actual DB
  connectivity, not just "the process is alive" — worth a small addition when this is built, not a
  new task on its own.

Verification: fresh publish/build in an empty directory with no dev-machine state, and no Postgres
reachable, confirms the app fails fast with a clear connection error rather than a confusing crash —
this is now the meaningful "first run" check, replacing the old SQLite-file-creation check.

---

## Track A — Standalone Windows executable

**Decision (2026-08-11): Windows only (`win-x64`) for now.** A Linux build was considered and set
aside — testing it would require a second environment (a Linux box or WSL) not in active use right
now, and doubling the publish/verify matrix for a v1 isn't worth it. Revisit as a named future item
once the Windows build is solid, not silently expanded later.

**Decision: self-contained, not trimmed.** `dotnet publish -r win-x64 --self-contained
-p:PublishSingleFile=true`, deliberately **without** `-p:PublishTrimmed`. EF Core relies on
reflection in ways that don't trim cleanly; the size savings aren't worth chasing trimming-induced
runtime bugs for this project.

**Revised decision (2026-08-11): the exe is no longer fully standalone with respect to the
database, and that's an accepted, deliberate tradeoff, not a gap.** Earlier revisions of this doc
had the exe carry its own embedded SQLite file, making it truly zero-dependency. That's no longer
true once Postgres is the one database engine everywhere (see the goals section above) — running
the exe now requires a reachable Postgres connection string, exactly like Track B/C. In practice
that's a `docker run postgres` one-liner (or pointing at an already-running Track B/C instance) —
a natively-run app talking to a containerized dependency, a normal and common pattern, not a
downgrade. This was a deliberate choice (see the conversation that produced Epic 4) over the
alternative of bundling a portable Postgres binary, which would have made the exe meaningfully
heavier and added a server process to manage in exchange for keeping a "truly zero dependency"
claim nobody asked to keep at that cost.

**What "standalone" still means here:** no `dotnet` runtime install, no separate frontend dev
server, no `npm`. The exe folder — `TaskFlow.Api.exe`, `typst.exe` alongside it (Typst is still a
sibling binary; "single file" bundles .NET assemblies, not arbitrary native binaries), `wwwroot/`
(from F1) — is still a complete, double-click-and-run application. It just talks to a database
outside the folder now, the same as every other deploy target.

**Secrets, unified with Track B/C rather than exe-specific:** the Postgres connection string and
the Anthropic API key are both provided via environment variables set before launching the exe.
`dotnet user-secrets` doesn't apply here — it's a dev-time, build-machine-tied mechanism, not
something a published exe can use — so environment variables are the one secret-handling
mechanism across all three tracks, not three different ones.

### Tasks

**TA.1 — Publish profile.** A `publish-exe` script (root-level, alongside `.\run`/`.\test`) running
the `dotnet publish` command above into a clean output folder. Verification: run it twice from a
clean checkout, confirm identical output structure both times.

**TA.2 — Bundle `typst.exe` into the publish output.** Copy the Windows Typst release binary into
the publish folder alongside `TaskFlow.Api.exe`, via an MSBuild `CopyToPublishDirectory` item
referencing a binary checked into the repo (e.g. `tools/typst/win-x64/typst.exe`) — same binary,
same version, every publish. Verification: a fresh publish folder, moved to a directory with no
`typst` on `PATH` at all, still runs export successfully.

**TA.3 — Clear failure when no database is reachable.** *(Revised from "first-run data
directory," which assumed SQLite.)* Verification: launch the exe with an intentionally-wrong or
absent connection string, confirm the app fails fast at startup with a readable error naming the
connection problem, not a stack trace or a silent hang.

**TA.4 — Smoke test.** From a clean publish folder, on a machine (or clean user profile) with no
`dotnet` SDK/runtime installed, and a real Postgres reachable at the configured connection string:
launch `TaskFlow.Api.exe`, load the app in a browser, create a task, confirm it persists across an
exe restart (the database, not the exe, is what's holding state now — worth confirming that
distinction explicitly rather than assuming restart-persistence the way SQLite's embedded file
made trivially true before).

### Definition of done

- A published folder runs the full app (frontend + API + typst export) with nothing else installed
  on the machine beyond a reachable Postgres instance.
- A missing/unreachable database fails fast and clearly at startup.
- Re-publishing from a clean checkout is repeatable and produces the same structure.

---

## Track B — Docker

**Decision: multi-stage Dockerfile.** Build stage (`mcr.microsoft.com/dotnet/sdk:10.0`) compiles
`TaskFlow.Api`, builds `TaskFlow.Web`'s frontend (`npm ci && npm run build`), and downloads the Linux
`typst` release binary. Runtime stage (`mcr.microsoft.com/dotnet/aspnet:10.0`, the smaller
runtime-only image) copies in the publish output, the built frontend (F1's `wwwroot/`), and the
`typst` binary — nothing else. This resolves Sprint 5's deferred packaging decision for this track:
`typst` is baked into the image at a fixed path, `Export:TypstBinaryPath` points there by default in
this image.

**Revised decision: a `postgres` service is now part of the compose stack** — the earlier version
of this doc assumed no separate database container was needed because SQLite was embedded. That
assumption is gone. Official `postgres` image, a named volume for its data directory
(`/var/lib/postgresql/data`), credentials via environment variables in the same `.env` file as the
Anthropic key.

**Decision, unchanged: secrets via environment variables, not `dotnet user-secrets`.**
`Anthropic__ApiKey` and the Postgres connection string (double-underscore binds to nested config
keys) are passed as environment variables at `docker run`/compose time — an `.env` file locally
(gitignored, matching how `appsettings.Secrets.json` is already gitignored), a real Kubernetes
`Secret` in Track C.

### Tasks

**TB.1 — Dockerfile.** Multi-stage build as above. Verification: `docker build` succeeds from a
clean checkout with no local `node_modules`/`bin`/`obj` state (i.e. respects the existing
`.gitignore` boundary — a new `.dockerignore` mirroring the current `.gitignore`'s exclusions is
part of this task).

**TB.2 — docker-compose with a Postgres service.** *(Revised — previously one service, no
database.)* Two services: `TaskFlow.Api` and `postgres`, a named volume for Postgres's data
directory, an `.env.example` documenting required variables (Anthropic key, Postgres credentials).
Verification: `docker compose up`, load the app, confirm data survives
`docker compose down && docker compose up` (volume persistence) but not
`docker compose down -v` (volumes removed on purpose, including the database's).

**TB.3 — Image smoke test.** `docker run` the built `TaskFlow.Api` image against the compose
Postgres service, confirm the app comes up, `/health` (via `HealthController`) responds, export
produces a PDF using the image's baked-in `typst`.

### Definition of done

- `docker build` produces a working image from a clean checkout.
- `docker compose up` runs the full app plus its own Postgres; data persists across container
  restarts via mounted volumes, not the images themselves.
- Export works inside the container using the image's own `typst` binary — no host dependency.

---

## Track C — Kubernetes (learning track, local cluster)

**Decision (2026-08-11): local cluster only, Docker Desktop's built-in Kubernetes.** Given no prior
K8s experience, this minimizes moving parts: no separate cluster-runtime CLI to learn
(`kind`/`minikube`), and images built via `docker build` are immediately visible to the cluster with
no explicit load step, unlike `kind` (`kind load docker-image`). `kind` is closer to how a lot of
real CI ephemeral-cluster setups work and has its own resume value, but that's a reasonable
next-step tool once the core K8s objects below are familiar, not the first thing to learn on. Real
cloud clusters (EKS/GKE/AKS) are explicitly a **future phase**, not part of this pass — no cost, no
cloud account, until the local objects below are solid.

**Manifests — revised to add a database object, previously absent because SQLite needed none:**

- `Deployment` (`TaskFlow.Api`) — 1 replica (see the goals section — this stays 1 regardless of the
  Postgres migration; the SignalR backplane problem is separate and unresolved), the image from
  Track B, resource requests/limits (small — this is a personal-scale app), liveness and readiness
  probes against `HealthController`'s existing endpoint.
- `StatefulSet` (`postgres`) — **new.** The idiomatic Kubernetes primitive for a stateful workload,
  even single-instance — stable pod identity, ordered PVC binding. This is genuinely new K8s
  learning surface the SQLite version of this doc never had a reason to include: running a real
  stateful database in Kubernetes (as opposed to a stateless app Deployment) is one of the more
  commonly-asked-about K8s skills, so this is a net addition to the learning goal, not just
  overhead from the migration.
- `Service` — `ClusterIP` for `TaskFlow.Api` (reachable via `kubectl port-forward` for local
  access), plus a headless/`ClusterIP` `Service` for the `postgres` `StatefulSet` so
  `TaskFlow.Api`'s pod can reach it by a stable DNS name. A `LoadBalancer`/`NodePort`/`Ingress` step
  for external access is a later, separate task once the Deployment/Service pair is confirmed
  working.
- `PersistentVolumeClaim` — now backs the `postgres` `StatefulSet`'s data directory, not a
  `TaskFlow.Api` data folder (that concept goes away with SQLite). Single-replica-only means no
  multi-writer conflict to worry about here.
- `ConfigMap` — non-secret config (e.g. `Export:TypstCompileTimeoutSeconds` if overridden).
- `Secret` — the Anthropic API key and Postgres credentials, mounted as environment variables into
  the `TaskFlow.Api` Deployment's pod spec, never committed to the manifest files themselves (a
  `secret.example.yaml` documents the shape; the real one is created imperatively via
  `kubectl create secret` or a local, gitignored file).

### Tasks

**TC.1 — Postgres `StatefulSet` + `Service` + `PersistentVolumeClaim`, applied first.** New task,
sequenced before the `TaskFlow.Api` Deployment since it needs a database to connect to.
Verification: `kubectl apply`, the pod reaches `Running`, `kubectl exec` into it and connect with
`psql` to confirm the database is actually up before wiring the app to it.

**TC.2 — `TaskFlow.Api` Deployment + Service, applied to a local cluster.** Infra doesn't have a
literal failing test, so a verification criterion stands in for RED/GREEN: before applying,
`kubectl get pods` shows nothing for `TaskFlow.Api`; after `kubectl apply`, the pod reaches
`Running`, `kubectl port-forward` reaches the app in a browser, and the app can actually read/write
through to the `postgres` `StatefulSet` from TC.1.

**TC.3 — PersistentVolumeClaim survives pod restarts.** *(Revised — previously tested the
`TaskFlow.Api` data folder; now tests the database.)* Verification: create a task, delete the
`postgres` pod (`kubectl delete pod`), confirm the `StatefulSet` recreates it and the task is still
there — proving the PVC, not the pod's ephemeral filesystem, is what's holding state.

**TC.4 — ConfigMap and Secret.** Verification: neither the Anthropic key nor the Postgres password
is ever present in any committed YAML (grep the manifests directory to confirm), and export/
tailoring still works end-to-end against the running pod.

**TC.5 — Health probes tuned.** Verification: intentionally break the app (e.g. stop the process
inside the `TaskFlow.Api` pod, or misconfigure a probe path) and confirm Kubernetes' own restart
behavior kicks in — this is the actual point of a readiness/liveness probe, and should be seen
happening, not assumed from the YAML alone.

### Definition of done

- A local Kubernetes cluster runs both the `TaskFlow.Api` Deployment and the `postgres`
  `StatefulSet`, with data surviving pod restarts via a PVC on the database side.
- No secret value is ever committed to a manifest file.
- Health probes are verified to actually trigger Kubernetes' restart behavior on failure, not just
  present in config.

### Explicitly out of scope for this pass

Revised 2026-08-11 — these were previously blocked on "moving off SQLite" as one undifferentiated
item. That migration (Epic 4) is now underway, which makes it possible to be specific about what's
*still* not being pursued and why, rather than leaving one vague blocker:

- **Running more than one `TaskFlow.Api` replica.** Postgres removes the database-side blocker, but
  SignalR's connection-pinning problem is separate and unresolved — a broadcast from one replica
  never reaches a client connected to another without a backplane (Redis, standardly). Not
  pursued: no real traffic requires it, and it's real ongoing infrastructure to maintain solo. Full
  reasoning in Epic 4's "Scaling beyond one replica" section.
- **Connection pooling (PgBouncer)** and **a managed database service** in place of the
  self-hosted `StatefulSet` — both are what a real multi-replica deployment would need; neither is
  needed at one replica. Named as future phases in Epic 4, not decided now.
- A real cloud cluster (EKS/GKE/AKS) — named as a future phase once the local-cluster objects above
  are solid, not decided now.
- Ingress/TLS — later, once ClusterIP + port-forward is confirmed working end to end.
- `kind`/`minikube` as an alternative local runtime — Docker Desktop's Kubernetes is the starting
  choice; revisit only if a concrete reason comes up (e.g. wanting closer-to-CI tooling).

---

## Open decisions log

1. **Linux build for Track A** — deferred, not decided. Revisit once win-x64 is solid and there's an
   actual environment to test it in.
2. **Real cloud Kubernetes cluster** — deferred as a named future phase, not decided. Revisit once
   the local-cluster track (Track C) is solid.
3. **Database engine** — ~~explicitly out of scope, not silently assumed either way.~~ **Settled
   2026-08-11: PostgreSQL**, see Epic 4. That doc also settles the deeper question this item used to
   gesture at — whether and how far to pursue horizontal scaling — with a full technical analysis
   (SignalR backplane, connection pooling, managed databases) concluding **not pursued for now**,
   deliberately, not by omission.

---

## Sequencing

F1/F2 (shared foundation) → Epic 4 (Postgres migration) should land before either track is built out
further, since both now depend on it → Track A (exe) and Track B (Docker) can then proceed in
either order or in parallel, since they don't depend on each other → Track C (Kubernetes) depends
on Track B's image being solid first; it cannot start before Track B is done. Epic 6 (per-user API
keys) is independent of all three tracks functionally, but its Data Protection key-persistence
design assumes Postgres is already the running database — sequence it after Epic 4 too, for the
same reason.
