# AI Collaboration Log

A running log of how AI tools were used in this project: what I asked, what the AI produced, how I verified it, and what I changed. The README's AI Collaboration Narrative summarises the most important entries.

## Entry template

```
### NNN — Short title (phase, date)
- Tool:
- Task / prompt (summary):
- AI output:
- How I verified it:
- What I changed or rejected:
- Lesson:
```

## Entries

### 001 — Analysing the brief and choosing a scenario (design, 2026-10-28)

- **Tool:** Claude (chat)
- **Task / prompt (summary):** Analyse the assessment brief, explain what the evaluators focus on, compare the four scenarios, and suggest a layer and a tech stack.
- **AI output:** Pointed out that AI engineering and verification is the main evaluation criterion. Recommended Scenario A with the backend layer, because it has real business logic (resource constraints, overlap detection, concurrent double booking) that can be tested. Suggested ASP.NET Core with PostgreSQL, and Spring Boot as an alternative if the job description required Java.
- **How I verified it:** Read the brief again against the recommendation, and checked the actual job posting. The posting is stack-agnostic and asks for strong fundamentals, testing and AI-assisted tooling; other Keyloop postings use .NET, Entity Framework and AWS.
- **What I changed or rejected:** Confirmed .NET instead of keeping the stack open. Decided not to build a frontend, because the brief asks for one layer implemented fully and allows the other to be stubbed. Added CI to the plan because the posting mentions CI/CD.
- **Lesson:** Check AI recommendations against the primary source (the brief and the job posting) before committing to them.

### 002 — Direction before detail, and a change of working mode (design, 2026-10-30)

- **Tool:** Claude Code (Opus)
- **Task / prompt (summary):** Turn the brief into a system design and an implementation plan.
- **AI output:** Went straight to database constraints, libraries and error codes, then wrote a plan with full pre-written code and ran probes and a demo nobody had asked for.
- **How I verified it:** Compared what was produced with what we had agreed; it was ahead of the agreement.
- **What I changed or rejected:** Stopped it, agreed brief and direction first, then the SDD; switched the project from AUTO to HYBRID mode (D16) and fixed a six-stage process (D17). The plan with code was kept in history but replaced by a task-level plan.
- **Lesson:** Decide the level of autonomy explicitly and write it down; the AI follows the boundary once it exists.

### 003 — Error format that broke scenario S31 (review of pull request #2, 2026-11-02)

- **Tool:** Claude Code (Opus reviewer)
- **Task / prompt (summary):** Review the whole pull request diff against the design and the rules.
- **AI output:** Ran the API and sent a malformed GUID: the framework answered with an RFC link as `type`, no `detail` and a different `traceId` format.
- **How I verified it:** The real responses were shown before any fix; S31 requires the project format for every error.
- **What I changed or rejected:** Approved normalising all framework errors (D25) with two extra tests.
- **Lesson:** Review by running, not only by reading.

### 004 — Deadlocks under concurrency, then a free bay left unbooked (2026-11-02)

- **Tool:** Claude Code (Opus reviewer, Sonnet implementer)
- **Task / prompt (summary):** Re-verify the test suite after the previous fix.
- **AI output:** The parallel booking test failed once in several runs. Reproduced on Docker Compose: some rounds returned 500; the log showed `40P01 deadlock detected`. After handling the deadlock, a second problem appeared: on a freshly started stack only one of two free bays was booked.
- **How I verified it:** 15 and 20 rounds of 12 parallel requests on fresh stacks, counting status codes, booked bays and `40P01` lines.
- **What I changed or rejected:** Approved the deadlock retry (D26), then per-resource advisory locks in a fixed order (D27) over more retries with back-off or relaxing the scenario. Afterwards: 12 of 12 rounds correct and zero deadlocks.
- **Lesson:** A flaky concurrency test is a signal, not noise; reproduce it at scale before fixing.

### 005 — A race in the first idempotency design (2026-11-03)

- **Tool:** Claude Code (Sonnet implementer)
- **Task / prompt (summary):** Make booking idempotent with an `Idempotency-Key` header.
- **AI output:** The first version returned 409 to some of five parallel identical requests.
- **How I verified it:** The parallel scenario I5 in the test suite.
- **What I changed or rejected:** PostgreSQL can report the bay, technician or vehicle conflict before the key conflict, so the replay check now also runs on those paths.
- **Lesson:** With several constraints on one row, do not assume which one fails first.

### 006 — Process slips caught in review (2026-11-01 to 2026-11-03)

- **Tool:** Claude Code
- **AI output:** Delegated work with a model alias that pointed to an older model than intended; an implementer started Docker Desktop on its own; a commit would have been red because a migration was missing.
- **How I verified it:** Reports were checked against the brief and the real test output before every commit.
- **What I changed or rejected:** Pinned the implementer's model and effort in an agent definition, tightened task briefs, and moved the migration into the task that changed the model.
- **Lesson:** The process needs the same verification as the code.