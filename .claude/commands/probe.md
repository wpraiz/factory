---
description: Propose–Probe–Compose — compare candidate approaches by evidence, not opinion
argument-hint: <spec folder or question>
---

Probe: $ARGUMENTS

Based on the Propose–Probe–Compose step from Harness² (github.com/google-research/harness2).

1. **Propose.** Write 2–3 genuinely different candidate approaches in `plan.md`, each in two or three lines, and
   the one check all of them will be measured against (taken from the acceptance criteria: a command, a test, a
   measured number).
2. **Probe.** For each candidate, on its own throwaway branch (`probe/NNN-a`, `probe/NNN-b`…), implement just enough
   to run the check. Run it and record what actually happened: pass/fail, output, size of the diff, surprises.
3. **Compare.** Fill the "Alternatives probed" table in `plan.md`: option · what happened on the same check · kept?
4. **Compose.** Write the final approach from the evidence; it may combine parts of several candidates. Update
   `tasks.md` accordingly.
5. Delete the probe branches (they were never pushed). If the comparison taught something reusable, add it to
   `docs/LESSONS.md` with its evidence.
