# Lessons

Evidence-backed lessons from real work on this repo, newest first. This is the "improvement harness" idea from
[Harness²](https://github.com/google-research/harness2): what is learned on one task is kept, with its evidence,
to guide the next one. **No evidence, no entry.**

Format: `date · area · lesson` then `evidence:` (commit, command output, measured number) and `apply:` (what to do
differently next time).

---

- 2026-11-03 · tooling · A mechanical rename by word boundary also hits unrelated words with the same spelling
  (a firewall "profile" and an OBS "profile" both became the owner-profile class).
  evidence: the bulk rename needed follow-up fixes in `Models/HostSecurity.cs` and `Services/ObsHands.cs`.
  apply: after any bulk rename, review every changed line of the generic names (`git diff -U0 | grep NewName`).
- 2026-10 · orb JS · `'use strict'` together with a default parameter in the same function is a SyntaxError and left
  the orb stuck on "loading" for about two minutes.
  evidence: fixed in the private history; rule now in `docs/rules/orb-js.md`.
  apply: after any orb JS change, open the orb and confirm it leaves the loading state.
- 2026-09 · prompts · Prompt changes measured with the eval bank before shipping caught regressions that reading
  the prompt did not.
  evidence: `tools/odin-evals.mjs` runs on TEST/LAB before PROD.
  apply: no prompt change ships unless the eval score goes up or stays equal.
