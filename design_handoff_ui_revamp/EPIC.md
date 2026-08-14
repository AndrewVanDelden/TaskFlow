# Epic: TaskFlow UI Revamp (Nocturne, agent-first)

> Replace the current "stock-Tailwind" look with a calm, dark, agent-first UI across Board, Ingest, and Login. Application-centric mental model; agents are the product.

## About the files in this bundle
`TaskFlow Board Revamp.dc.html` is a **design reference** — an HTML prototype of the intended look and behavior. **Do not paste it into the app.** Recreate each screen in the existing `TaskFlow.Web` stack (React 19 + TS + Tailwind v4), reusing the current file structure and hand-rolled-component pattern. The prototype is a single canvas with three "turns"; the ones that are signed off are **2a** (Board), **3a** (Ingest), **3b** (Login).

## Fidelity: **High**
Colors, type, spacing, and interactions are final. Match them. Values below are given as **stock Tailwind palette classes where one is close enough, and arbitrary values `[...]` where it must be exact** — per the project's "Tailwind v4 utilities only, no custom theme/config" constraint. No `tailwind.config` tokens are introduced.

## ⚠️ Dependency flags (decide before starting)
- **Icons — Phosphor.** The prototype uses Phosphor icons everywhere. `@phosphor-icons/react` is **not** in `package.json`. Either add it (`npm i @phosphor-icons/react`) or substitute inline SVGs. **Every `<i class="ph …">` in the prototype = one icon to place.** Nothing else in the design needs a new dependency.
- No animation library needed — all motion is CSS keyframes / Tailwind `animate-*`.
- Existing deps cover everything else (@dnd-kit for the board, signalr for the feed, react-markdown for previews).

---

## Design tokens → Tailwind v4 cheat-sheet
Paste these arbitrary values; they are the exact Nocturne hexes. Where a stock class is listed, it's within ~1 step and fine to use.

| Role | Hex | Use as |
| --- | --- | --- |
| Page bg | `#161826` | `bg-[#161826]` |
| Surface (cards) | `#232532` | `bg-[#232532]` |
| Divider/border | `rgba(233,233,237,.16)` | `border-[#e9e9ed]/[.16]` (≈ `border-white/15`) |
| Text | `#e9e9ed` | `text-[#e9e9ed]` (≈ `text-slate-100`) |
| Accent (blurple) | `#9184d9` | `text-[#9184d9]` / `bg-[#9184d9]` |
| accent-200 (text on tint) | `#e7e5fe` | `text-[#e7e5fe]` |
| accent-300 (accent text ≥ body) | `#d2cefd` | `text-[#d2cefd]` |
| accent-400 (glows, dots) | `#b5abfc` | `bg-[#b5abfc]` |
| accent-500 (fill) | `#968ae0` | `bg-[#968ae0]` |
| accent-700 / 800 (tint fills, hovers) | `#5d5294` / `#423a6a` | `bg-[#423a6a]` |
| neutral-300 → 600 (muted text ramp) | `#cfd3e5 #b2b6ca #9397ab #75798c` | `text-[#9397ab]` etc. (≈ slate-300…500) |

Radii: cards `rounded-xl` (12px), squares/large `rounded-[14px]`, pills `rounded-full`, chips `rounded-md` (6px).
Type: **Inter** (already the app font? if not, load weights 400–700). Headings weight **600 max** — never heavier. Sizes: page title `text-2xl`, card title `text-sm font-semibold`, meta `text-xs`, micro-labels `text-[11px] uppercase tracking-wide`.
Font family: everything is Inter; no separate heading face.

### Reusable primitives to build first
- **Button** (`.btn` equivalents): primary = accent **outline on transparent**, not a fill — `border border-[#9184d9] text-[#9184d9] hover:bg-[#9184d9]/15 rounded-lg`. Ghost = `text-[#9397ab] hover:bg-white/5`.
- **Focus ring** (global): `focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#9184d9]` — kill the default blue ring everywhere.
- **Column header**: `text-[11px] font-semibold uppercase tracking-wide text-[#9397ab]` + count in `text-[#75798c]`.

---

## Stories

### Story 1 — App shell (sidebar + top bar)
**Files:** new `src/components/SideBar.tsx` (replaces top-nav-only `NavBar.tsx`), `src/App.tsx` (layout wrapper).
- 60px fixed left rail: brand lightning tile (accent-500 bg, `#12121a` glyph) + icon nav items (Board active, Ingest, Activity) + avatar at bottom. Active item: `bg-[#9184d9]/15 text-[#e7e5fe] rounded-[10px]`, inactive `text-[#75798c]`.
- Top bar per screen: `text-2xl font-semibold tracking-tight` title + muted count; **actions live top-right** (search icon + primary button). Keep this pattern on every screen.
**Done when:** every route renders inside `[sidebar | main]`; NavBar's top-nav is gone.

