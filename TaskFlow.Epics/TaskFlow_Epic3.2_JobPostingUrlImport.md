# TaskFlow — Epic 3.2: Job Posting URL Import

**Epic map:** sequenced after Epic 3.1's own explicit descope decision. Epic 3.1's "Decisions owned
here" section states verbatim: *"The job-posting 'Parse from a URL' flow is explicitly out of scope
for this epic. It requires new backend surface (server-side fetch of an arbitrary user-supplied URL)
with a real SSRF attack surface that has not been designed, let alone reviewed... If URL-based
parsing is wanted later, it gets its own epic with its own security design — recorded in the open
decisions log below, not silently dropped."* This is that epic. Triggered directly by a user report
(2026-08-14): pasting a job-posting URL into the existing paste-text field fails with "Claude
response did not include a JSON object" — expected given the current scope, but the user wants URL
import built now, not deferred further.

**Cross-epic touch point — resolved (2026-08-20 architect review):** at the time this epic was
written, Epic 3.1 Sprint 4 (Ingest & Hand-off) had not yet run, and this doc originally assumed
Epic 3.2 would build against the *un-restyled* `IngestDocument.tsx`. That assumption is now stale:
**Epic 3.1 Sprint 4 has shipped and merged to `main`** (Nocturne restyle, close-out items complete).
`IngestDocument.tsx` today already uses the token constants from `TaskFlow.Web/src/lib/tokens.ts`
(`bgSurface`, `borderDivider`, `focusRingAccent`, `textNeutral400/500`), the shared `Button`
component, and a local `errorBannerClasses` constant for every `role="alert"` error banner. **Sprint
2 of this epic (below) has been rewritten to build against that current, restyled component** — it
does not touch, revert, or bypass any of Epic 3.1's styling work. This resolves the reconciliation
risk the original note flagged; no further action needed on it.

---

## Standing rules

Inherited unmodified from `CLAUDE.md` and `TaskFlow_Epic3.1_UIRevamp.md`'s "Standing rules" section:
strict TDD (RED confirmed failing before GREEN), clean code/SOLID/DRY, nothing marked done without an
actual `.\test` run read from `test-results.txt`, git/tooling boundary (Claude writes, the user runs
`git`/`dotnet`/`npm`, except `.\test` which Claude may always run itself).

**One rule specific to this epic, stated once and binding throughout:** every task that touches the
URL-fetch path gets a negative/attack-vector test proving the specific SSRF mitigation it's meant to
provide actually rejects the attack it exists to stop — not just a happy-path test that a well-formed
public URL works. A mitigation without a test proving it rejects the thing it's supposed to reject is
not done.

**Also inherited: the user's standing AI-Native coding standard** (full pillars in the user's core
memory, not restated in full here — see "AI-Native Pillars Applied to This Epic" below for the
epic-specific mapping). Short form: SOLID + DRY architecture, Small Context Units, Extreme Explicit
Typing, High Semantic Clarity naming, and current-generation language syntax only. This governs
every file this epic touches, C# and TypeScript alike.

## The problem, and why it's a real security decision, not just a missing feature

Claude cannot browse to a URL — it can only read text it's given. Today, pasting a URL into the
"Job posting" textarea sends the literal URL *string* to Claude as if it were posting text, which
predictably fails to produce parseable JSON. The feature the user wants — "give it a URL, fetch the
page server-side, extract the posting text, keep going exactly as today" — requires the TaskFlow API
itself to make an outbound HTTP request to a URL the user controls. That is textbook **Server-Side
Request Forgery (SSRF)** surface: without safeguards, a malicious or compromised client could supply
a URL pointing at:
- Cloud metadata endpoints (`http://169.254.169.254/...` — AWS/Azure/GCP instance credentials)
- Internal/private network services (`http://10.x/`, `http://192.168.x/`, `http://172.16-31.x/`)
- The API's own loopback interface (`http://localhost:PORT/admin-only-route`)
- A URL that DNS-resolves to a safe IP at validation time but a different (internal) IP at actual
  connection time ("DNS rebinding" — the classic bypass for naive "resolve-then-check" designs)

None of this is hypothetical or exotic; it's the standard threat model for any "fetch this URL for
me" server feature, and it's exactly why Epic 3.1 explicitly declined to build it without a real
design. This doc **is** that design.

## Confirmed against the repo

