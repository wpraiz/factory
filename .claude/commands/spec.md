---
description: Write the spec, plan and tasks for a backlog item before any code
argument-hint: <backlog item number or short description>
---

Write a spec for: $ARGUMENTS

1. Read `AGENTS.md`, `specs/README.md`, `docs/BACKLOG.md` and the `docs/rules/*` and `docs/adr/*` files for the
   area involved.
2. Pick the next free number `NNN` and a kebab-case name; create `specs/NNN-name/` from `specs/_template/`.
3. `spec.md`: problem with evidence (file:line, command output), goal, checkable acceptance criteria (each says
   how to check it), out of scope. No implementation details.
4. `plan.md`: approach, files table, risks, exact verification commands. If more than one approach is plausible,
   say so and recommend `/probe` instead of choosing by opinion.
5. `tasks.md`: small steps, each with its own check.
6. Add the spec to the status table in `specs/README.md` and link it from the backlog item.
7. Stop. Show the acceptance criteria and wait for agreement before implementing. Write no product code.
