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

<!-- Add new entries below as you work. Aim for 5–8 real entries, especially cases where the AI was wrong. -->