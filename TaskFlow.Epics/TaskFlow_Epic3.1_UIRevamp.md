# TaskFlow — Epic 3.1: UI Revamp (Nocturne)

**Epic map, stated once here to prevent mix-ups across these cross-referencing docs:** this is
**Epic 3.1** — sequenced immediately after Epic 3 (Resume and Cover-Letter Builder) and **before**
Epic 4 (Postgres Migration), Epic 5 (Deployment Infrastructure), and Epic 6 (User Credentials),
because it's the one being picked up next. It is a `TaskFlow.Web`-only visual and interaction
redesign of the Board, Ingest, and Login screens, replacing the current stock-Tailwind look with
the "Nocturne" dark, agent-first design system. It does **not** depend on Epic 4 (Postgres), Epic 5
(Deployment), or Epic 6 (User Credentials) — those are backend/infrastructure epics with no visual
surface — so this sequencing is a priority choice, not a technical dependency; Epic 3.1 could just
as validly run after any of them. The one real cross-epic touch point, a small domain-model
addition (`JobApplication.Company`, see Sprint 3), is called out explicitly where it happens; it
does not require any of Epics 4–6 to land first.

---

## Standing rules (inherited, restated in full because this doc's own reviewer asked for it)

**Every task in every sprint below is built strict TDD: a failing test (RED), confirmed red by an
actual test run, before any implementation (GREEN).** Clean code, SOLID, and DRY apply to every
line — that means, concretely, for this epic:

- No component ships without its test written first. A visual reskin is not exempt from RED/GREEN
  just because the change is "only CSS" — see Sprint 0's accessibility-testing decision below for
  why that instinct is exactly wrong for this particular epic.
- Duplication is fixed at the source (one shared primitive, one shared style map, one shared
  rail-rendering component — not copy-pasted per screen). Where this doc says "shared," that is a
  DRY decision, not a suggestion.
- Domain types never reuse .NET BCL names; result-bearing operations return the shared `Result`
  type from `Common/`. Applies to the one backend change in this epic (Sprint 3's `Company` field)
  exactly as it applies everywhere else in this codebase.
- Nothing is marked done without being actually checked — a fresh `.\test` run, read from
  `test-results.txt`, not an assumption from "the diff looks right."
- This is restated in full, not by reference, because this specific epic is a full-surface visual
  rewrite touching almost every existing frontend test file — the risk of quietly drifting into
  "just match the picture" and skipping RED is higher here than in a typical backend sprint.

Not re-litigated here beyond this: see `CLAUDE.md` and `TaskFlow_Epic3_ResumeBuilder.md`'s
"Standing rules" section for the full shared framework (git/tooling boundary, verification
discipline, etc.), which this epic inherits unmodified.

---

## The problem, stated plainly

TaskFlow's UI today is default Tailwind slate: red/amber/green priority and status pills
(`TaskFlow.Web/src/lib/styles.ts`), an oversized colored `ExecutorControl` banner, a flat top-nav
(`NavBar.tsx`), boxed/badged activity rows, and a textarea-driven Ingest flow. None of it is wrong,
exactly — it works, and every screen has real test coverage — but it reads as a generic admin
panel, not as the product it actually is: an autonomous agent workspace managing real job
applications. **This epic replaces the look and the interaction model, screen by screen, without
regressing any existing behavior or test coverage**, and fixes a short list of real gaps the
design handoff itself didn't resolve (see "Decisions owned here" below).

## About the source design bundle

`design_handoff_ui_revamp/EPIC.md` and `design_handoff_ui_revamp/TaskFlow Board Revamp
(standalone).html` are the original design handoff: a design-token cheat sheet plus a rendered
HTML prototype (three iterative "turns," of which **2a** (Board), **3a** (Ingest), and **3b**
(Login) are the signed-off references). Both were read in full before writing this doc — the
prototype is a large exported design-canvas file (a compressed JS runtime plus an escaped-HTML
payload), extracted and read directly rather than skimmed, so every design token, class, and copy
string quoted below is something actually seen in that file, not inferred from the summary alone.

**This doc is now the single source of truth going forward, per this project's own established
convention (one epic doc per epic, updated in place, not a review file or design doc left standing
alongside it).** Every decision, task, and open question from the handoff is folded in below,
either as a locked decision, a sprint task, or an explicit non-goal. **`design_handoff_ui_revamp/`
is deleted once this epic ships** — see "Epic close-out" at the end of this doc. Until then it
stays, read-only, as the design reference implementers can zoom into for exact spacing/color
values; nothing in it is ever pasted into the app directly (it's a static prototype, not real
React/Tailwind).

---

## Confirmed against the repo (2026-08-13, before any Epic 3.1 code exists)

Read directly, not assumed from the design handoff's own description of "current state" (which
doesn't describe the current app at all — it only describes the target):

| Claim | Status |
|---|---|
| `NavBar.tsx` renders exactly two links, **Board** and **Ingest** — no third nav item exists today. | **Confirmed**, exact file read. |
| No `/activity` route exists anywhere in `App.tsx`. The only authenticated routes are `/board`, `/ingest`, `/ingest/:sessionId`. | **Confirmed.** The design's sidebar lists a third icon nav item, "Activity," with no destination specified — this is a real, unresolved gap in the handoff, not something to infer. See Sprint 1. |
| `ExecutorControl.tsx` renders a bordered card (`p-4 mb-6`) that tints emerald when enabled, red when paused, with a colored status pill. | **Confirmed**, exact code and class names read. This is precisely the "oversized banner with red/amber/green state" the design replaces. |
| `lib/styles.ts` — `priorityStyles` (red/amber/slate fills) and `actionStyles` (red/blue/amber/emerald/violet fills) are the **only** two shared color maps in the frontend. | **Confirmed.** Both are exactly what the design's "no red/amber/green, status in type not color" rule needs to retire. |
| `TaskCardView.tsx` renders priority as a **filled, colored pill** (via `priorityStyles`), not quiet text, and shows `task.assignedToName` where the design shows a company name. | **Confirmed**, exact file read. |
| **No `Company` field exists anywhere in the domain model** — not on `JobApplication` (`TaskFlow.Api/Models/JobApplication.cs`: `Id`, `State`, `CreatedAt`, `IngestionSessionId`, `OwnerId`, `Tasks` — no company/employer field of any kind), not on `TaskItem`, not on `TaskDraft` (`TaskFlow.Api/Ingestion/TaskDraft.cs`: `Title`, `Description`, `Kind`, `Section` — no company field), not threaded through `assembleApplication`'s request DTO. | **Confirmed** by reading all three files directly. **This is the single biggest gap in the whole handoff** — the flagship, fully-signed-off Board card design (2a/2b) is built entirely around a distinct "Vercel / Linear / Anthropic"-style company line, and today there is no structured place to store or parse that value at all. Not a styling task — a schema change. See Sprint 3. |
| `useIntakeFlow.ts`'s `startTailoring()` stays on the Ingest page and renders `IntakeProgress` inline; it does not navigate anywhere. | **Confirmed**, exact code read. The design's Story 4 spec ("(2) navigate to the Board") is a real, new behavior change, not a restyle of something that already happens. |
| No URL-fetch/scrape capability exists anywhere in `TaskFlow.Api` or `TaskFlow.Core` (repo-wide search, zero hits). `IngestDocument.tsx`'s existing "Parse" button parses **pasted text only**, via `intake.parse()` → `parseJobPosting(jobPostingText)`. | **Confirmed.** The design's Ingest reference shows a URL input row with a "Parsed from vercel.com/careers · 2s ago" caption, implying server-side fetch of an arbitrary user-supplied URL. That is new backend surface with its own SSRF exposure — not part of this epic. See "Decisions owned here" and Sprint 4. |
| `Login.test.tsx` asserts an in-place toggle: default mode shows a `Sign in` button; clicking a button matching `/need an account\? register/i` reveals a `Name` field and changes the submit button to `Create account`. | **Confirmed**, exact test read. The design's split-card copy ("Create an account" as a secondary CTA below a divider) does not match this trigger text verbatim — reconciled explicitly in Sprint 2, not left as a silent break. |
| `package.json` — `@dnd-kit/core`, `@dnd-kit/sortable`, `@microsoft/signalr`, `react-markdown`, `rehype-sanitize` are already present. `@phosphor-icons/react` is **not** present. No accessibility-testing package (`axe-core`, `jest-axe`, `vitest-axe`, or similar) is present anywhere in `package.json` or the test setup. | **Confirmed.** Matches the design handoff's own dependency flag for icons; the missing a11y-testing gap is this doc's own finding, addressed in Sprint 0. |
| WCAG contrast, computed directly (relative luminance formula, sRGB): `#9397ab` (neutral-500) on `#161826` (page bg) ≈ **6.1:1** — passes AA for any text size. `#75798c` (neutral-600) on the same bg ≈ **4.09:1** — **fails** the 4.5:1 AA floor for normal text, and the design spec's own usage of neutral-600 (column counts, `text-[11px]` micro-labels) is exactly that size class. | **Confirmed by calculation**, not assumed. See Sprint 0. |

---

## Decisions owned here, before dispatching any engineer (2026-08-13)

These are epic-wide decisions that apply across sprints, made now so no engineer has to guess or
re-litigate them mid-sprint.

- **Icons: add `@phosphor-icons/react` as a real dependency, not inline SVGs.** The design uses
  Phosphor icons in well over a dozen places (nav rail, search, link icon, sparkle, chevrons,
  check, pause). Fifteen-plus hand-copied inline `<svg>` blocks scattered across components is a
  worse DRY violation than one well-maintained icon package. One new dependency; nothing else in
  the design needs one (confirmed against `package.json` above).
- **Accessibility contrast rule, locked from the computed numbers above: any text under 18px uses
  `neutral-500` (`#9397ab`) or lighter. `neutral-600` (`#75798c`) is reserved for large text
  (≥18px), icons, dividers, and other non-text fills** — never for column counts or the
  `text-[11px]` micro-labels the original design cheat sheet assigned it to. This is a correction
  to the handoff's own token table, not an addition to it.
- **`prefers-reduced-motion` is a first-class requirement, not an afterthought — the original
  handoff never mentions it despite being animation-heavy by design** (pulsing dots, a
  continuously shimmering progress line, an 8-sparkle click-triggered burst). Locked rule:
  every *continuous/idle* animation (pulse, shimmer) is wrapped so it renders in its static end
  state under `prefers-reduced-motion: reduce` — never removed, just not moving. Every
  *triggered* animation (the tailoring-square burst) collapses to an instant state change with no
  keyframe under the same media query, per WCAG 2.3.3 guidance. Verified by a rendered-DOM
  assertion under a mocked `window.matchMedia`, honestly scoped as a CSS-class-level check, not a
  claim of testing real browser animation playback.
- **Add `vitest-axe` (or an equivalent axe-core binding compatible with the existing
  vitest + jsdom + Testing Library setup) as a new devDependency in Sprint 0, and require one
  `expect(await axe(container)).toHaveNoViolations()` assertion in every component test this epic
  adds or rewrites.** This is what turns the contrast and accessible-name findings above from
  one-time manual checks into an enforced, repeatable RED test — directly serving this epic's own
  "strict TDD" mandate, and closing a real, confirmed gap (no such tooling exists in this repo
  today).
- **Every icon-only interactive control gets a real accessible name (`aria-label` or equivalent),
  no exceptions.** `Navigation.test.tsx` already queries `getByRole('link', { name: 'Ingest' })`
  against the current text-labeled nav — the redesigned icon-only sidebar must keep that query
  passing (or the doc records an intentional, reviewed rename, never a silent break) and gains the
  same treatment for Board and the new Activity item.
- **The "Activity" sidebar nav item routes to a new, standalone `/activity` page** (`Activity.tsx`,
  new), rather than scrolling/anchoring within the Board. The Board's own right-rail activity feed
  and the new full page render the same underlying list via one shared, extracted presentational
  component — DRY, one feed-rendering implementation, two places it's mounted. This resolves a real
  ambiguity the handoff left open (a nav icon called "Activity" with no stated destination, on top
  of an already-always-visible activity rail on the Board itself). See Sprint 1.
- **`JobApplication` gains a `Company` field.** Confirmed above: this doesn't exist anywhere today,
  and the entire Board card redesign depends on it. Best-effort, nullable — the free rule-based
  tier of the job-posting parser is not expected to reliably extract an employer name from
  arbitrary pasted text and may legitimately return `null`; the existing Claude-tier parser
  (`ClaudeJobPostingParser`, already doing structured extraction per Epic 3's tiered-ingestion
  pattern) is well-suited to extract it and is where this is expected to actually work in practice.
  The frontend renders a quiet placeholder, never a crash or a blank, when `Company` is `null` —
  which will legitimately happen for every task created before this ships. See Sprint 3.
- **The job-posting "Parse from a URL" flow is explicitly out of scope for this epic.** It requires
  new backend surface (server-side fetch of an arbitrary user-supplied URL) with a real SSRF
  attack surface that has not been designed, let alone reviewed. This epic restyles the existing
  paste-text Parse flow to match the design's visual language (input row, parsed-result card,
  requirement chips) without adding URL fetching. If URL-based parsing is wanted later, it gets its
  own epic with its own security design — recorded in the open decisions log below, not silently
  dropped.
