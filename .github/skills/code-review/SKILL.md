Strict TDD, clean code, SOLID, and DRY

copilot-code-review
Perform a comprehensive code review on source code, pull requests, or diffs in the style and quality standards of GitHub Copilot Max. Use when the user asks for a code review, feedback on code, checking a diff or PR, or auditing code for security, performance, correctness, and maintainability.

Instructions
copilot-code-review
Perform thorough, structured, and actionable code reviews consistent with the standards of GitHub Copilot Max code review.

When to Use
Use this skill when the user asks to:

Review source code, diffs, PRs, or patches
Audit code for bugs, security vulnerabilities, or performance bottlenecks
Provide constructive feedback or refactoring suggestions on code snippets
Check code against best practices and maintainability standards
Core Review Principles
Prioritize Impact: Focus heavily on correctness, security, and performance before superficial style nitpicks.
Actionable Feedback: Provide concrete, corrected code snippets demonstrating the suggested change.
Structured Severities: Classify issues clearly (Critical, Major, Minor, Nit) so developers know what requires immediate action.
Constructive Tone: Keep feedback clear, objective, and solution-oriented.
Code Review Checklist
Evaluate code across these key dimensions:

1. Correctness and Logic
Are there unhandled edge cases, null or undefined pointers, or boundary condition failures?
Is state managed properly without race conditions, deadlocks, or stale references?
Are error conditions caught and handled gracefully?
2. Security and Safety
Is user input sanitized and validated (protecting against OWASP vulnerabilities like SQL injection, XSS, and command injection)?
Are sensitive data, credentials, or secrets exposed in code or logs?
Are authentication, authorization, and access control checks properly enforced?
3. Performance and Resource Efficiency
Are there N+1 query problems, inefficient loops, or exponential complexity algorithms?
Are resources (file handles, database connections, sockets) properly opened and closed/disposed?
Is asynchronous I/O used correctly to avoid blocking execution threads?
4. Maintainability and Readability
Is the code self-documenting with clear, meaningful variable and function naming?
Does it adhere to DRY (Don't Repeat Yourself) without over-engineering or premature abstraction?
Are complex, non-obvious algorithms accompanied by concise explanatory comments?
5. Testability and Edge Case Coverage
Is the code structured to be easily unit-tested?
Are boundary conditions, empty inputs, and failure scenarios covered by tests?
Review Output Format
Structure code review responses using the following template:

Summary
Provide a 2 to 3 sentence overview of the code changes and the overall quality assessment.

Findings by Severity
Critical / Security
High-impact bugs, security vulnerabilities, memory leaks, or data loss risks that must be fixed before merging.

Major / Correctness & Performance
Logic errors, edge case failures, or performance bottlenecks that should be addressed.

Minor / Maintainability
Refactoring suggestions, code cleanups, or improved abstractions to enhance readability.

Nits / Style
Minor style inconsistencies, naming tweaks, or minor doc improvements.

Detailed Recommendations & Code Snippets
For each identified issue, provide:

File / Line Location: Point out where the issue occurs.
Explanation: Describe the problem and why it matters.
Suggested Fix: Provide a clear before and after or refactored code snippet.
Positive Highlights
Acknowledge clean patterns, good test coverage, or elegant implementations when present.

Gotchas
Do not invent hypothetical bugs if the code is solid. If the code looks good, explicitly state that it passes review with no critical findings.
Avoid forcing personal style preferences as critical bugs. Label subjective stylistic choices clearly as Nits or Minor suggestions.
Ensure code snippets in suggestions match the exact syntax and language conventions of the codebase being reviewed.
