# Rules to follow for AI who are reading this:

- TDD is How we will build everything
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
- **Source of truth is `TaskFlow_Refactor_Architecture_and_TDD.md`.** On any new chat, read
  its "RESUME HERE" block first. Do not re-derive context from chat history. Record every bug
  fix back into that doc so the chat stays disposable and can be compressed anytime.
- **Do not edit the user's source files unless asked.** The user is learning; tell them what
  to change and where, and let them apply it. Always keep the guide doc updated yourself.
  Edit source directly only on explicit request, and do not overstep a "where does this go?"
  question by silently creating or moving files.
