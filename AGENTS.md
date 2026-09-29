# Repository agent instructions

## Internal implementation plans

- Store agent-generated implementation plans and working roadmaps under `.codex/plans/` by
  default. This directory is intentionally excluded from Git.
- Do not commit, push, publish, attach, quote, or otherwise share an internal plan unless the user
  explicitly asks to share that specific plan.
- Public documentation should describe supported behavior, durable design decisions, limitations,
  and release information. It should not expose internal task breakdowns or planning history.
- When work is complete, transfer only lasting information into public guides, architecture
  decisions, maintenance policies, or changelog entries.