| Claim | Status |
|---|---|
| No HTML-parsing library exists anywhere in `TaskFlow.Api.csproj`'s dependencies. | **Confirmed 2026-08-14**, file read directly. **Re-confirmed 2026-08-20** — still true; `csproj` lists `Anthropic.SDK`, `BCrypt.Net-Next`, `Markdig`, JWT/EF/SQLite/Swashbuckle only. |
| No outbound-HTTP-fetch capability (`HttpClient`/`IHttpClientFactory` for fetching arbitrary external URLs) exists anywhere in `TaskFlow.Api` — the only external HTTP caller is `Anthropic.SDK`, a fixed, trusted destination. | **Confirmed 2026-08-14**, repo-wide search. **Re-confirmed 2026-08-20** — `Program.cs` still has zero `AddHttpClient`/`HttpClient` references. |
| `IJobPostingIngestionParser.ParseAsync(string documentText, ...)` is the exact same signature the existing `/api/JobApplications/parse` endpoint already calls, and internally just delegates to `TieredIngestionParser` (free rule-based tier, escalating to Claude). | **Confirmed**, `JobPostingIngestionParser.cs` read directly. This is the reuse point: once a URL is turned into plain text, it is handed to this exact same method, unchanged — the entire Sprint 3 (Epic 3.1) `Company` plumbing, the free/paid tiering, everything downstream, is inherited for free. |
| `JobApplicationsController.Parse` (`POST /api/JobApplications/parse`) takes `IngestDocumentDto { Content }` and calls `_parser.ParseAsync(dto.Content)`. | **Confirmed**, file read directly (`JobApplicationsController.cs:39-40`). A new, separate endpoint is added rather than overloading this one (see "Backend Architecture"). |
| `Program.cs` has no `AddHttpClient(...)` registration of any kind today. | **Confirmed.** A new named/typed `HttpClient` registration is added for exactly this feature, configured with the SSRF mitigations below — not the default client, and not reused for anything else. |
| `IngestDocument.tsx` is now the Nocturne-restyled version (Epic 3.1 Sprint 4, shipped). | **Confirmed 2026-08-20**, file read directly. Real tokens in play: `bgSurface`, `borderDivider`, `focusRingAccent`, `textNeutral400`, `textNeutral500` from `lib/tokens.ts`; the shared `Button` component (`variant="primary"`); a local `errorBannerClasses` constant for every `role="alert"` banner. Sprint 2 below targets this file as it actually exists, not the pre-restyle version the original draft of this epic assumed. |

---

## AI-Native Pillars Applied to This Epic

The user's standing coding standard (SOLID/DRY + AI-native optimization, held in core memory, not
TaskFlow-specific) maps onto this epic's own design choices as follows. This section exists so
"follow the pillars" isn't an abstract instruction floating outside the plan — every pillar below
points at a concrete decision already locked in this doc, and every sprint's Definition of Done is
checked against it before being called done.

| Pillar | How this epic satisfies it |
|---|---|
| **SRP** | `UrlValidation` (pure scheme/port/hostname/IP validation) is a separate static class from `JobPostingUrlFetcher` (owns the HTTP call, redirects, size/timeout enforcement) is a separate class from the HTML-to-text extraction step — three reasons to change, three units. |
| **ISP** | `IJobPostingUrlFetcher` exposes exactly one method, `FetchAsync(Uri) -> Result<string>`. No bloated multi-purpose "ingestion service" interface. |
| **DIP** | `JobApplicationsController` depends on `IJobPostingUrlFetcher`, injected via DI — never `new`s the concrete fetcher or `HttpClient` directly. |
| **OCP** | Redirect-hop validation reuses the exact same `UrlValidation` entry point as the first hop — no special-cased "redirect mode" branch bolted onto the validator. |
| **DRY** | `IJobPostingIngestionParser.ParseAsync` is reused completely unchanged for both `/parse` and `/parse-url` — no duplicated parsing/tiering logic between the two entry points. |
| **Small Context Units** | `UrlValidation.cs` and `JobPostingUrlFetcher.cs` are each expected to stay well under ~250 lines given their single responsibility; if either grows past that during implementation, that is itself a signal to split further, not a target to write toward. |
| **Extreme Explicit Typing** | `Result<string>`, `Uri`, `IPAddress` throughout the backend — no `object`/`dynamic`. Frontend: `Promise<TaskDraft[]>` return types, no `any`, matching every existing function in `api/jobApplications.ts`. |
| **High Semantic Clarity** | Names read as self-contained explanations: `IJobPostingUrlFetcher`, `UrlValidation`, `ParseUrlDto`, `parseJobPostingUrl` — no `Helper`/`Manager`/`Util` catch-alls. |
| **Cutting-Edge Language Sync** | The DNS-rebinding mitigation uses `SocketsHttpHandler.ConnectCallback` — the current .NET-native mechanism for this exact problem, not a hand-rolled pre-check-then-connect workaround or a third-party SSRF-guard package. |
| **Production-ready output** | No `TODO`/placeholder code accepted at GREEN for any task below — a task isn't done until its RED test is green for real. |

---

## Decisions owned here, before dispatching any engineer (2026-08-14)

### Security design (the core of this epic)

Defense in depth — every layer below is required, none is a substitute for another:

1. **Scheme allowlist.** Only `http://` and `https://` are accepted. Reject `file://`, `ftp://`,
   `gopher://`, `data:`, everything else, before any network activity.