- **Login's copy is reconciled, not left to silently drift from its own test.** The existing
  toggle mechanism (one form, one boolean mode) is kept — it works and is tested — restyled into
  the split brand+form card. The toggle-trigger copy changes from "Need an account? Register" to
  **"Create an account"** in sign-in mode (matching the design) and **"Already have an account?
  Sign in"** in register mode (the natural inverse, not specified by the design but required for
  the toggle to make sense both directions). `Login.test.tsx`'s `/need an account\? register/i`
  query is updated to match, as a named, deliberate change recorded here — not a regression
  discovered later. The submit button's own text (`Sign in` / `Create account`) is unchanged and
  needs no test update.
- **Sequencing: Shell → Login → Board → Ingest**, per this epic's own pre-work review (recorded in
  full in the sprints below). Shell first because every authenticated screen nests inside it.
  Login next because it is fully independent of the shell's authenticated routes and the lowest-
  risk place to prove the token system and primitives actually work end to end before touching the
  more complex, stateful Board. Board next because it is the fully signed-off reference and
  self-contained. Ingest last because it carries this epic's only real scope-boundary decision
  (the URL-parse descope above) and depends on nothing else in this epic.

---

## Roadmap

| Sprint | What | Status |
|---|---|---|
| **0** | Design System Foundations, Accessibility & Test Infrastructure | **Merged to develop** — [PR #52](https://github.com/AndrewVanDelden/TaskFlow/pull/52), review findings fixed |
| **1** | App Shell (Sidebar + Top Bar) | **Merged to develop** — [PR #53](https://github.com/AndrewVanDelden/TaskFlow/pull/53), review findings fixed |
| **2** | Login | **Merged to develop** — [PR #54](https://github.com/AndrewVanDelden/TaskFlow/pull/54), review findings fixed |
| **3** | Board (application-centric cards, quiet executor line, Activity rail) | **Merged to develop** — [PR #55](https://github.com/AndrewVanDelden/TaskFlow/pull/55), review findings fixed |
| **4** | Ingest & Hand-off (restyled paste flow, tailoring square) | **Merged to develop** — [PR #57](https://github.com/AndrewVanDelden/TaskFlow/pull/57), review findings fixed. Last sprint in this epic's own scope. |

**Post-sprint Board fixes (outside the sprint numbering, found via live use after Sprint 3 shipped), also merged:**
- [PR #58](https://github.com/AndrewVanDelden/TaskFlow/pull/58) — ExecutorControl running/paused clarity, AgentStatus card alignment, and the root-cause fix for Epic-3 sibling tasks getting permanently stuck below `Approved` when approved/rejected individually (export buttons silently unreachable).
- [PR #59](https://github.com/AndrewVanDelden/TaskFlow/pull/59) — Board Done-column soft-archive (bulk "Clear Done" + per-card archive, fully restorable via a new `/archive` view). A real feature request (not a Nocturne restyle task), tracked here since it touches the same Board surface.

Fresh `develop` checkout, full suite green: backend 465/465, frontend 309/309 (2026-08-16).

## Definition of Done (Epic 3.1)

- Every authenticated screen renders inside the new sidebar shell; `NavBar.tsx` is retired.
- Board, Login, and Ingest match the signed-off Nocturne reference (2a, 3a, 3b) — colors, type,
  spacing, and interaction per the design handoff's cheat sheet, corrected per the token fix above.
- No red/amber/green status coloring remains anywhere in the touched surfaces; status is carried by
  type and copy, with two locked exceptions: the muted green check for "Approved" (per the
  design), and emerald for a running/connected signal — `AgentStatus`'s "Running" pill, the Board's
  "Live" connection dot, and `ExecutorControl`'s status dot (added post-Sprint-3, PR #58, after the
  original all-neutral scheme made running/paused indistinguishable at a glance; see the close-out
  audit below for the full decision record). `ReviewActions`' hand-rolled green Approve / red
  Reject is a third, separately-recorded exception (predates this epic and the shared `Button`
  component entirely — see the close-out audit) — not a violation to fix, a scope boundary Sprint 3
  itself already drew.
- Every icon-only control has a real, tested accessible name.
- `vitest-axe` runs against every component this epic touches, zero violations, as part of the
  normal test suite (not a separate manual pass).
- `prefers-reduced-motion: reduce` disables every continuous animation and collapses every
  triggered one, verified by test.
- `JobApplication.Company` exists, is populated best-effort by ingestion, and renders on the Board
  without breaking older/Generic tasks that have none.
- `design_handoff_ui_revamp/` is deleted from the repository.
- Full suite green via `.\test` (backend + frontend, with coverage) before `develop → main`.

---

## Sprint 0 — Design System Foundations, Accessibility & Test Infrastructure

### Why this sprint exists

Every later sprint depends on the same primitives (button, focus ring, column-header pattern),
the same corrected token set, and the same testing tools existing first. Building any screen
before this sprint would mean redoing it once the contrast fix, reduced-motion pattern, and
`vitest-axe` wiring land — exactly the kind of rework this project's own sprint-0-gates-everything
precedent (Epic 3 Sprint 0) exists to avoid.

### Locked decisions

(Carried from "Decisions owned here" above, restated as this sprint's scope:) the neutral-500
contrast floor for small text, the `prefers-reduced-motion` pattern for continuous vs. triggered
animation, `vitest-axe` as new test infrastructure, and `@phosphor-icons/react` as a new
dependency.

### Files involved

- `TaskFlow.Web/package.json` (edit — add `@phosphor-icons/react`, `vitest-axe`)
- `TaskFlow.Web/src/index.css` or equivalent global stylesheet (edit — Nocturne CSS custom
  properties: `--color-bg`, `--color-surface`, `--color-text`, `--color-accent` ramp,
  `--color-neutral` ramp, focus-ring rule, corrected per the contrast fix)
- `TaskFlow.Web/src/components/ui/Button.tsx` (new — primary/ghost variants per the design's
  outline-not-fill rule)
- `TaskFlow.Web/src/components/ui/ColumnHeader.tsx` (new — the shared micro-label + count pattern,
  built once, reused by every column/section header instead of copy-pasted markup)
- `TaskFlow.Web/src/test/axe.ts` (new — shared `vitest-axe` setup/matcher, one place, reused by
  every component test rather than configured per file)
- `TaskFlow.Web/src/test/reducedMotion.ts` (new — shared helper to mock `window.matchMedia` for
  reduced-motion assertions, reused across every animated component's tests)
- `TaskFlow.Web/src/lib/tokens.ts` (new — token class-name constants and the `designTokens` list
  the RED test iterates)
- `TaskFlow.Web/src/test/stripCssLayers.ts` (new — see U0.1 finding below)
- `TaskFlow.Web/src/test/setup.ts` (edit — injects the real, layer-stripped Tailwind stylesheet
  into every test's jsdom document; see U0.1 finding below)

### Tasks

**U0.1 — Design tokens.** RED: a rendered element using each token class (`bg-[#161826]`,
`text-[#e9e9ed]`, the full accent/neutral ramps) resolves to the exact computed color expected —
asserted via `getComputedStyle` in a component test, not eyeballed. GREEN: token definitions
(CSS custom properties or Tailwind arbitrary-value usage, per the project's existing "Tailwind v4
utilities only, no custom theme/config" constraint) matching the corrected table above (neutral-600
excluded from any small-text usage).

**U0.2 — Shared `Button` primitive.** RED: primary renders an accent outline (not a fill), ghost
renders transparent with a hover tint, both expose a visible `:focus-visible` ring using the accent
color and never the browser default blue. GREEN: one `Button` component, two variants, consumed by
every later sprint rather than each screen hand-rolling its own button classes.

**U0.3 — Shared `ColumnHeader` primitive.** RED: renders the `text-[11px] uppercase tracking-wide`
micro-label in `neutral-500` (not `neutral-600`, per the contrast fix) plus a count, both meeting
the `vitest-axe` contrast rule below. GREEN: one component, reused by the Board's columns and any
other section header this epic introduces.

**U0.4 — `vitest-axe` wired into the test setup.** Infrastructure, not itself a failing-test-first
task — this is what every later sprint's accessibility assertions run against, mirroring this
project's own precedent (Epic 4's `PostgresFixture`, itself infra rather than a RED test).
Verification: a trivial rendered component with a known, deliberately-introduced contrast
violation is asserted to fail the `axe` check — proving the tool actually catches something —
before it's relied on anywhere else.

**U0.5 — Reduced-motion helper and pattern.** RED: a component using a continuous pulse/shimmer
animation renders with the animation's static end-state class when `window.matchMedia
('(prefers-reduced-motion: reduce)').matches` is mocked `true`, and with the animating class
otherwise. GREEN: the shared `reducedMotion.ts` test helper plus the CSS pattern (a
`motion-reduce:` Tailwind variant or equivalent media-query rule) every animated component in
later sprints is required to use.

**U0.1 finding (2026-08-13, discovered mid-task, not assumed going in): jsdom does not apply CSS
inside `@layer` blocks when computing style, and Tailwind v4 wraps every generated utility class
in `@layer utilities`.** First GREEN attempt (`tokens.ts` + a plain `import '../index.css'` in
`setup.ts`) still failed all 14 assertions with browser-default values
(`rgba(0, 0, 0, 0)` / `rgb(0, 0, 0)`) even though the compiled CSS contained the exact expected
rule (`.bg-\[\#161826\] { background-color: rgb(22, 24, 38); }`, confirmed by dumping
`document.styleSheets` in a throwaway diagnostic test). Isolated by hand-injecting a `<style>` tag
with the identical rule both inside and outside a manually-written `@layer utilities { }` wrapper:
outside, `getComputedStyle` resolved correctly; inside, it did not — confirming this is a jsdom
`@layer` gap, not a bug in the token classes, the selector escaping, or the injection mechanism.
Fix: `src/test/stripCssLayers.ts` (new, own unit test), a small parser that unwraps `@layer name {
... }` blocks to their plain contents and drops bare layer-order declarations (`@layer a, b;`),
preserving rule text and order. `setup.ts` now imports `../index.css?inline` (raw CSS, not
auto-injected), runs it through `stripCssLayers`, and appends the result as a `<style>` tag once in
a `beforeAll`. This is test-infrastructure only — production CSS via `main.tsx`'s plain
`import './index.css'` is untouched and still real, layered Tailwind output. Every later sprint's
`getComputedStyle`-based assertions (e.g. U0.2's focus-ring color, U0.3's contrast check) rely on
this fix already being in place.

**U0.6 — Phosphor icons installed and smoke-tested.** RED: one icon (e.g. the sidebar's board
icon) renders via `@phosphor-icons/react` without throwing and exposes the expected `aria-hidden`/
labeling contract (icons are decorative by default; the *interactive control* they sit inside
carries the accessible name, not the icon itself — confirmed pattern, not assumed). GREEN: package
installed, one working import proven.

### Definition of Done (expected completion)

- `Button` and `ColumnHeader` exist, tested, and are the only way later sprints build these
  patterns — no screen hand-rolls its own button or column-header markup.
- The full token set is defined and contrast-correct per the computed numbers in this doc.
- `vitest-axe` is wired in and proven to actually catch a violation.
- The reduced-motion pattern is defined, tested, and ready for every animated component in Sprints
  3–4 to use.
- `@phosphor-icons/react` is installed and one icon renders correctly.

### Prerequisites and what this unblocks

- Depends on: nothing — this is foundational, like Epic 3's Sprint 0.
- Unblocks: every other sprint in this epic. No screen work starts before this sprint is green.

### Code review findings (2026-08-13) — PR #52

Manual review posted directly to the PR as inline comments (high effort, 8 finder angles,
verified pass) — see
[review #4933068259](https://github.com/AndrewVanDelden/TaskFlow/pull/52#pullrequestreview-4933068259)
for the full text, each comment anchored to its exact line. No prior Copilot review to cross-check
(its automated pass on this PR hit its quota and returned no findings). **Status: RESOLVED — all 4
findings fixed, verified via a fresh `.\test` run (backend 414/414, frontend 39/39 files, 194/194
tests), and merged to `develop` in PR #52:**

1. `stripCssLayers.ts:24` (CONFIRMED, FIXED — d871bba) — infinite loop on an unterminated `@layer`
   token (both `semiIndex`/`braceIndex` at `-1` reset the scan cursor to 0 instead of advancing past
   it). Added a RED test for the exact malformed input, then a guard that leaves the unterminated
   remainder unchanged instead of looping.
2. `usePrefersReducedMotion.ts:5` (PLAUSIBLE, FIXED — 40085cb) — no `change`-event subscription,
   unlike every other hook in this codebase. Architect ruling: fix now, not defer — every other
   external-source hook here subscribes live, and the epic doc already states later sprints are
   "required to use" this hook for live motion-sensitivity, so a static snapshot didn't meet its own
   stated contract. Added a `useEffect` subscription (with cleanup) and a live-update RED test.
3. `package.json:26` (CONFIRMED, FIXED — df4f678) — `vitest-axe` moved from `dependencies` to
   `devDependencies`, matching every other test-only tool in this project.
4. `Button.test.tsx:49` (CONFIRMED, nit, FIXED — df4f678) — dynamic `import()` replaced with the
   codebase's standard static import.

### Post-sprint retrospective (fill in once this sprint ships)

*(Not yet started — nothing to record.)*

---

## Sprint 1 — App Shell (Sidebar + Top Bar)

### Why this sprint exists

Every authenticated screen nests inside the shell. Building the Board, Login, or Ingest redesign
before the shell exists would mean re-wrapping each of them afterward.

### Goal

Replace `NavBar.tsx`'s top-nav with a 60px fixed left rail (icon nav: Board, Ingest, Activity, plus
an avatar/sign-out affordance) and a consistent per-screen top bar (title + count, actions
top-right). `App.tsx`'s `ProtectedLayout` gains the new `/activity` route decided above.

### Files involved

- `TaskFlow.Web/src/components/SideBar.tsx` (new, replaces `NavBar.tsx`)
- `TaskFlow.Web/src/components/NavBar.tsx` (deleted once `SideBar` ships and every consumer is
  migrated — not left as dead code)
- `TaskFlow.Web/src/App.tsx` (edit — `ProtectedLayout` renders `[SideBar | Outlet]`; new
  `/activity` route)
- `TaskFlow.Web/src/features/Activity.tsx` (new)
- `TaskFlow.Web/src/components/AgentFeed.tsx` (edit — feed-row rendering extracted into a shared
  piece consumed by both the Board's rail and the new `Activity` page, per the DRY decision above)
- `TaskFlow.Web/src/features/Navigation.test.tsx` (edit only as needed to keep passing against the
  new icon-only nav's accessible names — no behavioral change to what it asserts)

### Decisions owned here, before dispatching any engineer

- **Nav items and their accessible names, locked:** Board (`aria-label="Board"`, `/board`), Ingest
  (`aria-label="Ingest"`, `/ingest` — the existing `IngestRedirect` target, unchanged), Activity
  (`aria-label="Activity"`, new `/activity`, per the epic-wide decision above). Active-item styling
  (`bg-[#9184d9]/15 text-[#e7e5fe] rounded-[10px]`) per the design; inactive `text-[#75798c]` is
  **not** used per the contrast fix — inactive nav items use `neutral-500`, one step lighter than
  the design handoff's own (uncorrected) spec.
- **Sign-out affordance:** the design shows only "an avatar at bottom" with no stated sign-out
  mechanism. Locked: the avatar is itself a button that opens a small menu containing "Sign out,"
  calling the existing `useAuth().signOut()` unchanged — confirmed safe: `AuthProvider.test.tsx`
  tests `signOut()` directly via the hook, not by querying nav UI text, so this is a free choice
  with zero test collision.
- **`Activity.tsx` is a thin page wrapping the shared feed-rendering component extracted from
  `AgentFeed.tsx` in U0's spirit but done here, since it's this sprint's own new consumer** —
  full-height, no 300px-rail sizing constraint. The Board's own aside (Sprint 3) mounts the same
  shared piece at its existing width. One implementation, two mount points.

### Tasks

**U1.1 — `SideBar` renders the three nav items with real accessible names.** RED:
`getByRole('link', { name: 'Board' })`, `{ name: 'Ingest' }`, and `{ name: 'Activity' }` all
resolve; active item reflects the current route. GREEN: `SideBar.tsx`, icon-only, `aria-label` on
each `NavLink`.

**U1.2 — `App.tsx` wraps `ProtectedLayout` in `[SideBar | Outlet]`; `/activity` route added.** RED:
navigating to `/activity` while authenticated renders the `Activity` page; `Navigation.test.tsx`'s
existing assertions (link name `Ingest`, redirect-when-unauthenticated, nav-from-board-to-ingest)
all still pass unchanged. GREEN: routing and layout changes.

**U1.3 — Sign-out via avatar menu.** RED: opening the avatar menu and clicking "Sign out" calls
`signOut()` and the user is redirected to `/login` (mirrors `AuthProvider.test.tsx`'s existing
behavioral contract, exercised here through the new UI). GREEN: avatar button + menu.

**U1.4 — `Activity.tsx` and the shared feed-rendering extraction.** RED: `Activity.tsx` renders the
same rows `AgentFeed` renders today (agent name, relative time, one line of text, no boxed/badged
styling per the design), given the same `logs` prop shape. GREEN: extracted shared component,
consumed by both `Activity.tsx` and (in Sprint 3) the Board's rail.

**U1.5 — `NavBar.tsx` deleted.** Not a RED/GREEN task — a deletion, done only once U1.1–U1.4 are
green and nothing imports `NavBar` anymore (confirmed by a repo-wide search before deleting, not
assumed).

### Definition of Done (expected completion)

- Every authenticated route renders inside `[SideBar | Outlet]`. `NavBar.tsx` no longer exists.
- The sidebar has three icon nav items, each with a real accessible name, each passing its own
  `vitest-axe` check.
- `/activity` exists and renders live agent activity full-height, sharing its row-rendering with
  the Board's own rail (built in Sprint 3, consuming the same extracted component).
- Sign-out works from the new avatar menu; `AuthProvider.test.tsx` is unaffected.
- `Navigation.test.tsx` passes with zero changes to what it asserts (only, if needed, to *how* an
  element is queried, never to the expected behavior).

### Prerequisites and what this unblocks

- Depends on: Sprint 0 (`Button`, tokens, `vitest-axe`, reduced-motion pattern, Phosphor icons).
- Unblocks: Sprints 2–4, all of which render inside this shell (Login is the one exception —
  see Sprint 2 — it renders outside the authenticated shell entirely, matching today's behavior).

### Code review findings (2026-08-14) — PR #53

Manual review posted directly to the PR as inline comments (high effort, 8 finder angles,
verified pass) — see
[review #4934165150](https://github.com/AndrewVanDelden/TaskFlow/pull/53#pullrequestreview-4934165150)
for the full text, each comment anchored to its exact line. No prior review on this PR to
cross-check. **Status: RESOLVED — all 4 findings fixed, verified via a fresh `.\test` run (backend
414/414, frontend 42/42 files, 217/217 tests), and merged to `develop` in PR #53:**

1. `App.tsx:35` (CONFIRMED, FIXED — 89e2588) — outer shell changed from `min-h-screen` to
   `h-screen`, so the fixed-height `SideBar` no longer scrolls out of view on tall pages. Verified
   via `getComputedStyle` (a real, non-flaky check — `h-screen`/`min-h-screen` set genuinely
   different CSS properties, `height` vs `min-height`).
2. `SideBar.tsx:33` (PLAUSIBLE, FIXED — 8c6d941) — added `aria-haspopup`/`aria-expanded`, and a
   `useLocation`-driven effect that closes the menu on route change (the actual correctness bug,
   since `SideBar` isn't remounted on navigation). **Deliberately not added**: outside-click/Escape
   dismissal — scoped out as separate UX polish, not the correctness bug the review found.
3. `formatting.ts:11` (PLAUSIBLE, FIXED — e6ffe6a) — `formatRelativeTime` now clamps explicitly via
   `Math.max(0, ...)` instead of relying on the accidental fallthrough that already produced
   `'just now'` for negative diffs. Behavior is unchanged by design (this is an agent-activity feed;
   clock-skew-as-"just now" is correct) — the fix makes the intent real, tested code.
4. `AgentFeedList.tsx:15` (PLAUSIBLE, FIXED — 8e0aad7) — `log.details ?? log.action` changed to
   `log.details || log.action`, so an empty-string `details` now falls back to `action` too.

### Post-sprint retrospective (2026-08-14)

- **`AgentFeed.tsx` (Board's boxed component) was deliberately left untouched**, exactly as this
  sprint's own U1.4 GREEN text scoped it — the extraction produced a new `AgentFeedList.tsx`
  consumed only by `Activity.tsx` here; Dashboard/Board keeps its current boxed rendering until
  Sprint 3 (U3.6) does the actual swap. Recorded so it isn't mistaken for a missed task.
- **`formatRelativeTime` added to `lib/formatting.ts`**, new scope not explicitly named as a file to
  create anywhere in this doc — the design's own "relative time" requirement for feed rows had no
  existing helper (`formatTime` is absolute, e.g. `3:45:00 PM`). Takes an optional `now` param so
  tests stay deterministic without mocking the system clock.
- **`Login.test.tsx`'s "signs in and stores the session" test needed reconciling**, not anticipated
  by this sprint's Definition of Done (which only covers `Navigation.test.tsx` staying unchanged).
  It asserted visible `"Ada"` text, written against `NavBar`'s header which displayed the username
  in plain text. The new icon-only `SideBar` has no visible username text anywhere by design (every
  control's accessible name comes from `aria-label`), so that assertion started failing — correctly,
  not a bug in the new shell. Replaced with a `heading`-role assertion that the Dashboard actually
  rendered, plus a direct `localStorage` check for the persisted username, which is a more precise
  test than inferring the session succeeded from incidental UI text.

---

## Sprint 2 — Login

### Why this sprint exists, and why it's second

Login is fully independent of the authenticated shell (it renders outside `ProtectedLayout`,
exactly as it does today) and touches the smallest, most self-contained surface in this epic —
the right place to prove the Sprint 0 primitives and token set actually work end to end, before
committing to the more complex, stateful Board rewrite.

### Goal

Restyle `Login.tsx` into the split brand-pane + form-pane card, keeping the existing toggle
mechanism and its tested behavior, reconciling copy per the epic-wide decision above.

### Files involved

- `TaskFlow.Web/src/features/Login.tsx` (edit)
- `TaskFlow.Web/src/features/Login.test.tsx` (edit — the one deliberate, named copy-query update
  decided above; every other assertion unchanged)

### Decisions owned here, before dispatching any engineer

- **Toggle copy, locked (restated from the epic-wide decision):** sign-in mode's trigger reads
  "Create an account"; register mode's trigger reads "Already have an account? Sign in." Submit
  button text is unchanged ("Sign in" / "Create account") — `Login.test.tsx`'s two
  `getByRole('button', { name: /sign in/i })` and `{ name: 'Create account' }` assertions need no
  change; only the trigger-button query (`/need an account\? register/i` → `/create an account/i`)
  is updated, and this line item is the record of that change.
- **Brand pane content is static copy plus three "live" teaser lines** (per the design: "Executor
  tailoring Anthropic resume…", "Notion application ready for review", "2 applications submitted
  today"). Locked: these are **static placeholder copy, not a real data feed**, for this sprint —
  wiring them to live SignalR data is explicitly out of scope here (an unauthenticated screen has
  no session to subscribe with) and is not implied by anything in the design handoff, which shows
  them as static prototype content.
- **Field styling** (`bg-[#232532] border border-white/10 rounded-lg h-10`, label
  `text-xs text-[#9397ab]` — `neutral-500`, already contrast-correct) reuses Sprint 0's token set
  directly; no new tokens needed here.

### Tasks

**U2.1 — Split-card layout.** RED: the login screen renders a brand pane and a form pane as
described; existing field queries (`getByPlaceholderText('Email')`,
`getByPlaceholderText('Password')`) still resolve. GREEN: restyled `Login.tsx` layout.

**U2.2 — Toggle copy reconciliation.** RED: `Login.test.tsx`'s updated toggle-trigger query passes
in both directions (sign-in → register → back to sign-in), and the submit-button assertions are
unaffected. GREEN: updated trigger copy per the decision above.

**U2.3 — Accessibility pass.** RED: `vitest-axe` reports zero violations on both the sign-in and
register states of the form pane. GREEN: any labeling/contrast fix the axe run surfaces.

### Definition of Done (expected completion)

- Login matches the split brand+form reference (3b), using Sprint 0's tokens and `Button`.
- The existing toggle mechanism and every one of its current test assertions still pass, with the
  one named copy-query update recorded above.
- Zero `vitest-axe` violations on both form states.

### Prerequisites and what this unblocks

- Depends on: Sprint 0 (tokens, `Button`, `vitest-axe`).
- Unblocks: nothing downstream — Login is a leaf in this epic's dependency graph, which is exactly
  why it's sequenced early, as a low-risk proof that the foundations work.

### Code review findings (2026-08-14) — PR #54

Manual review posted directly to the PR as inline comments (high effort, 8 finder angles,
verified pass) — see
[review #4934428666](https://github.com/AndrewVanDelden/TaskFlow/pull/54#pullrequestreview-4934428666)
for the full text, each comment anchored to its exact line. No prior review on this PR to
cross-check. **Status: RESOLVED — all 4 findings fixed, verified via a fresh `.\test` run (backend
414/414, frontend 42/42 files, 224/224 tests), and merged to `develop` in PR #54:**

1. `Login.tsx:54` (CONFIRMED, FIXED — e81adc5) — all four spots mapped to Nocturne tokens
   (`slate-300/400/500` → `textNeutral300`/`textNeutral400`/new `placeholderNeutral500`,
   preserving the original relative hierarchy).
2. `Login.tsx:51` (PLAUSIBLE, FIXED — e81adc5) — added a complementary `md:hidden` wordmark in the
   form pane, mirroring the brand pane's `hidden md:flex`; exactly one is visible at any width.
3. `Button.test.tsx:63` (PLAUSIBLE, FIXED — 4b79845) — now asserts
   `getComputedStyle(button).opacity` directly. Caught a real fact wrong along the way: Tailwind
   v4's `opacity-50` compiles to `opacity: 50%` (percentage syntax), not the `0.5` decimal first
   assumed — a genuine, verified value, not a jsdom quirk.
4. `Button.tsx:10` (nit, FIXED — 4b79845) — eliminated the composite `focusRingClasses` constant
   rather than renaming it; `focusRingAccent` and a new, precisely-named `disabledClasses` are
   composed directly, so each name matches its content exactly.

### Post-sprint retrospective (2026-08-14)

- **All three tasks (U2.1/U2.2/U2.3) were built as one unit, not delegated to separate parallel
  agents** — unlike Sprint 0/1's disjoint-file tasks, all three converge on the same two files
  (`Login.tsx`, `Login.test.tsx`), so splitting them would have meant multiple agents editing the
  same files concurrently. Recorded as a reusable pattern: parallelize by file ownership, not by
  the doc's own task numbering.
- **`Button` gained `disabled:opacity-50 disabled:cursor-not-allowed`**, not previously needed by
  any Sprint 0/1 consumer. Small, backward-compatible addition to the shared primitive, with its
  own test — Login's submit button is the first consumer to need a disabled state.
- **Caught during review, not anticipated by the sprint's own task list: Login's input fields still
  had the pre-Nocturne `focus:ring-blue-500` outline**, missed by the first restyle pass since the
  brief only specified button and field background/border styling, not focus-ring treatment.
  Violates Sprint 0's own locked "kill the default blue ring everywhere" rule. Fixed by extracting
  `focusRingAccent` into `lib/tokens.ts` as a shared constant (Button and Login's inputs now both
  import the same declaration, rather than duplicating the literal a second time) — this is now the
  established shared source for any future interactive element needing the accent focus ring.

---

## Sprint 3 — Board

### Why this sprint exists

The Board is the fully signed-off reference (2a/2b) and the screen users spend the most time in.
It also carries this epic's one real domain-model change (`Company`), confirmed above to not exist
anywhere today — that change is sequenced first within this sprint, before any visual work depends
on it having a value to render.

### Goal

Application-centric cards (company + role, quiet priority text, status carried by copy/type), a
single-line `ExecutorControl`, and an `Activity` rail sharing Sprint 1's extracted feed component —
with zero regression to drag-and-drop, review/approve, or export behavior.

### Files involved

- `TaskFlow.Api/Models/JobApplication.cs` (edit — add `Company` property)
- `TaskFlow.Api/Migrations/` (new migration, purely additive — no existing data to preserve,
  confirmed no production data exists per this project's own repeated confirmation elsewhere)
- `TaskFlow.Api/Ingestion/TaskDraft.cs` (edit — add `Company` property)
- `TaskFlow.Api/Ingestion/ClaudeJobPostingParser.cs` (edit — extract `Company` when present in the
  posting text; free rule-based tier may legitimately return `null`)
- `TaskFlow.Api/DTOs/` — whichever DTO(s) currently carry `Title`/`Section` through to the
  assemble/response path (audited at task time, not assumed already known — this doc does not
  claim a prior full audit of every DTO, only of `TaskDraft` and `JobApplication` directly)
- `TaskFlow.Web/src/hooks/useIntakeFlow.ts` (edit — thread `Company` through `startTailoring`'s
  assemble call)
- `TaskFlow.Web/src/types.ts` (edit — add `company` to the relevant frontend type(s))
- `TaskFlow.Web/src/components/ExecutorControl.tsx` (edit — single quiet line, no color-coded
  shell/pill)
- `TaskFlow.Web/src/components/TaskCardView.tsx` (edit — company line, quiet-text priority, kind-
  carried status, per-card progress line for in-progress items)
- `TaskFlow.Web/src/components/KanbanColumn.tsx` (edit — `ColumnHeader` primitive from Sprint 0)
- `TaskFlow.Web/src/features/Dashboard.tsx` (edit — mounts the Sprint 1 shared feed component as
  its rail, replacing the current inline `AgentFeed`)
- `TaskFlow.Web/src/lib/styles.ts` (edit — `priorityStyles`/`actionStyles` retired or reduced to
  the one allowed muted-green "done" exception, per the design's own stated allowance)

### Decisions owned here, before dispatching any engineer

- **`Company` sequencing within this sprint: domain/backend first (its own RED/GREEN pass,
  independently verified with a fresh `dotnet test` before any frontend task starts), then
  frontend.** Matches this project's own established pattern (Epic 3 Sprint 1's schema-first
  sequencing) rather than building a frontend field with nothing real behind it.
- **Company is nullable everywhere and the frontend renders a quiet placeholder (e.g. an em dash),
  never a crash or a blank layout gap, when it is `null`.** Every task created before this sprint
  ships will have `Company == null` — this is not a hypothetical edge case, it is the default state
  on day one of this feature existing.
- **Priority becomes quiet text (`High` in `accent-300`), not a filled pill — `priorityStyles` is
  retired from `TaskCardView`.** `lib/styles.ts`'s `actionStyles` (used by the Activity feed) is
  retired the same way, consistent with the Sprint 1 rail redesign already dropping boxed/badged
  rows.
- **The in-progress card's shimmering progress line and the "Live" pulse dot both use the Sprint 0
  reduced-motion pattern — no exceptions**, since these are exactly the continuous/idle animations
  that pattern was built for.
- **`ExecutorControl` keeps its existing `useExecutorControl()` hook and `toggle()` behavior
  unchanged — only the presentation changes** (one line: pulsing dot + summary text + a ghost
  Pause/Enable button pushed right, replacing the bordered/tinted card). No behavioral RED test is
  needed for the hook itself, since it isn't changing; the RED tests here are presentational
  (renders the one-line summary text, the button still calls `toggle()`).

### Tasks

**U3.1 — `JobApplication.Company` (domain/backend).** RED: persisting a `JobApplication` with a
`Company` value round-trips it; a `TaskDraft` produced by `ClaudeJobPostingParser` from a posting
containing a clear employer reference includes the extracted company name; the same posting run
through the free rule-based tier is allowed to return `null` without failing the test (asserted as
an allowed outcome, not a required one). GREEN: entity field, migration, parser extraction, DTO
plumbing confirmed end-to-end from parse → assemble → persisted `JobApplication.Company`.

**U3.2 — Frontend `company` field, threaded through.** RED: `useIntakeFlow`'s `startTailoring` call
includes `company` when the parsed draft has one; `types.ts`'s relevant type gains `company:
string | null`. GREEN: hook and type updates.

**U3.3 — Application-centric `TaskCardView`.** RED: a card renders the company name (or the quiet
placeholder when `null`), quiet-text priority (no filled pill), and no `assignedToName` line where
company now lives. GREEN: restyled card markup.

**U3.4 — In-progress and Review card states.** RED: an in-progress card shows the status line
("Tailoring resume…") and a progress-line element carrying the reduced-motion pattern from Sprint
0; a Review-column card renders the existing `ReviewActions` unchanged, wrapped in the new visual
shell. GREEN: state-specific card styling, existing `onApprove`/`onReject` wiring untouched.

**U3.5 — Single-line `ExecutorControl`.** RED: renders the pulsing-dot summary line and a Pause/
Enable button that still calls the existing hook's `toggle()`; the old tinted-card shell and
colored pill are gone. GREEN: restyled `ExecutorControl.tsx`, hook untouched.

**U3.6 — Board rail shares Sprint 1's `Activity` component.** RED: `Dashboard.tsx`'s aside renders
the same shared feed component `Activity.tsx` renders, at the design's 300px rail width. GREEN:
`Dashboard.tsx` swaps its inline `AgentFeed` usage for the shared component.

**U3.7 — Accessibility and drag-regression pass.** RED: `vitest-axe` reports zero violations on
each column and card state; existing dnd-kit drag tests (`KanbanBoard.test.tsx`,
`KanbanColumn.test.tsx`) pass unchanged, proving the restyle didn't disturb `SortableContext`/drop
semantics. GREEN: any fix the axe run surfaces; no drag-logic changes expected or permitted in this
sprint.

### Definition of Done (expected completion)

- The Board matches the signed-off reference (2a/2b): quiet single-line executor status,
  application-centric cards with company + role, priority as quiet text, status carried by
  type/copy, no red/amber/green.
- `JobApplication.Company` exists, is populated best-effort by ingestion, and renders correctly
  (including its `null` placeholder state) on every existing and new task.
- The Board's activity rail and the standalone `/activity` page share one implementation.
- Every existing drag-and-drop, review/approve, and export test still passes, unchanged.
- Zero `vitest-axe` violations across all column/card states.

### Prerequisites and what this unblocks

- Depends on: Sprint 0 (primitives, tokens, reduced-motion, `vitest-axe`), Sprint 1 (the shared
  `Activity` feed component this sprint's rail consumes).
- Unblocks: nothing downstream within this epic — Sprint 4 does not depend on the Board's visual
  state, only on the shell (Sprint 1) and the shared primitives (Sprint 0).

### Code review findings (2026-08-14) — PR #55

Manual review posted directly to the PR as inline comments (high effort, 8 finder angles,
verified pass) — see
[review #4939746497](https://github.com/AndrewVanDelden/TaskFlow/pull/55#pullrequestreview-4939746497)
for the full text, each comment anchored to its exact line. No prior review on this PR to
cross-check. **Status: FIXED — both findings resolved 2026-08-14:**

1. `JobApplicationAssemblyService.cs:44` (CONFIRMED) — `TaskItem.SourceSection` is now always
   empty for job-posting-sourced tasks (Section moved to `string.Empty`, Company only reaches
   `application.Company`), but `TailoringAgentBase.FormatJobPosting` (untouched by this PR) still
   built the Claude prompt's "Company: {SourceSection}" line from `SourceSection` — so tailoring
   agents silently stopped telling Claude which company a posting is for. Existing tailoring-agent
   tests masked this because they hand-set `SourceSection` directly, bypassing the real pipeline.
   **Fix:** `TailoringAgentBase.BuildPrompt`/`FormatJobPosting` now take the already-loaded
   `JobApplication` and read `application.Company` instead of `task.SourceSection`. RED-first:
   `ResumeTailoringAgentTests`/`CoverLetterAgentTests`' shared `SeedApplicationAsync` helper now
   seeds `JobApplication.Company` (not `TaskItem.SourceSection`), matching the real pipeline, and
   `Wraps_the_job_posting_in_the_initial_prompt...` asserts a `Company: <name>` line inside the
   wrapped `<job_posting>` block; both failed (`found -1`) before the fix, pass after.
2. `Dashboard.tsx:9` (PLAUSIBLE) — the Board's Live/Offline SignalR-connection indicator was
   dropped entirely; `connected` was no longer read from `useAgentFeed()`, and the replacement
   `AgentFeedList` has no equivalent. **Fix:** `Dashboard.tsx` reads `connected` again and renders
   the same dot + Live/Offline text next to the Activity heading (mirroring the deleted
   `AgentFeed.tsx`'s styling), scoped to Dashboard rather than added to the shared `AgentFeedList`
   since `Activity.tsx` also consumes that component without a connection concept. RED-first:
   `Dashboard.test.tsx` switched its `useAgentFeed` mock to a controllable `vi.fn()` and added two
   tests (Live when connected, Offline when not) that failed before the fix, pass after. Verified
   live in the running app (dev server + real login) — the indicator renders correctly.

Full suite green after both fixes: backend 417/417, frontend 241/241 (up from 239).

### Post-ship fix (2026-08-14) — Board screenshot feedback

Two real usability regressions found after shipping, on `fix/board-executor-control-and-agent-status-alignment` (off `develop`, separate from this sprint's own PR since these are Sprint 3 files):

1. **`ExecutorControl`'s status dot lost any glanceable running/stopped distinction** — `bgAccent400`
   (running) vs. `bg-white/20` (paused, near-invisible). Fixed to match this same Board screen's own
   existing vocabulary rather than inventing a third one: `bg-emerald-400` (running, pulsing) matches
   `AgentStatus`'s "Running" pill and Dashboard's "Live" connection dot; `bg-slate-500` (paused, solid)
   matches `AgentStatus`'s "Idle" dot. No red anywhere — no adjacent precedent for it on this screen,
   and "paused" isn't an error state.
2. **`ExecutorControl` renders full-width in Dashboard's `<main>`, above the two-column split** — the
   old bare `flex-1` row stretched the whole page width, stranding the Pause/Enable button far from
   its own status text. Fixed by capping the row at `max-w-sm`.
3. **`AgentStatus`'s two agent cards didn't vertically align** — "Stale Task Detector" wraps to two
   lines while "Task Prioritizer" fits one, and since the cards size independently in a `grid-cols-2`
   layout with no shared row height, that extra line pushed everything below it (the stats rows) down
   relative to the shorter card. Fixed with `truncate` (plus `min-w-0` on the flex parent, required
   for truncate to engage inside a flex item) so both headers stay exactly one line regardless of
   label length.

All three RED-tested first, verified live in the running app (capped row width, emerald/slate dot
colors, and identical `Actions logged` row `top` position between both cards, confirmed via direct
DOM measurement at a real 1280px desktop viewport). Full suite green: backend 424/424, frontend
245/245.

### Post-sprint retrospective (2026-08-14)

- **Real tooling-boundary checkpoint, confirmed not hypothetical: an EF Core migration was actually
  required.** Backend tests use two different DB-provisioning paths — unit tests via
  `SqliteInMemoryContext.EnsureCreated()` (builds schema straight from the live model, no migration
  needed) and integration tests via `TestWebAppFactory` running real `Program.cs` startup, which
  calls `Database.Migrate()` and throws `PendingModelChangesWarning` if the model and migration
  history disagree. Checking only the first path before starting led to a wrong initial assumption
  that no migration was needed — caught for real (not by re-reasoning) when the first post-U3.1
  `.\test` run came back with 48 failures, all the identical error. `dotnet ef migrations add` isn't
  covered by the standing `.\test`-only self-run permission, so this was handed to the user
  ("done" — user ran it); the resulting migration is purely additive, matching what was expected.
- **Both job-posting parsers already extracted a company name before this sprint** — they were
  smuggling it through `TaskDraft.Section` (meant for document-section provenance) for lack of a
  real field. `TaskDraft.Company` was added as an *optional trailing* record parameter (default
  `null`) specifically so every unrelated 4-arg call site (`ClaudeIngestionParser`,
  `SpecDocumentParser`, and their tests) kept compiling unchanged. Two existing parser tests
  (`JobPostingParserTests`, `ClaudeJobPostingParserTests`) needed a deliberate, named reconciliation
  from asserting company-in-Section to company-in-Company — their own names already said "extracts
  title and company," so this was always their real intent.
- **`TaskResponseDto` (not `JobApplicationResponseDto`) is what actually reaches the Board's
  cards** — traced end-to-end via the existing `task.Application?.State` → `ApplicationState`
  precedent before writing any code, not assumed from the epic doc's own "audited at task time" note.
- **`TaskItem.company`/`TaskDraft.company` made optional** (`company?: string | null`), mirroring
  this codebase's own existing `applicationState?` precedent and its stated reason verbatim —
  existing fixtures across `KanbanColumn.test.tsx`/`TaskCard.test.tsx`/`ApplicationReviewCard.test.tsx`
  don't need updating.
- **`ExecutorControl`'s summary line uses only `enabled`/`busy`, not the design mockup's task-count
  example** ("2 working · 3 queued · 6 done today") — that data isn't available from
  `useExecutorControl()`, and the epic's own locked decision says the hook stays unchanged. Adding
  counts would have meant quietly expanding scope into hook changes explicitly ruled out.
- **Done-column's muted/dimmed treatment and "Approved · exported" checkmark line (from the raw
  design mockup) were deliberately not built** — neither is in this epic doc's own locked U3.3/U3.4
  task text, only in the source design's Story 2 description, so building it would have been
  scope invented beyond what was actually asked for here.
- **Board rail kept at its existing 360px, not the design's descriptive 300px** — `AgentStatus`'s
  two-card `grid-cols-2` layout risks real wrapping at 300px with no test to catch it, and 300px
  isn't a locked, RED-tested requirement anywhere in this sprint's actual task list.
- **A genuine cross-cutting gap, caught by an agent and correctly escalated rather than silently
  patched**: `TaskCardView` now calls `usePrefersReducedMotion()` unconditionally (Rules of Hooks),
  and jsdom has no real `window.matchMedia` — every test rendering `TaskCardView`, directly or via
  `TaskCard`/`KanbanColumn`/`KanbanBoard`'s `DragOverlay`, would throw. Fixed once, centrally: a
  default `mockPrefersReducedMotion(false)` in `test/setup.ts`'s `beforeEach`, benefiting every
  current and future animated component's tests, not a per-file patch repeated three times.
- **`TaskCard.test.tsx` had its own separate `'Unassigned'` assertion** neither the TaskCardView
  restyle task nor its own test file's brief covered (different file, missed in initial planning) —
  caught by running the full suite, not by re-reading the brief, and fixed with the same deliberate
  em-dash reconciliation as `TaskCardView.test.tsx`'s own equivalent case.
- **`AgentFeed.tsx` deleted and `lib/styles.ts`'s `priorityStyles`/`actionStyles` retired**, confirmed
  via repo-wide import search after both became genuinely unused (Dashboard's U3.6 swap and
  TaskCardView's U3.3 restyle respectively) — `neutralStyle` kept, still used by `AgentStatus.tsx`.

---

## Sprint 4 — Ingest & Hand-off

### Why this sprint exists, and why it's last

This sprint carries the epic's one deliberate scope cut (URL-based parsing, descoped above) and
depends on nothing else in this epic beyond the shell and primitives — sequencing it last means
every foundational decision (tokens, reduced motion, `vitest-axe`) is already proven out on two
other screens first.

### Goal

Restyle `IngestDocument.tsx`'s existing paste-text flow to match the design's visual language
(centered column, 3-step indicator, parsed-result card with requirement chips, click-to-expand
base-resume preview) and build the "Start tailoring" hand-off square with its click animation —
**without** adding URL-based parsing, per the epic-wide decision.

### Files involved

- `TaskFlow.Web/src/features/IngestDocument.tsx` (edit)
- `TaskFlow.Web/src/components/TailorButton.tsx` (new)
- `TaskFlow.Web/src/hooks/useIntakeFlow.ts` (edit — `startTailoring` navigates to `/board` on
  success, a real behavior change, called out explicitly here rather than folded silently into the
  button's animation spec)

### Decisions owned here, before dispatching any engineer

- **The job-posting input stays a textarea, restyled to match the design's input-row visual
  language, with its existing "Parse posting" behavior unchanged.** The design's URL-input
  affordance and "Parsed from vercel.com/careers" caption are **not built** in this sprint — see
  the epic-wide descope decision. The parsed-result card (company avatar, role, comp, requirement
  chips) **is** built, sourced from whatever `ClaudeJobPostingParser` already returns plus Sprint
  3's new `Company` field — no new parser capability needed for the result card itself, only for
  the paste-then-parse flow already in place.
- **Base resume becomes a click-to-expand doc preview**, reusing the existing sanitized
  `MarkdownPreview` component (`T0.3` from Epic 3 — already sanitizes untrusted markdown, no new
  rendering path needed) for the expanded view, with a lightweight thumbnail (a small, static
  CSS-rendered card, not a real document-image render) collapsed by default. The underlying
  textarea-based capture/save behavior (`useBaseResumeCapture`, `useBaseResumeReuse`) is unchanged.
- **`startTailoring` navigating to `/board` on success is a real, intentional behavior change**
  (confirmed above: today it stays on the Ingest page). Locked as a good change, consistent with
  the design's intent — recorded explicitly here, per this project's own rule against folding a
  real behavior change silently into an animation spec.
- **The tailoring square's animation is pure CSS** (`goGlow`, `sparkFly`, `spin` keyframes per the
  design's own `<style>` block), gated by Sprint 0's reduced-motion pattern — under
  `prefers-reduced-motion: reduce`, the click still triggers navigation and pipeline kickoff
  immediately, just without the glow/sparkle/spin keyframes playing.
- **The existing generic paste/file/parse/approve flow (`useIngestion`, kept under the collapsed
  `<details>` since Epic 3 Sprint 6) is restyled to match but not otherwise touched** — same
  reasoning Sprint 6 already recorded: it's real, separately-tested capability, not something this
  epic was asked to remove.

### Implementation decisions, locked before dispatching engineers (2026-08-14)

Pulled directly from `design_handoff_ui_revamp/TaskFlow Board Revamp (standalone).html`'s own
`<style>` block and the `.tailor-square` markup (grepped and read verbatim, not paraphrased from
the EPIC.md summary), plus this doc's own established patterns from Sprints 0–3:

- **Task split, by file ownership (Sprint 0/1's parallel-by-disjoint-file precedent, not Sprint
  2's single-unit precedent — these files don't overlap):**
  - **Engineer A — U4.4 only.** `TailorButton.tsx` (new) + `useIntakeFlow.ts` (navigate-on-success)
    + their own tests. Fully self-contained; touches nothing Engineer B touches.
  - **Engineer B — U4.1 + U4.2 + U4.3 + U4.5.** `IngestDocument.tsx` + `IngestDocument.test.tsx`
    only — one unit, same file, sequential tasks, mirroring Sprint 2's reasoning for converging
    tasks. Mounts Engineer A's `TailorButton` in place of the current inline "Start tailoring"
    button, against the locked prop contract below (built and merged after Engineer A, not in
    parallel, so B's integration and full-stage `vitest-axe` pass runs against real code, not a
    guessed interface).
- **`TailorButton` prop contract, locked so both engineers can work from it independently:**
  `{ onClick: () => void; disabled: boolean; busy: boolean }` — purely presentational, no internal
  state or hook access of its own (mirrors `TaskCardView` taking fully-resolved props rather than
  deriving them). `IngestDocument` keeps owning the existing eligibility condition
  (`!intake.baseResumeText || intake.stage !== 'review'`) and passes `busy={intake.stage ===
  'starting'}`.
- **`startTailoring` navigates via `useNavigate()` called inside `useIntakeFlow` itself**, not
  passed in from the component. `useIntakeFlow` is only ever consumed by `IngestDocument`, which
  always renders under the app's router, so this is safe; it also keeps "reached building / it
  worked, go to the board" as one behavior in one place instead of splitting the stage transition
  and the navigation across two files. Real, necessary consequence: `useIntakeFlow.test.ts`'s
  `renderHook` calls gain a `MemoryRouter` wrapper — test-infrastructure work, not a workaround.
- **Animation implementation — exact values taken from the design, not invented:**
  - `spin` reuses Tailwind's **built-in** `animate-spin` utility (`spin 1s linear infinite`) — it
    is already byte-for-byte the design's own `@keyframes spin{to{transform:rotate(360deg)}}`, so
    no custom keyframe is needed for the "Tailoring…" label's spinning icon.
  - `goGlow` and `sparkFly` are **not** built into Tailwind and must be added as plain `@keyframes`
    in `TaskFlow.Web/src/index.css` (still just the global stylesheet Sprint 0 already owns — not
    a `tailwind.config.js`/theme file, so this doesn't violate the "utilities only" constraint),
    referenced via Tailwind v4 arbitrary-value utilities so no other build config changes:
    - `@keyframes goGlow { 0% { transform: scale(.2); opacity: .7 } 100% { transform: scale(3); opacity: 0 } }`
      → `animate-[goGlow_0.7s_ease-out]`
    - `@keyframes sparkFly { 0% { opacity: 1; transform: translate(-50%,-50%) scale(.3) } 60% { opacity: 1 } 100% { opacity: 0; transform: translate(calc(-50% + var(--dx)), calc(-50% + var(--dy))) scale(1.1) } }`
      → `animate-[sparkFly_0.8s_ease-out_forwards]`
  - **8 sparks**, each `SparkleIcon` or `StarFourIcon` (`@phosphor-icons/react`, already installed
    — Sprint 0), positioned via inline `--dx`/`--dy` CSS custom properties the `sparkFly` keyframe
    reads, exact values from the design's markup:

    | # | dx | dy | icon | size | color token |
    |---|---|---|---|---|---|
    | 1 | -54px | -42px | SparkleIcon | 13px | `textAccent300` |
    | 2 | 50px | -46px | SparkleIcon | 11px | `textAccent200` |
    | 3 | 60px | 22px | StarFourIcon | 12px | `textAccent300` |
    | 4 | -60px | 18px | SparkleIcon | 10px | `textAccent200` |
    | 5 | -22px | -62px | StarFourIcon | 10px | `textAccent200` (see substitution note) |
    | 6 | 28px | 58px | SparkleIcon | 12px | `textAccent300` |
    | 7 | -32px | 54px | SparkleIcon | 11px | `textAccent200` |
    | 8 | 14px | -60px | StarFourIcon | 9px | `textAccent300` |

    **Substitution note (spark #5):** the design uses a bare `accent-100` for this one spark, which
    has no equivalent in this codebase's locked token set (`lib/tokens.ts` only defines
    accent-200/300/400/500/700/800). Rather than adding a ninth token for one decorative spark,
    it's substituted with `textAccent200` — a deliberate, recorded downgrade, not a silent drop.
  - Trigger is `busy` (see prop contract above), **not** the design's CSS-only checkbox-input hack
    — real component state replaces that static-prototype trick.
  - Gated by `usePrefersReducedMotion()` (existing hook, Sprint 0/3 precedent — `TaskCardView`'s
    progress-line pulse is the direct precedent here): when `true`, the `animate-[goGlow...]`,
    `animate-[sparkFly...]`, and `animate-spin` classes are all omitted entirely — `busy`'s label
    swap ("Start tailoring" → "Tailoring…") and the click's `onClick` call are unaffected, per the
    epic-wide rule that triggered animations collapse to an instant state change under reduced
    motion, never removed functionality.
- **3-step indicator labels are this doc's own choice, not lifted from the design** (a quick,
  reasonable search of the extracted prototype turned up no literal step-label copy near the 3a
  section — not worth further excavation for non-load-bearing presentational text): **"1 Provide"
  → "2 Review" → "3 Generate"**, mapped from `useIntakeFlow`'s stage
  (`provide`/`parsing` → step 1, `review`/`starting` → step 2, `building` → step 3), current step
  marked with `aria-current="step"` (a real, standard ARIA pattern for step indicators, not
  invented here).
- **"Requirement chips" on the parsed-result card are explicitly NOT built — a real, deliberate
  scope cut, not an oversight.** Checked directly: `TaskDraft` (`TaskFlow.Api/Ingestion/TaskDraft.cs`
  and its frontend mirror in `types.ts`) has exactly `title`, `description`, `kind`, `section`,
  `company` — no structured requirements list of any kind, and a repo-wide search of the design
  prototype's own markup for a "chip" component near the Ingest section turned up nothing either
  (only unrelated icon-font glyph names). Rendering "requirement chips" would mean fabricating
  data that was never parsed — this epic's own standing rule elsewhere (never invent scope, never
  fabricate) applies exactly here. **The parsed-result card shows title (role), company (with the
  same em-dash `—` quiet-placeholder convention `TaskCardView.tsx` already established for a null
  company), and description** — real fields only, sourced exactly as the epic doc's own Decisions
  section already said ("whatever `ClaudeJobPostingParser` already returns plus Sprint 3's new
  `Company` field — no new parser capability needed").
- **`TailorButton` replaces both the current `stage === 'review'` button block and the separate
  `stage === 'starting'` "Starting…" paragraph** — `TailorButton` renders across both stages
  (`busy={intake.stage === 'starting'}`) and its own "Tailoring…" label already communicates the
  busy state, so the standalone paragraph is redundant once it's wired in, not a second thing to
  keep in parallel.
- **The click-to-expand base-resume preview is additive, not a replacement of the existing
  textarea.** The Sprint 4 Definition of Done's own phrasing ("not a raw textarea") is read
  against the more precise, binding U4.3 task text and this sprint's own locked "underlying
  textarea-based capture/save behavior is unchanged" decision above: you cannot edit resume text
  through a rendered Markdown preview, so the `id="base-resume"`/`<label>` textarea pair stays
  exactly as-is (existing tests type into it via `getByLabelText(/base resume/i)` and must keep
  passing unchanged) — the collapsed-thumbnail/expand-to-`MarkdownPreview` piece is a new sibling
  element next to it, not a swap.

### Tasks

**U4.1 — Restyled 3-step Ingest layout.** RED: the existing stage-model assertions
(`IngestDocument.test.tsx`'s placeholder/label queries) pass against the restyled markup; the
3-step indicator reflects `useIntakeFlow`'s current stage. GREEN: restyled layout, no stage-model
changes.

**U4.2 — Parsed-result card.** RED: after a successful parse, the parsed title/section/description
plus (when present) `Company` render as a card with requirement-style chips. GREEN: result-card
component consuming the existing `intake.drafts[0]` shape plus Sprint 3's `Company` field.

**U4.3 — Click-to-expand base resume preview.** RED: the base-resume section renders a collapsed
thumbnail summary by default; clicking it expands a `MarkdownPreview`-rendered full view of the
current `baseResumeText`; the underlying save/reuse behavior is unchanged. GREEN: expand/collapse
UI wrapping the existing `MarkdownPreview` component.

**U4.4 — `TailorButton` and its animation.** RED: clicking the square with `startTailoring`
eligible (non-empty base resume, `review` stage) triggers the existing `startTailoring()` call and,
on success, navigates to `/board`; under mocked `prefers-reduced-motion: reduce`, the same click
still navigates but without the animation's keyframe classes applied. GREEN: `TailorButton.tsx`,
`useIntakeFlow.ts`'s navigation addition.

**U4.5 — Accessibility pass.** RED: `vitest-axe` reports zero violations across the provide/review/
building stages; the click-to-expand resume preview is keyboard-operable and exposes its expanded
state to assistive tech (native `<details>` or an equivalent ARIA-disclosure pattern). GREEN: any
fix the axe run surfaces.

### Definition of Done (expected completion)

- Ingest matches the signed-off reference (3a) for everything **except** URL-based parsing, which
  is explicitly not built — the paste-text flow is restyled to the same visual language instead.
- The base resume renders as a click-to-expand document preview, not a raw textarea, while keeping
  its existing save/reuse behavior intact.
- The tailoring square behaves as one button: click triggers the existing create-application flow,
  navigates to the Board on success, and its animation respects reduced motion.
- The existing generic-document flow is restyled but behaviorally unchanged.
- Zero `vitest-axe` violations across every intake stage.

### Prerequisites and what this unblocks

- Depends on: Sprint 0 (primitives, tokens, reduced-motion, `vitest-axe`), Sprint 1 (shell/routing
  the navigate-to-`/board` call lands on).
- Unblocks: nothing further within this epic — this is the last sprint.

### Code review findings (2026-08-16) — PR #57

Manual review posted directly to the PR as inline comments (high effort, 8 finder angles,
verified pass) — see
[review #4946723549](https://github.com/AndrewVanDelden/TaskFlow/pull/57#pullrequestreview-4946723549)
for the full text, each comment anchored to its exact line. No prior review on this PR to
cross-check. **Status: FIXED — both findings resolved 2026-08-16:**

1. `IngestDocument.test.tsx:216` (conventions) — a comment claimed the old `IntakeProgress` render
   branch was "kept, not deleted" and that `IntakeProgress.test.tsx` still covers it; this same PR
   deletes both, so the comment was factually wrong. **Fix:** corrected the comment to state plainly
   that `IntakeProgress.tsx`, its test, and the render branch are all deleted outright, confirmed
   dead code with zero remaining consumers.
2. `IngestDocument.tsx:149` (simplification) — the parsed-result card's Section paragraph was dead
   code: Sprint 3 (PR #55) made `Section` always empty for job-posting-sourced drafts, so this
   conditional never rendered in the flow it's part of. **Fix:** removed the paragraph. This broke
   one existing test that only passed because the shared MSW fixture's stale `section: 'Job
   Posting'` value happened to render there — reconciled by name (not silently patched): the test
   now asserts on `parsed-company`/description only, matching what the real pipeline actually
   threads through. Confirmed via a RED run that removing the dead render broke exactly that one
   assertion and nothing else, before fixing the test.

Full suite green after both fixes: backend 424/424, frontend 262/262.

### Post-sprint retrospective (2026-08-14)

- **A real, discovered consequence of U4.4's navigate-on-success, not invented scope: `IntakeProgress`
  became dead code and was deleted.** `startTailoring()` calls `setStage('building')` immediately
  followed by `navigate('/board')`, both synchronous (no `await` between them) — React 18's
  automatic batching means these land in one commit, and since the navigation swaps `<Routes>` at
  an ancestor of `IngestDocument`, that commit unmounts `IngestDocument` rather than ever painting
  it with `stage === 'building'`. Verified empirically (a scratch test proved the pre-existing
  "renders live per-item progress rows once building is reached" test only passed if changed to
  assert navigation instead), not assumed from reading the code alone. Confirmed via a repo-wide
  `IntakeProgress` search that `IngestDocument.tsx` was its only consumer, then deleted
  `IntakeProgress.tsx`/`IntakeProgress.test.tsx` outright along with the now-unreachable `stage ===
  'building'` branch and the `useAgentFeed` subscription that only fed it — full scoped suite
  (47/47) still green after the deletion. **This is judged a genuine improvement, not a
  regression**: Sprint 3 (U3.4) already built equivalent live per-task progress directly on the
  Board (`TaskCardView`'s in-progress shimmer + "Tailoring {kind}…" line), so a user who lands on
  the Board immediately after starting tailoring sees real, live progress there — a bespoke
  progress widget on the page they've already left would have been redundant, not missing
  functionality.
- **"Requirement chips" (from the original design mockup's parsed-result card) were not built.**
  Checked directly, not assumed: `TaskDraft` (backend and its `types.ts` mirror) has no structured
  requirements field, and a search of the design prototype's own markup near the Ingest section
  found no real chip data source either. Building them would have meant fabricating data that was
  never parsed — descoped explicitly, recorded in this sprint's own "Implementation decisions"
  above before any engineer started, not discovered after the fact.
- **Two-engineer split by file ownership (Sprint 0/1's precedent, not Sprint 2's) worked cleanly**:
  Engineer A's `TailorButton.tsx`/`useIntakeFlow.ts` slice and Engineer B's `IngestDocument.tsx`
  slice never touched the same file, and B integrated against A's already-finished, verified work
  (not a guessed interface) since the two were run sequentially, not in parallel, specifically
  because B's integration and full-stage `vitest-axe` pass depended on A's component actually
  existing.
- **`useIntakeFlow.test.ts`'s `renderHook` calls all needed a real `MemoryRouter` wrapper** once
  `startTailoring` started calling `useNavigate()` internally — written with `React.createElement`
  rather than JSX since the file is `.test.ts`, not `.test.tsx` (matching the path this doc itself
  specifies), proven equivalent to a JSX version.
- **`stepForStage` (U4.1's stage→step mapping) was extracted to a new `lib/intakeSteps.ts`**, not
  named as a file to create anywhere in this doc — `eslint`'s `react-refresh/only-export-components`
  rejects a non-component export from a component file, and this codebase's own `lib/board.ts`
  already established the "small pure helpers live in `lib/`" convention this follows.
- **Independently verified, not just trusted**: both engineers' diffs were read directly (not just
  their self-reports), their scoped tests re-run independently by the architect pass, `tsc -b`
  checked (surfaced only a pre-existing, repo-wide `vitest-axe` type-declaration gap affecting
  every axe test file, not something either engineer introduced), and the dead-code consequence
  above was verified by direct repo search before deleting anything.

---

## Epic close-out (before merging to main)

Mirrors Epic 3's own pre-merge review step (`develop → main`, PR #50) — done once, after every
sprint above has independently shipped and its own suite is green:

1. Run the full `.\test` suite one final time from a clean `develop` checkout; confirm the result
   in `test-results.txt` before doing anything else in this checklist.
2. Re-read every sprint's Definition of Done above against the real, merged code — not against
   memory of having built it.
3. Confirm `design_handoff_ui_revamp/` has nothing in it that isn't already reflected somewhere in
   this doc or the shipped code (a final diff-against-the-handoff pass), then **delete the folder**
   (`design_handoff_ui_revamp/EPIC.md` and the standalone prototype HTML) in its own commit.
4. Confirm no dead code remains from the migration — `NavBar.tsx`, the old `priorityStyles`/
   `actionStyles` maps (if fully retired), and any other file this epic's sprints explicitly marked
   for deletion.
5. Open the `develop → main` PR; run an independent manual review before looking at any automated
   reviewer's comments, then compare, per this project's established two-pass review habit.

### Close-out audit (2026-08-16) — checklist items 1–3

Ran against a fresh `develop` checkout (full suite green: backend 465/465, frontend 309/309) before
anything else, per item 1's own instruction. Findings below are **documentation only** — nothing in
this section has been fixed yet; each is either a real gap needing a decision, or a stale doc claim
corrected to match a real, already-approved decision made elsewhere in this session.

#### 1. Definition of Done, re-read against real code

**Real functional gap — Sprint 0's "no screen hand-rolls its own button" is violated:**
- `ReviewActions.tsx` hand-rolls two `<button>`s (`bg-emerald-600`/Approve, `bg-red-600`/Reject)
  instead of the shared `Button` primitive.
- `IngestDocument.tsx` hand-rolls five `<button>`s (`bg-blue-600`, e.g. "Parse posting", "Save base
  resume") the same way.
- **Not a mechanical swap-in**: the shared `Button` component (Sprint 0) only has two variants,
  `primary` (accent outline) and `ghost` (neutral) — neither carries the positive/negative
  semantic `ReviewActions` actually needs (Approve vs. Reject must read as visually distinct at a
  glance, the same problem Sprint 4/PR #58's ExecutorControl work already ran into for
  running/paused). Fixing this means either a real design decision (new `Button` variants, e.g.
  `success`/`danger`) or accepting Approve/Reject lose their color distinction under
  `primary`/`ghost` — an open question, not something to silently decide mid-audit.

**Real functional gap — Sprint 4's Ingest restyle is incomplete:**
- Only the pieces Sprint 4 explicitly built new (3-step indicator, parsed-result card,
  `TailorButton`, click-to-expand preview) use Nocturne tokens (`bgSurface`, `borderDivider`,
  `textNeutral400/500`, `textAccent200`).
- Everything else in `IngestDocument.tsx` — both textareas, the "Parse posting"/"Save base resume"
  buttons, their labels, both file inputs, and the entire generic-document `<details>` flow — is
  still stock pre-Nocturne Tailwind (`bg-slate-900`, `bg-blue-600`, `text-slate-400`,
  `border-slate-700/800`), confirmed by direct grep (18 old-style class occurrences vs. one file
  importing Nocturne tokens at all).
- **This also means the old blue focus ring is still present in 5 places**
  (`focus-visible:ring-2 focus-visible:ring-blue-500` on the stage heading, the "Use previously
  saved base resume" link, both `<details>` summaries, and the shared file-input class string) —
  a direct violation of Sprint 0's own global rule ("kill the default blue ring everywhere"), the
  same exact bug Sprint 2's review already caught and fixed once for `Login.tsx` but which was
  never applied to `IngestDocument.tsx`.
- Net effect: Sprint 4's own Definition of Done ("Ingest matches the signed-off reference (3a) for
  everything except URL-based parsing") is **not accurate** — the page currently reads as a
  patchwork of new-Nocturne and old-stock-Tailwind pieces, not a fully restyled screen.

**Fidelity gap vs. Sprint 4's own locked decision:**
- Sprint 4's "Implementation decisions" section (above) commits to "a lightweight thumbnail (a
  small, static CSS-rendered card, not a real document-image render), collapsed by default" for
  the base-resume preview. What actually shipped is a plain `<summary>Preview base resume</summary>`
  text link — no card, no thumbnail. Smaller gap than the two above, but a real mismatch between a
  locked decision and the shipped code, not just an unmet aspiration from the original design.

**Stale doc text, not gaps — corrected here for accuracy, no code changed:**
- Sprint 1's DoD says "the sidebar has three icon nav items" — it now has four (`Archive`, added by
  PR #59 after Sprint 1 shipped, a legitimate later addition this doc's Sprint 1 section predates).
- Sprint 3's DoD says "no red/amber/green... status carried by type/copy" — but three places on the
  Board now deliberately use emerald: `AgentStatus`'s pre-existing "Running" pill (never in Sprint
  3's own scope to begin with — see the handoff-diff finding below), the "Live" connection dot
  (added fixing PR #55's own review finding), and `ExecutorControl`'s running/paused dot (added in
  PR #58, explicitly matching the other two after screenshot feedback that the original
  all-neutral scheme made running/paused indistinguishable at a glance). This is a real, deliberate,
  already-approved evolution of the "no red/amber/green" rule — it needs a **locked exception**
  recorded here (mirroring the epic-wide DoD's own existing "one muted green check for Approved"
  exception), not a silent contradiction left standing.

#### 2. `design_handoff_ui_revamp/` diff pass

Re-read `EPIC.md` in full against the shipped code. Confirmed still accurate/already recorded:
URL-based parsing and requirement chips are the two deliberate, already-documented scope cuts;
Login, the Board's card/executor/rail restyle, and the app shell otherwise match the handoff
faithfully.

**One real gap found**: `EPIC.md`'s own "Screen ↔ file map" explicitly lists `AgentStatus.tsx` as
a Board file requiring the Nocturne restyle (alongside `Dashboard.tsx`, `KanbanBoard.tsx`,
`KanbanColumn.tsx`, `TaskCardView.tsx`, `ExecutorControl.tsx`, `AgentFeed.tsx`) — but Sprint 3's own
"Files involved" list (this doc, above) never included it, and it was never touched. This is the
direct root cause of the Sprint 3 DoD contradiction above: `AgentStatus`'s emerald "Running" pill
was never brought into the "no red/amber/green" pass because the sprint that was supposed to do it
was never scoped to include the file the original handoff named. Resolved below (decision 3) as a
locked exception, not a fix. The folder itself (`design_handoff_ui_revamp/EPIC.md` + the standalone
prototype HTML) has **not** been deleted yet — holding it until the `IngestDocument.tsx` restyle
(decision 2 below) is actually implemented, since it's still the reference for exactly what
"matches the signed-off reference" means for that work. Delete it once that lands, not before.

#### 3. Dead-code sweep

Clean — nothing further found. `NavBar.tsx`, `AgentFeed.tsx`, and `IntakeProgress.tsx` are all
confirmed deleted (repo-wide search, zero remaining references); `priorityStyles`/`actionStyles`
are confirmed retired from `lib/styles.ts` (only `neutralStyle` remains, still used by
`AgentStatus.tsx`).

#### Decisions (2026-08-16)

Ruling applied throughout: a deliberate, specifically-scoped choice gets recorded as a locked
exception; anything that just drifted unscoped gets fixed. Checked against real evidence
(`git log`, this doc's own task text), not assumed, for each:

1. **`ReviewActions`'s hand-rolled Approve/Reject buttons → locked exception, not fixed.**
   Checked `git log --follow` on `ReviewActions.tsx`: created in Epic 3 (`a2e1e25`), last touched
   in Epic 3's own pre-merge review (`411f694`) — both **before** Sprint 0 created the `Button`
   component (`7a94018`) at all. Never touched by any Epic 3.1 commit. Sprint 3's own U3.4 task
   text (above) is explicit: *"a Review-column card renders the existing `ReviewActions`
   unchanged, wrapped in the new visual shell."* This was a specific, named scope decision, not
   drift — `ReviewActions.tsx` is a **permanent, recorded exception** to "always use the shared
   `Button`." Its green Approve / red Reject stays exactly as it is.
2. **`IngestDocument.tsx`'s incomplete restyle → will be fixed**, not shipped as a gap. Bring the
   remaining elements (both textareas, "Parse posting"/"Save base resume" buttons, their labels,
   both file inputs, the generic-document `<details>` flow) up to Nocturne tokens, and kill the 5
   remaining `focus-visible:ring-blue-500` occurrences in favor of the locked `focusRingAccent`
   token.

   **Status: Fixed (2026-08-16), branch `feature/epic3.1-closeout-ingest-restyle`.** Implemented
   by a dispatched engineer against the exact mapping below, then independently verified: every
   line of `IngestDocument.tsx` re-read and diffed against the table (all 4 hand-rolled buttons
   now `<Button variant="primary">`, both textareas + generic textarea on `textareaClasses`, zero
   `slate-*`/`blue-*` classes left — confirmed via `grep -c` returning 0); `IngestDocument.test.tsx`
   run standalone (38/38 passing) and again as part of the full suite (backend 465/465, frontend
   321/321, `.\test`); `eslint` clean; live-verified in the browser preview at `/ingest` by reading
   computed classes/styles off the real DOM for the parse button, both textareas, and the "use
   saved resume" link — all match the locked tokens exactly.

   **Code review findings (2026-08-17) — PR #60.** Manual review posted directly to the PR as an
   inline comment — see
   [review #4949367313](https://github.com/AndrewVanDelden/TaskFlow/pull/60#pullrequestreview-4949367313)
   for the full text. No prior review on this PR to cross-check. **Status: Fixed (2026-08-17).**
   - `IngestDocument.tsx:183` (CONFIRMED) — the "Use previously saved base resume" button's
     `` hover:${textAccent200} `` interpolates a Tailwind variant prefix onto a token constant.
     This is the exact anti-pattern `lib/tokens.ts`'s own comment warns against: the scanner needs
     the full `hover:text-[#e7e5fe]` string as a literal substring, not a JS template expression,
     so the hover rule is never generated — a silent, string-check-proof regression. **Note: this
     exact value is what the mapping table below specifies** (row: "Use previously saved base
     resume" `<button>`) — the bug originated in this doc's own locked mapping, not just the
     implementation, so the fix updates both.
     **Fix:** added a literal `hoverTextAccent200 = 'hover:text-[#e7e5fe]'` constant to
     `lib/tokens.ts` (matching the file's own "must stay literal strings" rule) and swapped the
     interpolation for it in `IngestDocument.tsx`; updated the mapping table row above to match.
     Verified two ways: (1) full `.\test` — backend 465/465, frontend 321/321, both green; (2) live
     browser check — read the compiled Vite stylesheet's `cssText` directly and confirmed
     `.hover\:text-\[\#e7e5fe\]:hover { color: rgb(231, 229, 254); }` is now present as a real
     generated rule (it was absent before the fix, which is exactly the failure mode the review
     described — a class string that renders in the DOM but has no matching CSS).

   **Exact mapping, locked so an engineer doesn't have to guess** (every "old" value is quoted
   verbatim from the file as it stands today; every "new" value reuses an existing token/pattern
   already proven elsewhere in this codebase, not invented here):

   | Element | Old | New |
   |---|---|---|
   | `fileInputClasses` (shared, both file inputs) | `text-slate-400 ... file:border-slate-700 file:bg-slate-800 ... focus-visible:ring-2 focus-visible:ring-blue-500` | `${textNeutral500} ... file:${borderDivider} file:${bgSurface} ... ${focusRingAccent}` |
   | Stage `<h1>` focus ring | `outline-none focus-visible:ring-2 focus-visible:ring-blue-500` | `${focusRingAccent}` (already includes `outline-none`, drop the duplicate) |
   | Job-posting & base-resume `<textarea>` (2×, identical pattern) | `bg-slate-900 border border-slate-700` — **no focus-ring class at all today** (plain browser default outline, not even the old blue one) | `${bgSurface} border border-white/10 text-white ${focusRingAccent}` — exact match to `Login.tsx`'s own `inputClass` (`bg-[#232532] border border-white/10 ... focusRingAccent`), the locked reference pattern for every text input in this app |
   | Generic-document `<textarea>` | same as above | same as above |
   | "Or upload a file" / "Or upload a document file" `<label>`s | `text-xs text-slate-400` | `text-xs ${textNeutral500}` |
   | "Parse posting" `<button>` | `bg-blue-600 hover:bg-blue-500 ...` hand-rolled | `<Button variant="primary">` (shared component — this is also the Sprint 0 DoD fix for this file, not just a color change) |
   | "Save base resume" `<button>` | same hand-rolled pattern | `<Button variant="primary">` |
   | "Parse" (generic-document) `<button>` | same hand-rolled pattern | `<Button variant="primary">` |
   | "Approve and add to board" `<button>` | same hand-rolled pattern (emerald) | `<Button variant="primary">` — emerald here was never a locked exception (unlike the three running/connected spots in decision 3), it's the same drift as the other hand-rolled buttons |
   | "Use previously saved base resume" `<button>` (text-link style, not a real button — keep as a plain `<button>`, not `Button`, since `Button`'s two variants both carry padding/border treatments wrong for an inline link) | `text-blue-400 hover:text-blue-300 underline focus-visible:ring-2 focus-visible:ring-blue-500` | `${textAccent} ${hoverTextAccent200} underline ${focusRingAccent}` (literal `hoverTextAccent200` token, not `hover:${textAccent200}` interpolation — see PR #60 finding below) |
   | `<details>` summaries — "Preview base resume" and "Other: paste a generic document" (2×) | `text-slate-400 ... focus-visible:ring-2 focus-visible:ring-blue-500` | `${textNeutral500} ... ${focusRingAccent}` |
   | Section divider borders (`border-t border-slate-800`, 2×) | `border-slate-800` | `${borderDivider}` |
   | Expanded base-resume preview wrapper | `bg-slate-900/60 border border-slate-800` | `${bgSurface} border ${borderDivider}` |
   | Generic-document draft list items | `border border-slate-800 rounded-lg ... bg-slate-900/60`; section meta `text-[11px] text-slate-500`; description `text-xs text-slate-400` | `border ${borderDivider} rounded-lg ... ${bgSurface}`; meta `text-[11px] ${textNeutral500}`; description `text-xs ${textNeutral400}` (matches the parsed-result card's own established content-vs-meta split from U4.2: `textNeutral400` for primary content lines, `textNeutral500` for quieter/secondary text) |
   | "Base resume provided." status text | `text-sm text-slate-400` | `text-sm ${textNeutral500}` |

   **Deliberately left unchanged** (not in scope, not drift):
   - Error banners (`role="alert"`, red-on-dark) — genuine error messaging, a different UI category
     from workflow-status coloring; `Login.tsx` already establishes red stays for real errors
     (`text-red-300 bg-red-500/10 border-red-500/30`). Match Login's exact shade while touching
     these (currently `text-red-400 bg-red-950 border-red-900`, a mismatched red never reconciled
     with Login's) — a small consistency fix, not a new category decision.
   - Success confirmation text ("Base resume saved.", "Added N tasks...") — `text-emerald-400`,
     left exactly as-is. Same reasoning as errors: one-line transient feedback text, not persistent
     status coloring, and no locked green token exists to migrate it to — out of scope for this fix.
   - Section `<label>`s ("Job posting", "Base resume", "Paste a document") — `text-sm font-semibold`,
     no color class today (inherits white from the page wrapper), not a hand-rolled/stale-color
     issue — leave as-is. These are bold section headers, not per-field micro-labels like Login's,
     so they're not migrated to Login's separate muted `labelClass` pattern.
3. **Emerald running/connected exception → locked explicitly, `AgentStatus.tsx` included as-is.**
   `AgentStatus`'s "Running" pill, the "Live" connection dot, and `ExecutorControl`'s dot all keep
   emerald. This **updates the epic-wide Definition of Done** (below) to add a second named
   exception to "no red/amber/green," alongside the existing muted-green-Approved one. This
   resolves the design-handoff-diff finding above too (the handoff's file map named
   `AgentStatus.tsx` as in-scope for Sprint 3, but only its *card/spacing* would need a Nocturne
   pass if that's ever done separately — its pill color is now a locked exception either way, not
   something a future pass needs to "fix").

### Code review findings (2026-08-19/20) — PR #61 (`develop` → `main` close-out PR)

Manual review posted directly to PR #61 as inline comments (high effort, 8 finder angles across
correctness/reuse/simplification/efficiency/altitude/conventions, 1-vote verify) — see
[review #4979230329](https://github.com/AndrewVanDelden/TaskFlow/pull/61#pullrequestreview-4979230329)
for the full text. No automated reviewer had run on this PR to cross-check against. Two more
findings were independently posted afterward by a prior session that hit its usage limit mid-task
(recovered and reconciled — see the `CLAUDE.md` finding on this dated the same day: it left Python/
`gh api` scratch artifacts in the repo root, cleaned up, and never touched any source file).
12 findings total, all researched against the real code/doc before acting — not taken at face
value. **Status: all resolved (2026-08-20).**

**Fixed (3 confirmed bugs):**
1. `IngestDocument.tsx` — `file:${borderDivider}`/`file:${bgSurface}` interpolated onto token
   constants inside `fileInputClasses`, the exact `hover:` anti-pattern from PR #60's own finding,
   recurring. Only "worked" because `IngestDocument.test.tsx` happened to contain the literal
   strings the Tailwind scanner picked up from the test file instead of the component. Fixed with
   literal `fileBorderDivider`/`fileBgSurface` constants in `tokens.ts`, matching the
   `hoverTextAccent200` precedent. Verified with a real `vite build` both ways (broke it back to
   interpolated form to confirm the bug was genuine, then confirmed the fix generates real CSS).
2. `TaskService.UpdateAsync` (the plain `PUT /api/Tasks/{id}`) had no `RequiresPairApproval` guard,
   while `UpdateStatusAsync`/`ApproveAsync`/`RejectAsync` all got one in this same epic to close the
   "Epic-3 sibling forced to Done individually strands its `JobApplication`" bug — this endpoint
   silently reopened it. Fixed: same guard added, mirroring `UpdateStatusAsync`'s exact condition.
3. `ReviewActions.tsx`'s rejection-reason `<textarea>` was still stock pre-Nocturne Tailwind with no
   focus ring — the epic's own locked exception for this component covers only its Approve/Reject
   buttons, never the textarea; it was simply missed. Fixed to match the locked text-input pattern.

**Fixed (6 plausible findings, real but lower-severity):**
4. `AgentFeedList.tsx` dropped the `Task #{taskId}` reference the deleted `AgentFeed.tsx` rendered —
   restored, muted via `textNeutral500`.
5. Export-download eligibility (`status === 'Done' && applicationId !== null && applicationState ===
   'Approved'`) was duplicated business logic between `TaskCardView.tsx` and `ArchivedTaskList.tsx` —
   extracted to `canDownloadExport(task)` in `lib/board.ts`.
6. `JobApplicationRepository`'s `BothRequiredSiblingsAreReview`/`...AreDone` were near-identical
   expression trees — collapsed into one `BothRequiredSiblingsAre(WorkflowStatus)` factory.
7. `TaskCardView.tsx`/`KanbanColumn.tsx` still had raw `slate-*` classes never migrated to Nocturne
   tokens (description/meta/executor-output text and backgrounds, column shell, empty state) —
   migrated to `textNeutral300/400/500/600`/`bgSurface`/`borderDivider`.
8. Dashboard's offline-dot shade (`slate-600`) had drifted from `ExecutorControl`/`AgentStatus`'s
   `slate-500` for the same state — realigned.
9. `usePrefersReducedMotion` registered one independent `matchMedia` subscription per calling
   component (up to ~30 for a full board) — rewritten around `useSyncExternalStore` with a shared
   module-level singleton; public API unchanged, all existing callers unaffected.

**Fixed (1 finding not originally delegated — caught during final reconciliation):**
10. `TaskService.ArchiveAsync`/`UnarchiveAsync` re-fetched the task a second time after their
    guarded repository update purely to read back a value already known locally — removed the
    re-fetch, updating the tracked entity in memory instead (3 DB round trips → 2).

**Pushed back on (2 findings, researched and rejected as real issues):**
- `ArchiveAsync`/`UnarchiveAsync` discarding the repository's guarded-update bool: the code's own
  existing comment already reasons through this exact race and calls it "not a data-corruption
  risk" (end state is identical either way) — a deliberate, already-recorded tradeoff, not a miss.
- `border-white/10` vs `borderDivider` "drift," 2 of the 5 originally-flagged spots
  (`Login.tsx`'s `inputClass`, `IngestDocument.tsx`'s `textareaClasses`): checked against this
  doc's own locked mapping table (Sprint 4 close-out, decision 2 above) — `border-white/10` is the
  *locked reference pattern itself* for text inputs, not a drift. Fixed the other 3 spots
  (`TaskCardView.tsx`'s card wrapper, `AgentFeedList.tsx`/`ArchivedTaskList.tsx`'s row dividers)
  where it was genuinely misapplied to a divider/panel context.
- (Partial push-back, folded into finding 7 above): "broad `slate-*` remains across Board files"
  named `AgentStatus.tsx` alongside `TaskCardView.tsx`/`KanbanColumn.tsx` — `AgentStatus.tsx`'s
  card/spacing was already explicitly ruled out of this epic's scope by decision 3 above; left
  untouched. Not collapsing `ArchiveAsync`/`UnarchiveAsync` into one generic helper for the
  duplicate-shape part of finding 10's origin either — 2 call sites doesn't earn a
  parameterized-precondition abstraction (this doc's own "three similar lines beats a premature
  abstraction" standard).

Full suite green throughout and at the end: backend 470/470, frontend 340/340.

---

## Open decisions log

Recorded so nothing here is silently assumed. Each needs an answer before the sprint that depends
on it starts:

1. **URL-based job-posting parsing (fetch an arbitrary user-supplied URL server-side).** Explicitly
   **out of scope for this epic** — requires its own design and security review (SSRF surface) if
   ever pursued. Not decided against forever, just not part of Epic 3.1.
2. **Exact DTO(s) needing a `Company` field beyond `TaskDraft`/`JobApplication` themselves** — left
   to U3.1's own audit at task time, not pre-enumerated here, since this doc does not claim a full
   DTO audit was already done (only `TaskDraft.cs` and `JobApplication.cs` were read directly).
3. **Whether the brand-pane "live" teaser lines on Login are ever wired to real data** — explicitly
   static placeholder copy for this epic (Sprint 2); revisit only if a real product reason to make
   them live shows up.

---

## TDD Loop and Git Workflow (unchanged from Epic 3)

1. Claude writes a failing test (RED) with exact file path, namespace/imports, and usings.
2. You run `dotnet test` / `npm run test` (or `.\test` for the full suite) and confirm it is red.
3. Claude writes the simplest code to pass (GREEN).
4. You run again and confirm green.
5. Refactor if needed, tests staying green.

One branch and one PR per sprint into `develop`. `develop → main` at the epic close-out above.
Branch names: `feature/epic3.1-sprint-N-short-name`.
