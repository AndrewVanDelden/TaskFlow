# Rules to follow for AI who are reading this:

- Strict TDD, clean code, SOLID, and DRY is How we will build everything.
- **When adding code, add its test coverage in the same change — up front, not later.**
  If asked to add a method/class/file, deliver the tests that cover it alongside it. Do not
  hand over new implementation code with "tests can come later."
- we will adhere to strictly DRY and SOLID principles.
- Do not agree with me on everything. Come back with sound advice from the principles.
- Do not bandaid any fixes. If it is wrong then lets work to fix it.
- **How to work:** follow this document top to bottom. Each slice has explicit file paths,
  RED tests, GREEN code, a pastable PR description, and merge/delete steps. Bring bugs to
  chat; every fix gets recorded back into this document.
- **Tooling boundary (important):** Claude can create and edit files directly in the repo,
  but Claude's sandbox CANNOT run `git` (it is permission-blocked from writing `.git`) and
  does not run `dotnet`. So: Claude writes the code and tests into the repo; YOU run every
  `git` and `dotnet test`/`dotnet build` command on your machine and report the result.
  This matches the TDD loop (you run, Claude writes).
- **Root convenience commands:** `.\run` (run.cmd) starts the whole app — API + web with the browser
  opening on `:5173` — and `.\test` (test.cmd) runs the full test suite with coverage into
  `test-results.txt`. Both live at the repo root so no folder-changing is needed.
- **Test workflow (how Claude checks results):** the user runs `.\test` from the repo root. It runs the full backend (`dotnet test /p:CollectCoverage=true`) and frontend
  (`vitest run --coverage`) suites and writes ALL output — pass/fail, coverage tables, and errors
  (it captures `stdout` **and** `stderr` via `2>&1`, so failing tests, assertion messages, stack
  traces, and build errors are all in there) — to `test-results.txt` at the repo root. That file is
  git-ignored, overwritten each run, and color-free (`NO_COLOR`). Claude READS `test-results.txt`
  (its mount path is `/sessions/.../mnt/TaskFlow/test-results.txt`) to confirm results instead of
  asking the user to paste terminal output. Loop: Claude writes code+tests → user runs
  `.\coverage.cmd` → Claude reads `test-results.txt`.
- **Standing rules that were violated once and must not be again:** domain types never
  reuse .NET BCL names (see Naming Conventions); fix collisions by renaming at the source,
  not aliasing; result types live in `Common/`.

**Rules added after the AI violated them in this session (do not repeat):**

- **Never claim you verified something you did not actually check.** Confirm against the
  real artifact: exact file name, real contents, actual git state. (Violated: reported
  `Result.cs` "missing" after checking the wrong name; the file was `Results.cs`. A false
  verification is worse than admitting you have not checked.)
- **Separate facts from inferences; never state an inference as fact.** Say what you
  actually checked, and label everything else as an inference for the user to confirm.
  (Violated: asserted "the solution does not build" without a build. The truth is whatever
  `dotnet build` prints.)
- **Never assume progress or mark work done without confirmation.** Do not tick off a step
  (git run, file created, test passed) unless the repo or the user confirms it. When
  unsure, check or ask. Do not guess. (Violated: marked D0/D1 complete on assumption.)
- **Enforce TDD order; halt the moment implementation is being written before its failing
  test.** If code is landing ahead of a confirmed RED, call it out and stop, even if the
  user is the one moving ahead. (Violated: let D3 service code exist before D2 was red.)
- **When the deliverable is code, deliver the actual code** with file path, namespace, and
  usings, not a prose description of what it would do. Prose is for the test to encode, not
  a substitute for the class. (Violated: gave an AuthService prose spec instead of the file.)
- **Never hand over anything you claim works but have not tested.** Test it first, or state
  plainly that it is untested and why. Applies to snippets, links, and commands. (Violated:
  shipped a self-anchor link twice without testing that it resolves.)
- **Hold the whole map, not just the slice in front of you.** Read the entire document
  before advising so guidance fits the overall scope, not one local step. (Violated:
  advised for several turns having only read part of the doc.)
- **Do not attempt `git` or `dotnet` from the AI sandbox.** It cannot write `.git` and has
  no `dotnet`; a failed attempt left a stale `.git/index.lock` the user had to remove by
  hand. Hand every git/build/test command to the user (see Tooling boundary above).
- **Do not invent scope, and never slip unspecified work into a "next step."** If something is
  missing and should be added, say so explicitly and record it in the active doc as a labeled
  decision before acting. (Violated: dropped "thread a source name into provenance" as if it were
  a task that existed.)
- **Refer to a task by exactly what the doc says it is;** do not silently relabel or re-scope a
  numbered task in passing.
- **Own architect/developer decisions; do not punt "your call" on a choice that is yours to make.**
  Decide, record it in the doc, and commit. Reserve "your call" for genuine product decisions.