### Story 2 — Board (Dashboard.tsx / KanbanBoard.tsx) — ref **2a**
**Files:** `src/features/Dashboard.tsx`, `src/features/KanbanBoard.tsx`, `src/components/KanbanColumn.tsx`, `src/components/TaskCardView.tsx`, `src/components/ExecutorControl.tsx`, `src/components/AgentFeed.tsx`.
- Layout: `grid` main = `[1fr 300px]` (board + activity rail). Board = 4 equal columns `gap-[18px]`: To Do / In Progress / Review / Done.
- **ExecutorControl** → shrink from the big banner to a **single quiet line** under the header: pulsing accent dot + "Executor running · 2 working · 3 queued · 6 done today" in `text-[#9397ab]`, Pause as a ghost button pushed right. Replace the current oversized card entirely.
- **TaskCardView** → application-centric card: `bg-[#232532] border border-white/10 rounded-xl p-4`. Role title `text-sm font-semibold`, company `text-[#9397ab] text-xs`, priority as **quiet text** top-right (`High` in `text-[#d2cefd]`), not a filled badge. Status carried by type, not color chips.
  - In-progress card: line "Tailoring resume…" in `text-[#e7e5fe]` with a pulsing dot; a **2px accent progress line pinned to the card bottom** (`absolute inset-x-0 bottom-0 h-0.5`, inner bar `bg-gradient-to-r from-[#796cbf] to-[#d2cefd]` with a shimmer keyframe).
  - Review card: adds a full-width primary "Review" button.
  - Done column: cards at `opacity`/`bg-[#232532]/55`, muted, with a check + "Approved · exported".
- **AgentFeed** → the 300px rail titled **Activity** with a "Live" pulse. Each row: one line of text + agent name + relative time, separated by `border-b border-white/10`. Drop the old boxed/badged rows.
**Done when:** columns read as applications with nested resume/cover status; no red/amber/green (mono blurple only, aside from a single muted green check for done — optional).

### Story 3 — Ingest (IngestDocument.tsx) — ref **3a**
**Files:** `src/features/IngestDocument.tsx`.
- Centered column `max-w-[760px]`. Header actions top-right: `Cancel` (ghost) + `Start tailoring` (primary).
- 3-step indicator: Posting · Base resume · Tailor.
- **Job posting**: a **URL input row** (`link` icon + input, arbitrary placeholder "Paste a job posting URL — or type/paste the description") + **Parse** primary button. Below: a "Parsed from vercel.com/careers · 2s ago" caption, then the **parsed result card** (company avatar, role, comp, requirement **chips** `bg-[#3f424d] text-[#e4e7f5] text-[11px] rounded-md`). This replaces the raw textarea for the posting.
- **Base resume**: NOT a textarea. A `<details>` row: left = a **112×146 paper thumbnail** (light `#f3f5fe` with grey skeleton lines) + name/meta/"Click to expand"; clicking expands the **full one-page resume** rendered as a light document. To the right sits Story 4.
**Done when:** posting comes from a URL→Parse flow; base resume is a click-to-expand doc preview, not a text field.

### Story 4 — "Start tailoring" hand-off square (part of Ingest) — ref **3a**
**Files:** `src/features/IngestDocument.tsx` (+ a small `TailorButton` component).
- A **184×184 square** to the right of the base resume. The **whole square is the button** — contents are only a sparkle icon + "Start tailoring" (accent, centered). No heading/sub-copy.
- Hover: square tints + accent border. 
- On click (**fun animation**): a radial glow burst scales out from center, 8 sparkle glyphs fly outward, the square fills accent (`bg-[#968ae0]`), and the label flips to "Tailoring…" with a spinning sparkle. Pure CSS keyframes (`goGlow`, `sparkFly`, `spin`) — see prototype `<style>` block; port them to Tailwind arbitrary keyframes or a small CSS module. **No JS animation lib.**
- **Behavior after click** (functional, agreed): (1) create the application + child tasks (tailored resume, cover letter) in `To Do`; (2) navigate to the Board; (3) if executor is running, prioritizer ranks + executor claims top task → card moves To Do→In Progress, streaming over SignalR; (4) agents do resume then cover letter, emitting feed events; (5) app lands in Review; (6) approve → Done/exportable. The button's job = persist + kick off pipeline + route to Board.
**Done when:** square behaves as one button with the burst animation, and click triggers create-application + navigate-to-board.

### Story 5 — Login (Login.tsx) — ref **3b**
**Files:** `src/features/Login.tsx`.
- Split card `920×560`: left **brand pane** (gradient `from accent-tint to bg`, lightning + "TaskFlow", headline "Your autonomous application workspace", and 3 live agent-status teaser lines with dots) | right **form pane** (Welcome back, email + password fields using `.field/.input` equivalents, primary "Sign in" full-width, divider, secondary "Create an account").
- Fields: `bg-[#232532] border border-white/10 rounded-lg h-10`, label `text-xs text-[#9397ab]`.
**Done when:** login is the split brand+form layout with agent-first identity copy.

---

## Global interaction rules (apply everywhere)
- Every interactive element gets a themed `:hover` tint from the accent ramp and the accent `:focus-visible` ring — no browser defaults.
- Primary buttons are **outlined**, not solid-filled (the tailoring square is the one intentional fill-on-activate).
- Keep chroma low: neutrals for surfaces/borders/muted text; blurple only as line, text, dot, and glow.
- Headings never heavier than 600; hierarchy is size + space.

## Screen ↔ file map
| Screen (ref) | Primary files to touch |
| --- | --- |
| Shell | `App.tsx`, new `SideBar.tsx` (retire `NavBar.tsx`) |
| Board (2a) | `Dashboard.tsx`, `KanbanBoard.tsx`, `KanbanColumn.tsx`, `TaskCardView.tsx`, `ExecutorControl.tsx`, `AgentFeed.tsx`, `AgentStatus.tsx` |
| Ingest (3a) | `IngestDocument.tsx` |
| Tailor square (3a) | `IngestDocument.tsx` (+ `TailorButton`) |
| Login (3b) | `Login.tsx` |
| Shared | `src/lib/styles.ts` (badge/status class maps — update to Nocturne values) |