2. **No embedded credentials.** Reject a URL whose `Uri.UserInfo` is non-empty
   (`http://user:pass@host/...`) — no legitimate job-posting link needs this, and it's a known
   SSRF/credential-leak vector.
3. **Port allowlist.** Only the scheme's default port (80 for http, 443 for https) or no explicit
   port is accepted. Reject `http://host:8080/...`, `http://host:6379/...` (Redis), etc. — arbitrary
   ports are exactly how SSRF is used to probe/attack internal services.
4. **Hostname denylist (defense in depth, checked before DNS).** Reject `localhost`, any hostname
   ending in `.local`/`.internal`/`.test`/`.localhost`, and any single-label hostname (no `.` at
   all — real public job-posting domains always have one). This catches obvious cases even before
   DNS resolution.
5. **IP-literal denylist.** If the host is already a literal IP address (not a name to resolve),
   reject it outright if it falls in any of: loopback (`127.0.0.0/8`, `::1`), link-local
   (`169.254.0.0/16` — **this is the cloud metadata range**, `fe80::/10`), private/RFC1918
   (`10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`), unique-local IPv6 (`fc00::/7`), unspecified
   (`0.0.0.0`, `::`), and multicast/reserved ranges. A user has no legitimate reason to paste a raw
   IP as a "job posting URL" at all — this mostly exists to reject the attack pattern directly, not
   to serve a real use case.
6. **DNS-rebinding-safe connection: validate the IP at actual socket-connect time, not just at a
   pre-check DNS lookup.** A naive "resolve the hostname, check the IP, then let `HttpClient` do its
   own separate resolve-and-connect" has a TOCTOU gap: the DNS record can change between the check and
   the real connection (or the attacker's DNS server can return different answers to different
   resolvers/queries). The mitigation: a custom `SocketsHttpHandler.ConnectCallback` that performs the
   DNS resolution *itself*, validates every resolved IP against the same denylist as (5) above, and
   only then opens the socket to that exact validated IP — so the IP that gets validated is
   *guaranteed* to be the IP that gets connected to, with no gap an attacker can race.
7. **Redirects: capped and re-validated at every hop.** `AllowAutoRedirect` on the handler is
   disabled; redirects are followed manually (loop, max 3 hops), and **every** redirect target goes
   back through the full scheme/port/hostname/IP validation in (1)–(6) before being followed — a
   validated safe URL that 302s to an internal address must be rejected at the redirect, not just at
   the first hop.
8. **Response size cap.** Abort the read once the response body exceeds 5 MB (a job posting page is
   never legitimately larger than this; this bounds memory use and rules out being used as a
   bandwidth/resource-exhaustion vector).
9. **Timeout.** 10 seconds total (connect + read). No legitimate job-posting page takes longer; this
   bounds how long a request thread can be tied up.
10. **Content-Type check.** Only proceed with extraction if the response's `Content-Type` starts with
    `text/html` or `text/plain`. Reject anything else (binaries, JSON APIs accidentally targeted,
    etc.) before attempting to parse it as HTML.
11. **A real, identifying `User-Agent`** (e.g. `TaskFlow-JobPostingFetcher/1.0`) — not spoofing a
    browser. Courtesy to the sites being fetched, not itself a security control.

None of this is configurable per-request by the client — every rule is enforced server-side,
unconditionally, regardless of what the frontend sends.

### Backend Architecture

- **New endpoint, not an overload of the existing one:** `POST /api/JobApplications/parse-url`,
  accepting `{ url: string }`, returning the exact same `TaskDraft[]` shape `/parse` already returns.
  Kept separate (not a second optional field on the existing `IngestDocumentDto`) so the
  security-sensitive fetch code path is a narrow, single-purpose, easily-auditable surface — not
  blended into a general-purpose endpoint's branching logic.
- **New `IJobPostingUrlFetcher` service** (`FetchAsync(Uri url) -> Result<string>`, the `string`
  being extracted plain text) owns every mitigation in the security design above. It is the *only*
  place in the codebase that performs this kind of fetch — no other service gets its own copy of this
  logic.
- **HTML-to-text extraction via `HtmlAgilityPack`** (new NuGet dependency — a mature, MIT-licensed,
  widely-used .NET HTML parser; chosen over a hand-rolled regex tag-stripper because regex-based HTML
  parsing is well-documented to be fragile and wrong at the edges, and this is exactly the kind of
  "don't hand-roll what a real library already solves correctly" case). Strips `<script>`, `<style>`,
  `<nav>`, `<header>`, `<footer>` (common non-posting boilerplate), extracts remaining visible text,
  collapses whitespace.
