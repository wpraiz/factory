# 003 · Offline hand-eval validation in CI

## Problem
`tools/odin-evals.mjs` runs the eval bank (`tools/evals/hand-cases.json`) against live ElevenLabs agents. It needs
a key and costs money, so it never runs in CI and a broken case is only noticed in a paid run.

## Goal
An offline mode that validates the eval bank against the hands manifest, runnable anywhere, in CI.

## Acceptance criteria
- [ ] `node tools/odin-evals.mjs --offline` needs no key and makes no network call.
- [ ] It fails if a case calls a hand that is not in `src/Factory/Assets/hands.json`, uses a parameter the hand
      does not declare, or is missing `mao`/`fala`.
- [ ] It prints a one-line summary (`ok: N cases, M hands covered`) and exits non-zero on any error.
- [ ] CI runs it in the privacy job (Node is already set up there).
- [ ] Hands with no eval case are listed as a warning (coverage), not a failure.

## Out of scope
- Changing the live eval runner or the agents.
