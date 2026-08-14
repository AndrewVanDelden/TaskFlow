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

**Cross-epic touch point:** Epic 3.1 Sprint 4 (Ingest & Hand-off, not yet started as of this epic's
creation) explicitly locks "The design's URL-input affordance... is **not built** in this sprint" and
restyles only the existing paste-text flow. Whichever of these two epics lands second needs to
reconcile its UI with the other's — Epic 3.2 builds the URL input against the *current*, un-restyled
`IngestDocument.tsx`; Epic 3.1 Sprint 4 (whenever it runs) needs to carry the URL input row forward
into its restyled layout rather than dropping it. Recorded here so neither epic silently regresses
the other's work — check this note before starting either Epic 3.1 Sprint 4 or closing out this epic.

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

## Confirmed against the repo (2026-08-14, before any Epic 3.2 code exists)

| Claim | Status |
|---|---|
| No HTML-parsing library exists anywhere in `TaskFlow.Api.csproj`'s dependencies. | **Confirmed**, file read directly. |
| No outbound-HTTP-fetch capability (`HttpClient`/`IHttpClientFactory` for fetching arbitrary external URLs) exists anywhere in `TaskFlow.Api` — the only external HTTP caller is `Anthropic.SDK`, a fixed, trusted destination. | **Confirmed**, repo-wide search. |
| `IJobPostingIngestionParser.ParseAsync(string documentText, ...)` is the exact same signature the existing `/api/JobApplications/parse` endpoint already calls, and internally just delegates to `TieredIngestionParser` (free rule-based tier, escalating to Claude). | **Confirmed**, `JobPostingIngestionParser.cs` read directly. This is the reuse point: once a URL is turned into plain text, it is handed to this exact same method, unchanged — the entire Sprint 3 (Epic 3.1) `Company` plumbing, the free/paid tiering, everything downstream, is inherited for free. |
| `JobApplicationsController.Parse` (`POST /api/JobApplications/parse`) takes `IngestDocumentDto { Content }` and calls `_parser.ParseAsync(dto.Content)`. | **Confirmed**, file read directly. A new, separate endpoint is added rather than overloading this one (see "Decisions owned here"). |
| `Program.cs` has no `AddHttpClient(...)` registration of any kind today. | **Confirmed.** A new named/typed `HttpClient` registration is added for exactly this feature, configured with the SSRF mitigations below — not the default client, and not reused for anything else. |

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

### Architecture

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
- **Frontend: a URL input row added to the *current* `IngestDocument.tsx`** (the un-restyled version —
  Epic 3.1 Sprint 4 hasn't run yet; see the cross-epic note at the top of this doc), alongside the
  existing paste-text textarea, per the original design handoff's own reference copy ("Paste a job
  posting URL — or type/paste the description"). A "Parse posting" action on the URL field calls the
  new `/parse-url` endpoint; success populates `intake.drafts` exactly as the text-paste flow already
  does (same downstream stage-machine, same review UI) — no new frontend state machine, this plugs
  into `useIntakeFlow`'s existing `parse`-equivalent flow as a sibling entry point, not a parallel one.

---

## Roadmap

| Sprint | What | Status |
|---|---|---|
| **1** | Secure URL fetch + HTML extraction (backend) | Ready — architecture above, no code yet |
| **2** | Frontend URL input | Ready — architecture above, no code yet |

## Definition of Done (Epic 3.2)

- A user can paste a job-posting URL into Ingest and get the same parsed-result experience as pasting
  text today (same `TaskDraft`s, same Company extraction, same downstream flow).
- Every SSRF mitigation in "Decisions owned here" is implemented and has a passing negative test
  proving it actually rejects the specific attack it exists to stop.
- The existing paste-text `/parse` flow and its full test coverage are completely unaffected.
- Full suite green via `.\test` (backend + frontend, with coverage) before `develop → main`.

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

### Prerequisites and what this unblocks

- Depends on: nothing — foundational for this epic.
- Unblocks: Sprint 2 (frontend), which does not start until this sprint's tests are independently
  green via a fresh `.\test` run.

### Code review findings (fill in after this sprint's PR is reviewed)

*(Not yet started — nothing to record.)*

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
  (same `stage` transitions, same `role="alert"` error rendering) — no new UI pattern invented.

### Tasks

**S2.1 — `parseJobPostingUrl` API function + `useIntakeFlow` URL entry point.** RED: calling the new
hook entry point posts to `/api/JobApplications/parse-url` with the given URL and, on success,
transitions to the `review` stage with the returned drafts, exactly like the existing `parse()`
does for pasted text. GREEN: the API function and hook wiring.

**S2.2 — URL input row in `IngestDocument.tsx`.** RED: a URL input field and its own "Parse" trigger
exist alongside the existing textarea; entering a URL and triggering parse shows the same
review-stage UI the paste-text flow already produces. GREEN: the new input row.

**S2.3 — Accessibility and error-state pass.** RED: a failed URL fetch (e.g. a rejected/invalid URL)
surfaces via the same `role="alert"` pattern the existing paste-text failure path uses. GREEN: any
gap the test surfaces.

### Definition of Done (expected completion)

- A user can paste a URL, click Parse, and land in the same review UI the text-paste flow produces.
- Existing paste-text flow, its tests, and its behavior are completely unaffected.

### Prerequisites and what this unblocks

- Depends on: Sprint 1.
- Unblocks: nothing further within this epic.

### Code review findings (fill in after this sprint's PR is reviewed)

*(Not yet started — nothing to record.)*

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