- **Reuses `IJobPostingIngestionParser.ParseAsync(text)` completely unchanged** — the controller
  action calls `_urlFetcher.FetchAsync(url)`, and on success, feeds the resulting text into the exact
  same call `Parse` already makes. Zero changes to `TieredIngestionParser`, `JobPostingParser`,
  `ClaudeJobPostingParser`, or anything downstream (Company extraction, DTO plumbing — all of Epic
  3.1 Sprint 3's work is inherited for free, not reimplemented).

### Frontend Architecture

*Rewritten 2026-08-20 to target `IngestDocument.tsx` as it actually exists today (post Epic 3.1
Sprint 4 restyle), not the pre-restyle version the original draft assumed — see "Confirmed against
the repo" above.*

- **A URL input row is added inside the existing `jobPostingEditable` branch of `IngestDocument.tsx`**
  (`IngestDocument.tsx:124-156`), as a sibling of the current textarea + file-upload row — not a new
  section, not a separate stage. This is the same branch that already renders when
  `intake.stage === 'provide' || 'parsing'`, so the URL row appears and disappears in lockstep with
  the rest of the job-posting input, with no new conditional to maintain.
- **Reuses the file's existing conventions exactly, per the design tokens already in place:** the URL
  `<input>` styled consistently with the existing `textareaClasses` pattern (`bgSurface`,
  `border-white/10`, `focusRingAccent`); the "Parse posting" trigger is the shared `Button` component
  (`variant="primary"`), matching the existing "Parse posting" button for the textarea; a fetch/parse
  failure renders through the same local `errorBannerClasses` constant and `role="alert"` pattern
  every other error in this file already uses. No new visual language is introduced — this task is
  additive wiring against Epic 3.1's restyle, not a second restyle pass.
- **A "Parse posting" action on the URL field calls the new `/parse-url` endpoint**; success populates
  `intake.drafts` exactly as the text-paste flow already does (same downstream stage-machine, same
  review UI) — no new frontend state machine. This plugs into `useIntakeFlow`'s existing
  `parse`-equivalent flow as a sibling entry point, not a parallel one, per the original design
  handoff's own reference copy ("Paste a job posting URL — or type/paste the description").

---

## Roadmap

| Sprint | What | Status |
|---|---|---|
| **1** | Secure URL fetch + HTML extraction (backend) | **Shipped (2026-08-22)** — [PR #63](https://github.com/AndrewVanDelden/TaskFlow/pull/63) merged to `develop` (`967a76b`). S1.1-S1.6 all RED-confirmed then GREEN-confirmed; 6 post-review findings (most severe: an IPv4-mapped IPv6 literal bypassing the SSRF denylist in both defense layers) fixed and re-verified before merge. 510/510 backend, 340/340 frontend. |
| **2** | Frontend URL input | **Complete (2026-08-22)** — S2.1-S2.3 all RED-confirmed then GREEN-confirmed on `feature/epic3.2-sprint-2-frontend-url-input`. 510/510 backend (unaffected), 348/348 frontend. Not yet reviewed/merged. |

## Definition of Done (Epic 3.2)

- A user can paste a job-posting URL into Ingest and get the same parsed-result experience as pasting
  text today (same `TaskDraft`s, same Company extraction, same downstream flow). **Met** — a "Job
  posting URL" input + "Parse URL" button sit alongside the existing textarea, both converging on
  the same `useIntakeFlow` stage machine and parsed-result summary card.
- Every SSRF mitigation in "Decisions owned here" is implemented and has a passing negative test
  proving it actually rejects the specific attack it exists to stop. **Met** — see Sprint 1's DoD.
- The existing paste-text `/parse` flow and its full test coverage are completely unaffected. **Met**
  — verified at every step across both sprints; zero pre-existing tests changed behavior.
- Full suite green via `.\test` (backend + frontend, with coverage) before `develop → main`. **Met**
  for both sprints individually (Sprint 1: 510/510 + 340/340 before merge; Sprint 2: 510/510 + 348/348
  on its own branch). Epic-level close-out (both sprints merged to `develop`) still pending.

---

## Sprint 1 — Secure URL Fetch + HTML Extraction (backend)

### Why this sprint exists, and why it's first

This is the epic's entire security surface, concentrated in one new, narrow service. It must be
correct and independently verified — a fresh `.\test` run, all negative/attack-vector tests
confirmed passing — before any frontend code exists to call it, exactly mirroring Epic 3.1 Sprint 3's
"domain/backend first" sequencing discipline for the same underlying reason: get the
security/correctness-critical layer right in isolation before building anything on top of it.

### Files involved

- `TaskFlow.Api/TaskFlow.Api.csproj` (edit — add `HtmlAgilityPack`)
- `TaskFlow.Api/Ingestion/IJobPostingUrlFetcher.cs` (new)
- `TaskFlow.Api/Ingestion/JobPostingUrlFetcher.cs` (new — owns every mitigation)
- `TaskFlow.Api/Ingestion/UrlValidation.cs` (new — the scheme/port/hostname/IP validation logic,
  extracted as its own pure, easily-unit-testable static class rather than buried inline in the
  fetcher, since it's the part that most needs exhaustive, isolated negative-case coverage)
- `TaskFlow.Api/DTOs/ParseUrlDto.cs` (new — `{ Url: string }` request shape)
- `TaskFlow.Api/Controllers/JobApplicationsController.cs` (edit — new `POST parse-url` action)
- `TaskFlow.Api/Program.cs` (edit — register the new `HttpClient` with the SSRF-safe
  `SocketsHttpHandler.ConnectCallback`, and the new service)
- `TaskFlow.Tests/Ingestion/UrlValidationTests.cs` (new — exhaustive positive/negative cases)
- `TaskFlow.Tests/Ingestion/JobPostingUrlFetcherTests.cs` (new)
- `TaskFlow.Tests/Controllers/JobApplicationsControllerTests.cs` (edit — new endpoint's own tests)

### Tasks

**S1.1 — `UrlValidation` (the pure validation logic, mitigations 1–5 from the security design).**
RED: a table of cases — `https://example.com/job` (accept), `http://example.com/job` (accept),
`ftp://example.com` (reject, bad scheme), `file:///etc/passwd` (reject), `http://user:pass@example.com`
(reject, credentials), `http://example.com:8080` (reject, non-default port), `http://localhost`
(reject), `http://169.254.169.254` (reject — cloud metadata), `http://127.0.0.1` (reject, loopback),
`http://10.0.0.5` (reject, private), `http://192.168.1.1` (reject, private), `http://172.20.0.1`
(reject, private), `http://[::1]` (reject, loopback v6), `http://internal-tool.local` (reject),
`http://intranet` (reject, single-label) — each asserted individually, named after exactly what it
proves. GREEN: the validation function.

**S1.2 — `JobPostingUrlFetcher`'s connect-time IP validation (mitigation 6, the DNS-rebinding
defense).** RED: a test proving that when a hostname resolves to a denylisted IP, the fetch is
rejected *at the connection layer*, not just via a pre-check — construct this so it genuinely
exercises the `ConnectCallback` path (e.g. a test double/fake resolver returning a private IP for a
hostname that would otherwise pass the string-based hostname check), not just re-testing S1.1's logic
under a different name. GREEN: the `SocketsHttpHandler.ConnectCallback` wiring in the fetcher/DI
registration.

**S1.3 — Redirect handling (mitigation 7).** RED: a test using a local test server or mocked handler
proving that a redirect chain longer than 3 hops is rejected, and a test proving a redirect *to* a
denylisted target (e.g. a public-looking URL that 302s to `http://169.254.169.254/`) is rejected, not
silently followed. GREEN: manual redirect loop with per-hop re-validation.

**S1.4 — Size cap, timeout, Content-Type check (mitigations 8–10).** RED: three tests — an
over-size response is aborted before being fully buffered/returned; a slow/hanging response is
aborted after the timeout; a non-HTML/text `Content-Type` (e.g. `application/octet-stream`) is
rejected before extraction is attempted. GREEN: the corresponding handler/response-reading logic.

**S1.5 — HTML-to-text extraction.** RED: given representative HTML (a real page structure with
`<nav>`, `<script>`, `<style>`, and a body containing an H1 title, a company-ish heading, and
paragraph text), the extracted text contains the meaningful content and excludes script/style/nav
boilerplate. GREEN: `HtmlAgilityPack`-based extraction.

**S1.6 — Wire it together: `POST /api/JobApplications/parse-url`.** RED: an integration test (styled
like the existing `JobApplicationsIntegrationTests.cs`) proving a valid URL end-to-end produces the
same `TaskDraft[]` shape `/parse` would for equivalent text content (mock the `IJobPostingUrlFetcher`
or the outbound `HttpClient` at the test boundary — do not make a real network call in the test
suite); and a test proving an invalid/rejected URL returns a clear 400-level error, not a 500 or a
silent empty result. GREEN: the controller action, calling `_urlFetcher.FetchAsync` then the existing
`_parser.ParseAsync` unchanged.

### Definition of Done (expected completion)

- Every mitigation in the security design has its own passing negative test.
- `POST /api/JobApplications/parse-url` returns the same `TaskDraft[]` shape as `/parse`, reusing the
  existing tiered parser completely unchanged.
- The existing `/parse` endpoint and its tests are untouched.
- Satisfies "AI-Native Pillars Applied to This Epic" above: `UrlValidation`/`JobPostingUrlFetcher`
  stay within Small Context Unit size, explicit types throughout (`Uri`, `IPAddress`, `Result<string>`
  — no `object`/`dynamic`), no placeholder code at GREEN.

### Prerequisites and what this unblocks

- Depends on: nothing — foundational for this epic.
- Unblocks: Sprint 2 (frontend), which does not start until this sprint's tests are independently
  green via a fresh `.\test` run.

### Code review findings (fill in after this sprint's PR is reviewed)

1. **`TaskFlow.Api/Ingestion/HtmlTextExtractor.cs:13`**
   - **Why:** `RegexOptions.Compiled` is an outdated pattern (violating Pillar 2 / Syntax Recency).
   - **Fix:** Use .NET 7+ source-generated regex `[GeneratedRegex(@"\s+")]` for better performance.
   - **RED test:** Existing `HtmlTextExtractorTests` should remain green. (No new test needed, just syntax modernization).
   - **Status:** Fixed (2026-08-20). `HtmlTextExtractor` is now `static partial class` with a
     `[GeneratedRegex(@"\s+")]` partial method. All 7 existing tests green, unchanged behavior.

2. **`TaskFlow.Api/Ingestion/JobPostingUrlFetcher.cs:48`**
   - **Why:** `GetAsync` without `HttpCompletionOption.ResponseHeadersRead` buffers the entire response body in memory before returning, which completely defeats mitigation 8 (bounded stream read). A malicious server could return a massive response and OOM the server before `ReadBoundedAsync` ever checks `_maxResponseBytes`. (Correctness).
   - **Fix:** Pass `HttpCompletionOption.ResponseHeadersRead` to `GetAsync` to stream it safely.
   - **RED test:** Add a unit test that verifies `GetAsync` doesn't hang or buffer indefinitely, or rely on existing tests but ensure the flag is passed.
   - **Status:** Fixed (2026-08-20). `GetAsync` now passes `HttpCompletionOption.ResponseHeadersRead`.
     Honest caveat, recorded rather than glossed over: this specific real-network-buffering behavior
     isn't observable through `FakeHttpMessageHandler` (a fake in-memory transport has no
     distinction between the two completion options), so there is no dedicated automated regression
     test for it — the fix is verified by code inspection and the existing suite staying green
     (510/510), not by a test that would fail without it.

**Independent manual review (2026-08-20) — PR #63.** Posted directly to the PR as inline comments
— see
[review #4985511963](https://github.com/AndrewVanDelden/TaskFlow/pull/63#pullrequestreview-4985511963)
for the full text. Cross-checked against findings 1–2 above: independently traced and confirm
finding 2 (`GetAsync` buffering) is accurate; finding 1 (`RegexOptions.Compiled`) is reasonable but
overstated as "outdated" — not deprecated, just less optimal than a source-generated regex, low
priority either way. Two further findings, one more severe than either above:

3. **`TaskFlow.Api/Ingestion/UrlValidation.cs:61`** (CONFIRMED)
   - **Why:** `IsDenylistedIpAddress` branches on `AddressFamily` before checking IPv4 ranges, so an
     IPv4-mapped IPv6 literal (`http://[::ffff:169.254.169.254]/`) is never checked against
     `DenylistedIpv4Ranges` at all — it parses as `AddressFamily.InterNetworkV6`, takes the IPv6
     branch (`fe80::/10`, `fc00::/7`), and neither range contains it. This function backs **both**
     defense layers — `UrlValidation.Validate` and `SsrfSafeConnectCallback.ConnectAsync` both call
     it — so the bypass defeats the string-based check and the connect-time DNS-rebinding check
     simultaneously. No remaining backstop for this vector.
   - **Fix:** Unwrap via `if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();` at the top
     of `IsDenylistedIpAddress`, before the family-based range check.
   - **RED test:** A `UrlValidationTests` case asserting `http://[::ffff:169.254.169.254]` is
     rejected (currently accepted), plus an equivalent case in `SsrfSafeConnectCallbackTests` for
     the connect-time path.
   - **Status:** Fixed (2026-08-20), exactly as suggested. Added `Ipv4_mapped_ipv6_cloud_metadata_address_is_rejected`
     and `Ipv4_mapped_ipv6_private_rfc1918_address_is_rejected` to `UrlValidationTests.cs`, and
     `Connect_is_rejected_when_resolved_address_is_an_ipv4_mapped_ipv6_cloud_metadata_address` to
     `SsrfSafeConnectCallbackTests.cs` — all 3 confirmed RED (currently-accepted bypass) before the
     `IsIPv4MappedToIPv6`/`MapToIPv4()` unwrap, GREEN after.
4. **`TaskFlow.Api/Ingestion/SsrfSafeConnectCallback.cs:23`** (PLAUSIBLE)
   - **Why:** DNS resolution failure (NXDOMAIN, transient error) throws from inside
     `ConnectAsync`, and `JobPostingUrlFetcher.FetchAsync`'s `try/catch` only catches
     `OperationCanceledException` — so this (and the `HttpRequestException` thrown a few lines
     below when every resolved address is denylisted) propagates unhandled instead of becoming the
     clean `Result<string>.Invalid(...)` every other rejection path in this fetcher returns.
     Robustness/UX gap, not a security hole.
   - **Fix:** Add a `catch (HttpRequestException)` alongside the existing timeout catch in
     `JobPostingUrlFetcher.FetchAsync`.
   - **RED test:** A `JobPostingUrlFetcherTests` case where the fake resolver throws, asserting the
     fetcher returns `Result<string>.Invalid(...)` rather than letting the exception propagate.
    - **Status:** Fixed (2026-08-20). Fixed in `JobPostingUrlFetcher.FetchAsync` (this file's own
      throw was correct — the gap was the caller not catching it), via
      `catch (HttpRequestException ex)` alongside the existing timeout catch. RED-first test:
      `Underlying_transport_failure_is_returned_as_an_invalid_result_not_an_unhandled_exception`.

**Antigravity (Claude Opus 4.6) review pass (2026-08-22) — PR #63.** Posted directly to the PR as
inline comments — see
[review #5001003140](https://github.com/AndrewVanDelden/TaskFlow/pull/63#pullrequestreview-5001003140).
Cross-checked against findings 1–4 above: independently corroborates findings 1 and 2; findings 3
and 4 (from the earlier unsigned session) are also verified as accurate. Two additional findings:

5. **`TaskFlow.Api/DTOs/ParseUrlDto.cs:5`** (CONFIRMED)
   - **Why:** Every other new concrete class in this PR (`DnsResolver`, `SsrfSafeConnectCallback`,
     `JobPostingUrlFetcher`, `FakeHttpMessageHandler`, `FakeJobPostingUrlFetcher`) is `sealed`. This
     DTO is the sole exception, breaking the PR's own convention and leaving the class open for
     unintended inheritance.
   - **Fix:** `public sealed class ParseUrlDto`.
   - **RED test:** Existing tests should remain green — purely a modifier addition.
   - **Status:** Fixed (2026-08-20).
6. **`TaskFlow.Api/Ingestion/HtmlTextExtractor.cs:24`** (PLAUSIBLE)
   - **Why:** `node.Remove()` is called while iterating the `HtmlNodeCollection` returned by
     `SelectNodes`. This collection is backed by the live document tree; mutating it mid-iteration
     risks skipping nodes or an `InvalidOperationException` depending on HtmlAgilityPack version.
   - **Fix:** Copy to a list first: `foreach (HtmlNode node in boilerplateNodes.ToList())`.
   - **RED test:** A test with nested boilerplate (e.g., `<nav>` inside `<header>`) that would expose
     a skipped-node bug if the iteration is order-sensitive.
   - **Status:** Fixed (2026-08-20) as a precaution — applied the suggested `.ToList()` directly. Not
     confirmed as a reproducible bug: `HtmlAgilityPack.SelectNodes`'s result appears to be a snapshot
     list, not a live view tied to the document tree, from reasoning through its behavior, so no RED
     test could be made to actually fail first. Recorded as an inference, not a fact, per this
     project's own standing rule — the fix costs nothing and removes the doubt either way.

### Post-sprint retrospective (fill in once this sprint ships)

*(Not yet started — nothing to record.)*

---

## Sprint 2 — Frontend URL Input

### Why this sprint exists, and why it's second

Depends entirely on Sprint 1's endpoint existing and being verified safe. No frontend code is written
before that.

### Files involved

- `TaskFlow.Web/src/api/jobApplications.ts` (edit — new `parseJobPostingUrl(url)` function)
- `TaskFlow.Web/src/hooks/useIntakeFlow.ts` (edit — a URL-parse entry point alongside the existing
  text-parse `parse()`, both converging on the same `drafts`/`stage` state)
- `TaskFlow.Web/src/features/IngestDocument.tsx` (edit — URL input row alongside the existing
  textarea)

### Decisions owned here

- The URL input and the paste-text textarea are two ways to reach the same `review` stage — not two
  separate flows. Whichever succeeds populates the same `drafts` state `useIntakeFlow` already
  manages; everything after "parsed" (review, start tailoring, Company, hand-off) is completely
  unchanged and shared.
- Loading/error states for the URL path mirror the existing text-parse path's conventions exactly
  (same `stage` transitions, same `role="alert"` error rendering via the existing local
  `errorBannerClasses` constant) — no new UI pattern invented.
- **The URL row lands inside the existing `jobPostingEditable` branch** (`IngestDocument.tsx:124-156`,
  see "Frontend Architecture" above), styled with the file's current tokens (`bgSurface`,
  `focusRingAccent`) and the shared `Button` component — not a new section, and not styled against
  the pre-restyle version this epic originally assumed.
- URL input value is component-local state (e.g. `const [jobPostingUrl, setJobPostingUrl] =
  useState('')`), matching how `genericText`/`genericSourceName` are already handled locally in this
  same file for the generic-document flow below it — `useIntakeFlow` does not need to own the raw URL
  string, only the result of parsing it.

### Tasks

**S2.1 — `parseJobPostingUrl` API function + `useIntakeFlow` URL entry point.** RED: calling the new
hook entry point posts to `/api/JobApplications/parse-url` with the given URL and, on success,
transitions to the `review` stage with the returned drafts, exactly like the existing `parse()`
does for pasted text. GREEN: the API function (`parseJobPostingUrl(url: string): Promise<TaskDraft[]>`
in `api/jobApplications.ts`, matching the explicit-return-type convention every other function in
that file already follows) and hook wiring.

**S2.2 — URL input row in `IngestDocument.tsx`.** RED: a URL input field and its own "Parse posting"
trigger exist inside the `jobPostingEditable` branch, alongside the existing textarea and file input;
entering a URL and triggering parse shows the same review-stage UI the paste-text flow already
produces. GREEN: the new input row, styled with the file's existing token constants and the shared
`Button` component — no new classes invented ad hoc.

**S2.3 — Accessibility and error-state pass.** RED: a failed URL fetch (e.g. a rejected/invalid URL)
surfaces via the existing `errorBannerClasses` + `role="alert"` pattern the paste-text failure path
already uses. GREEN: any gap the test surfaces.

### Definition of Done (expected completion)

- A user can paste a URL, click Parse, and land in the same review UI the text-paste flow produces.
- Existing paste-text flow, its tests, and its behavior are completely unaffected.
- The new input row is visually and structurally consistent with Epic 3.1's restyled
  `IngestDocument.tsx` — same tokens, same `Button` component, same error-banner convention. No
  parallel/inconsistent styling introduced.
- Satisfies "AI-Native Pillars Applied to This Epic" above: explicit `Promise<TaskDraft[]>` return
  type on the new API function, no `any`, no placeholder code at GREEN.

### Prerequisites and what this unblocks

- Depends on: Sprint 1.
- Unblocks: nothing further within this epic.

### Code review findings (fill in after this sprint's PR is reviewed)

**Antigravity (Claude Opus 4.6) review pass (2026-08-22) — PR #64.** Posted directly to the PR as
inline comments — see
[review #5001097525](https://github.com/AndrewVanDelden/TaskFlow/pull/64#pullrequestreview-5001097525).
This is a clean, well-structured PR. The `parseFrom` extraction in `useIntakeFlow.ts` is textbook
DRY/OCP. Explicit types throughout, no `any`, SCU maintained. One minor finding:

1. **`TaskFlow.Web/src/features/IngestDocument.tsx:142`** (PLAUSIBLE)
   - **Why:** The existing textarea (line 162) has `aria-busy={intake.stage === 'parsing'}` to
     signal assistive technology during async operations. The new URL `<input>` does not, even
     though it participates in the same `parsing` stage. The input is already `disabled` during
     parsing, so this is low severity — but it's an accessibility consistency gap within the
     file's own established pattern.
   - **Fix:** Add `aria-busy={intake.stage === 'parsing'}` to the URL input element.
   - **RED test:** An axe/a11y audit or a targeted `getByRole` assertion checking `aria-busy`
     transitions during the URL parse path.
   - **Status:** Fixed (2026-08-22). RED-first test `the URL input has aria-busy while parsing,
     matching the textarea` (delayed MSW response, mirrors the existing 'starting stage' pattern for
     making a transient stage observable), confirmed RED then GREEN.

**Independent manual review (2026-08-22) — PR #64.** Posted directly to the PR as an inline
comment — see
[review #5001099066](https://github.com/AndrewVanDelden/TaskFlow/pull/64#pullrequestreview-5001099066)
for the full text. Cross-checked against finding 1 above: accurate and reasonable, not duplicated.
One further finding:

2. **`TaskFlow.Web/src/features/IngestDocument.tsx:142`** (reuse, low severity)
   - **Why:** The new URL `<input>`'s className hand-rolls the exact same token combination as the
     established `textareaClasses` constant (`bgSurface`, `border-white/10`, `text-white`,
     `text-sm`, `focusRingAccent`) instead of reusing or extracting a shared base — identical
     token-for-token except sizing utilities. If the Nocturne input styling ever changes, this spot
     won't pick it up automatically the way the three textareas sharing `textareaClasses` will.
   - **Fix:** Factor the non-sizing portion into a shared constant alongside `textareaClasses`
     (e.g. an `inputBaseClasses` both compose from).
   - **RED test:** Not applicable — pure styling consistency, not a behavior change.
   - **Status:** Fixed (2026-08-22) exactly as suggested. New `inputSurfaceClasses` constant holds
     the shared tokens; `textareaClasses` and the URL input's className both compose from it, only
     sizing utilities differ per use-site. Rendered class strings unchanged, so no test needed beyond
     the existing suite staying green (510/510 backend, 349/349 frontend).

### Post-sprint retrospective (fill in once this sprint ships)

*(Not yet started — nothing to record.)*

---

## TDD Loop and Git Workflow (unchanged from Epic 3 / Epic 3.1)

1. Claude writes a failing test (RED) with exact file path, namespace/imports, and usings.
2. You run `dotnet test` / `npm run test` (or `.\test` for the full suite) and confirm it is red.
3. Claude writes the simplest code to pass (GREEN).
4. You run again and confirm green.
5. Refactor if needed, tests staying green.

One branch and one PR per sprint into `develop`. Branch names: `feature/epic3.2-sprint-N-short-name`.
