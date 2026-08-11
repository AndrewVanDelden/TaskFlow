# TaskFlow code-review skill

Repo-specific instructions for reviewing changes to this codebase — on top of general good
practice (correctness, design, complexity, tests, naming), check the items below every time.
They exist because each one caused a real, confirmed finding in a past review (see PR #40's
`TaskFlow_Epic3_ResumeBuilder.md` "Code review findings" subsection for the full history), not
because they're theoretical.

## Architecture conventions to verify, not assume

- **Services return `Result<T>` (`TaskFlow.Api/Common/Result.cs`), never throw for expected
  failure cases, never reference `IActionResult`.** Controllers translate via
  `ResultExtensions.ToActionResult()`. A service method that can fail (validation, not-found,
  conflict) should return a `Result`, not throw — throwing is reserved for genuinely unexpected
  failures (a dependency being down), which should surface as an unhandled 500, not be swallowed
  into a `Result` that pretends everything is fine.
- **DTOs (`TaskFlow.Api/DTOs`) are the wire boundary; domain types (`TaskFlow.Api/Models`) are
  internal.** A DTO must not silently drop or ignore fields it accepts — either don't accept them
  or use them. Check both directions: does the DTO accept something the domain layer ignores
  (dead client input), and does the domain layer assume something the DTO doesn't actually
  guarantee (see the two checks below)?
- **Ownership scoping is always a pair, queried together.** Anything keyed by
  `(IngestionSessionId, OwnerId)` — `ResumeContext`, `JobApplication` — must query, index, and
  compare on both fields together, never one alone. A caller with the right session id and the
  wrong owner id must get the same "not found" response as a caller with no session id at all —
  never a distinguishable error that reveals the session id exists.
- **Untrusted content reaching a Claude prompt must go through
  `PromptSafety.WrapUntrusted`** (`TaskFlow.Api/Security/PromptSafety.cs`) before it's concatenated
  into any prompt string. This includes every new Claude-backed parser or agent, not just the most
  recently added one — when adding a second implementation of an existing pattern, check whether
  the *existing* one already has this protection, don't just add it to the new one.
- **Generated/saved content goes through `ToolOutputValidator.Validate`**
  (`TaskFlow.Api/Security/ToolOutputValidator.cs`) before it touches storage.
- **A second Claude-backed parser should extend `ClaudeJsonExtractionParserBase<TJson>`**
  (`TaskFlow.Api/Ingestion/ClaudeJsonExtractionParserBase.cs`), not reimplement the
  configured-check/API-call/JSON-extraction/deserialize-failure skeleton. If a new parser looks
  structurally identical to an existing one apart from the prompt and JSON shape, it probably
  should be a subclass, not a sibling copy.

## Specific failure patterns already found in this codebase — check for these explicitly

1. **A DTO's `[MaxLength]` must match the persisted domain field's `[MaxLength]`, and any
   *derived* string built from that field (a prefix/suffix added later) must be truncated or
   accounted for separately** — capping the raw input alone is not enough if something downstream
   concatenates onto it. (Found: `JobPostingSummaryDto` had no caps at all; separately, capping
   `Title` alone didn't prevent the derived cover-letter title from overflowing once
   `"Cover letter — "` was prepended.)
2. **A non-nullable C# string property on a DTO is not actually non-nullable at the wire boundary**
   unless it's marked `[Required]` (with `AllowEmptyStrings = true` if empty string should still be
   valid). An explicit JSON `null` bypasses the C# type system entirely during model binding.
   (Found: `JobPostingSummaryDto.Section`.)
3. **An enum-like string discriminator field (`ContentFormat`: `"text"`/`"markdown"`) must
   normalize null, empty, *and* whitespace-only input the same way** — `value ?? "default"` only
   catches `null`. Use `string.IsNullOrWhiteSpace(value) ? "default" : value`. (Found:
   `ResumeContextService.SaveAsync`.)
4. **A check-then-act upsert (query, then insert-or-update) backed by a unique database
   constraint needs the constraint-violation exception handled, not just the constraint added.**
   Adding the constraint without handling what it throws converts a silent data-integrity bug into
   an unhandled 500 for the exact scenario the constraint exists to catch. (Found:
   `ResumeContextService.SaveAsync` after the round-1 idempotency fix.)
5. **When you do catch that exception, re-verify the specific business condition before deciding
   what it means — don't catch a broad exception type and assume the cause.** Catching
   `DbUpdateException` and always reporting "conflict" will misreport an unrelated persistence
   failure (DB unavailable, a different constraint) as a concurrency race, hiding the real error.
   Re-check business state (does a row now exist for this exact key?) to distinguish the specific
   case you're handling from everything else that exception type could also mean, and rethrow for
   everything else.
6. **A comment describing what a value "does" must be checked against what actually reads that
   value** — a value threaded through a constructor or parameter that nothing downstream ever
   consults is dead, and a comment implying it matters is actively misleading. (Found: `TaskDraft`'s
   `Kind` argument in `JobApplicationsController.Assemble`.)

## Process rules (from this repo's `CLAUDE.md`) that apply to the review itself, not just the code

- **Fix a found, scoped, fixable issue immediately — don't defer it to a follow-up task**, even
  one found outside the original ask. If it's ambiguous whether something is a quick fix or a
  larger scope question, ask, rather than defaulting to deferral.
- **Every fix needs its own RED test, confirmed failing against the pre-fix code, before the GREEN
  change** — this applies to fixes made *during* a review pass exactly as much as to new features.
- **Record findings in the active epic/architecture doc's own history for the sprint/PR, not a
  standalone review file.** One source of truth per project.
- **Cross-check independent reviews (manual and automated) against each other before treating
  either as final.** Convergence on the same finding is corroborating evidence; a finding only one
  side catches is still worth checking before dismissing OR before acting on it blind — verify
  automated findings against the actual code (they can be false positives) before fixing them.
- **State what you actually verified vs. what you're inferring, especially about deploy/database
  state** — "should be applied" is not the same as confirmed via `dotnet ef migrations list`.
