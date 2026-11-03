# Specs

Every non-trivial change starts here. One folder per change: `specs/NNN-short-name/`.

## Flow
1. **Spec** (`spec.md`): the problem, who it is for, what "done" means as checkable acceptance criteria, and what
   is out of scope. No implementation details.
2. **Plan** (`plan.md`): files to touch, approach, alternatives considered, risks, how it will be verified.
3. **Tasks** (`tasks.md`): small steps, each with its own check. Ticked as they land.
4. **Implement**: one task at a time, commit per task when it makes sense.
5. **Verify**: every acceptance criterion checked with evidence (command output, screenshot path, commit). Then
   record lessons in `docs/LESSONS.md` if, and only if, there is evidence behind them.

## When the right approach is unclear: probe
Borrowed from the Propose–Probe–Compose step in [Harness²](https://github.com/google-research/harness2):
propose two or three candidate approaches in `plan.md`, try each on a throwaway branch against the **same**
acceptance check, compare what actually happened, and compose the final plan from the evidence instead of from
opinion. `/probe` runs this.

## Status
| Spec | Title | Status |
|---|---|---|
| [001](001-ci-on-main/spec.md) | CI and local CI run on the `main` branch | ready |
| [002](002-unit-tests/spec.md) | Unit test project for pure logic | ready |
| [003](003-offline-evals/spec.md) | Offline hand-eval validation in CI | ready |
| [004](004-english-codebase/spec.md) | English comments and identifiers | ready |
| [005](005-hands-manifest-sync/spec.md) | Hands missing from the manifest | ready |

Templates: [`_template/`](_template/).