- **Never run `git reset --hard` (or any other command that rewrites the working tree) without
  first checking `git status` for uncommitted changes to tracked files, and committing or stashing
  them first.** `reset --hard` discards uncommitted edits to tracked files silently; it does not
  touch untracked files, which makes partial data loss easy to miss. (Violated, 2026-08-07, during
  Epic 3 Sprint 1: fixed an unrelated branch-hygiene mistake by running `git reset --hard` on
  `develop` while a subagent's uncommitted edits to five existing files were still in the working
  tree. The edits were silently wiped; only the subagent's new, untracked files survived. Caught by
  independently re-verifying the subagent's diff instead of trusting its self-report, then recovered
  by having the same subagent redo just the lost edits. Lesson: commit each verified slice of work
  immediately, before starting the next one — don't let verified-but-uncommitted work sit exposed
  while doing anything else.)

**Findings from the long setup/refactor session (apply these too):**

- **Every code block must be paste-ready.** Include the file path, the `namespace`, and all
  `using` directives in each block. The user pastes verbatim, so a block missing a `using`
  costs a full build cycle. This recurred with the repositories, `Program.cs`, and
  `TokenResult`. If a block is an edit, show the DELETE and the REPLACE, not just the new
  lines; leftover old lines once kept a `_config` field alive that was supposed to be removed.
- **Trust `dotnet build`, but verify with a fresh one.** If the IDE and the build disagree,
  have the user run `dotnet build` again before claiming anything. Never call an error
  "stale IDE" without that fresh build. Missed once: a real missing `using` in `Program.cs`
  was wrongly dismissed as stale IDE.
- **Give exact file locations, not vague ones.** "Put it in the service" is not enough. Name
  the file, say whether it is create-new or edit-existing, and for edits point at the exact
  spot.
- **Repo hygiene is set up; keep it that way.** Root `.gitignore` excludes
  `node_modules/ bin/ obj/ .env.local *.db`; `.gitattributes` normalizes line endings to LF
  (with `.ps1/.cmd/.bat` kept CRLF). Never commit `node_modules` or build output. If
  `git status` shows a huge changeset, it is almost certainly line-ending noise: confirm with
  `git diff --ignore-all-space -- <file>` (empty output means pure whitespace) before acting.
- **On a rename, update internal names too.** Renaming a folder is not enough. Also fix
  `index.html` `<title>` and `package.json` `"name"` (npm names must be lowercase). The web
  source recovery point after the reset is commit `6ca203d`.
- **Source of truth is the active working document.** `TaskFlow_Refactor_Architecture_and_TDD.md`
  is COMPLETE (Slices A–L shipped, 39 backend + 14 frontend tests green) and is now historical.
  `TaskFlow_NorthStar_Epic.md` (Epic 2, Sprints 1–7 shipped) is also now historical — **Sprint 8
  (Claude retry/resilience) was deliberately deferred indefinitely (2026-08-06)** and was never
  built; do not assume it exists. Ongoing work now lives in **`TaskFlow_Epic3_ResumeBuilder.md`**
  (Epic 3: resume + cover-letter builder, Sprints 0, 1, 2, 3R, 4R, 5, 6 — all seven sprint docs are
  in hand as of 2026-08-06). On any new chat, read the active epic doc
  first; do not re-derive context from chat history and do not use a "RESUME HERE" block. Record
  every bug fix and decision back into the active doc so the chat stays disposable.
- **Do not edit the user's source files unless asked.** The user is learning; tell them what
  to change and where, and let them apply it. Always keep the guide doc updated yourself.
  Edit source directly only on explicit request, and do not overstep a "where does this go?"
  question by silently creating or moving files.

**Findings from the PR #40 code-review session (2026-08-10):**

- **Code review findings live in the active epic doc, not a standalone review file.** Never
  create a one-off `PR-<n>-CODE-REVIEW.md` (or similar) file for a review. Record findings as
  a dated subsection under the sprint the PR belongs to, in the active epic doc (e.g.
  `TaskFlow_Epic3_ResumeBuilder.md`), matching that doc's own decision-record style (numbered
  findings, file:line anchors, why + fix, RED-test-first note, open/fixed status). One source
  of truth per project, not a review file per PR. (Corrected in this session: created
  `PR-40-CODE-REVIEW.md` unprompted; user asked for it folded into the epic doc and the
  standalone file deleted.)
- **Cross-check a manual PR review against any automated reviewer already on the PR** (e.g.
  GitHub Copilot's review comments, fetched via
  `gh api repos/<owner>/<repo>/pulls/<n>/comments`) before treating the review as final.
  Convergence between an independent automated review and a manual one is corroborating
  evidence a finding is real, not noise; and the automated pass can catch things a manual
  pass missed. (Applied in this session: manual review and Copilot's review independently
  converged on the same idempotency bug and the same unguarded-`int.Parse` issue; Copilot also
  caught a test-quality gap — a `.not.toContain(...)` assertion that doesn't actually prove
  `localStorage.setItem` was never called — that the manual review missed.)
