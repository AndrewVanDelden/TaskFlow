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
| **0** | Design System Foundations, Accessibility & Test Infrastructure | **Shipped** (2026-08-13) — U0.1-U0.6 all green on `feature/epic3.1-sprint-0-foundations`; PR not yet opened |
| **1** | App Shell (Sidebar + Top Bar) | **Shipped** (2026-08-14) — U1.1-U1.5 all green on `feature/epic3.1-sprint-1-app-shell`; PR not yet opened |
| **2** | Login | Ready — architecture below, no code yet |
| **3** | Board (application-centric cards, quiet executor line, Activity rail) | Ready — architecture below, no code yet |
| **4** | Ingest & Hand-off (restyled paste flow, tailoring square) | Ready — architecture below, no code yet |

## Definition of Done (Epic 3.1)

- Every authenticated screen renders inside the new sidebar shell; `NavBar.tsx` is retired.
- Board, Login, and Ingest match the signed-off Nocturne reference (2a, 3a, 3b) — colors, type,
  spacing, and interaction per the design handoff's cheat sheet, corrected per the token fix above.
- No red/amber/green status coloring remains anywhere in the touched surfaces; status is carried by
  type and copy (one muted green check for "Approved," per the design, is the sole exception).
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

### Code review findings (fill in after this sprint's PR is reviewed)

*(Not yet started — nothing to record.)*

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

### Code review findings (fill in after this sprint's PR is reviewed)

*(Not yet started — nothing to record.)*

### Post-sprint retrospective (fill in once this sprint ships)

*(Not yet started — nothing to record.)*

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

### Code review findings (fill in after this sprint's PR is reviewed)

*(Not yet started — nothing to record.)*

### Post-sprint retrospective (fill in once this sprint ships)

*(Not yet started — nothing to record.)*

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

### Code review findings (fill in after this sprint's PR is reviewed)

*(Not yet started — nothing to record.)*

### Post-sprint retrospective (fill in once this sprint ships)

*(Not yet started — nothing to record.)*

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
