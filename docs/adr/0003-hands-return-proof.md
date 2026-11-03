# ADR 0003 · Every hand returns a proof read back from the system

- Status: accepted (2026-09-27)

## Context
Voice agents tend to say they did something when they only tried, or when the tool call failed silently. For an
agent that drives the desktop, a false "done" is worse than an honest failure.

## Decision
- Each hand returns its text result plus a `[prova]`: state read back from the real system after the action
  (window title and rect, canvas pixels, media session, git commit, handle registry).
- A fiscal step marks the action confirmed or failed from the proof, and each run is logged as a `hand-result/v1`
  episode (`Services/HandLog.cs`).
- The agent prompt forbids claiming anything the hand did not return as done. Unconfigured features answer
  "not configured", never pretend.

## Consequences
- Hands take a little longer (one read-back per action) and need a way to observe their own effect.
- Confirmation rates per hand are measurable (`tools/direction-score.mjs`), so regressions show up as numbers.
- New hands are not accepted without a proof path.
