---
description: Implement an agreed spec task by task, checking each step
argument-hint: <spec folder, e.g. specs/001-ci-on-main>
---

Implement: $ARGUMENTS

1. Read `spec.md`, `plan.md` and `tasks.md` in that folder, plus `AGENTS.md` and the relevant `docs/rules/*`.
   If the spec has no agreed acceptance criteria, stop and run `/spec` first.
2. Work on a branch named after the spec folder.
3. For each unticked task, in order:
   - make the smallest change that completes it;
   - run the task's own check and read the output;
   - tick it in `tasks.md` only if the check passed; otherwise fix or stop and report what failed.
4. Respect the boundaries in `AGENTS.md`: stay inside the repo, no personal data, no secrets, the hands contract
   (four places), nothing irreversible.
5. Commit per task when it makes sense: imperative English subject, body says why.
6. When all tasks are ticked, run `/verify`. Do not claim "done" before it passes.
