# Backlog

Real open work, each item sized for one focused session (~1–3 h). Pick one, read or write its spec
([specs/README.md](../specs/README.md)), branch, finish, commit.

## Reliability
1. **CI never runs on push.** `.github/workflows/ci.yml` triggers on `master` and `tools/ci-local.ps1` compares
   against `origin/master`, but the default branch is `main`. → [specs/001-ci-on-main](../specs/001-ci-on-main/spec.md)
2. **No unit tests.** Add an xUnit test project for the pure logic first: `Services/CommandLine.cs` (verb/token
   parsing, "long sentence is never a command"), `Services/ProjectScanner.cs` (tags), `StackDetector`. Wire it into CI.
   → [specs/002-unit-tests](../specs/002-unit-tests/spec.md)
3. **Eval bank in CI.** `tools/odin-evals.mjs` + `tools/evals/hand-cases.json` run by hand today. Add an offline mode
   (no API key: validate cases against the hand schema) and run it in CI.
   → [specs/003-offline-evals](../specs/003-offline-evals/spec.md)
4. **Hands missing from the manifest.** `seguranca` and `roteiro` exist in `OdinHands.cs` but not in `hands.json`, so
   `node tools/check-hands.mjs` fails. → [specs/005-hands-manifest-sync](../specs/005-hands-manifest-sync/spec.md)
5. **Hand result schema.** Hands log `hand-result/v1` episodes (`Services/HandLog.cs`). Write the JSON Schema and
   validate episodes in a test.

## Product
6. **Web demo.** `web-orb/` exists; make it a self-contained demo (fake hands, no key) that can be hosted statically.
7. **Demo mode polish.** Review the six fictional projects in demo mode so every section (money map, fleet, decisions)
   shows meaningful content on first run.
8. **Command line help.** `CommandLine` verbs have a `Help` string; surface it as a discoverable help panel.
9. **Settings screen for environment variables** (`FACTORY_ROOT`, `FACTORY_DATA`, `FACTORY_VOICE`): show current
   values and what is off, never showing the key itself.

## Docs (English)
10. **Translate `docs/SECTIONS.pt-BR.md`** into `docs/SECTIONS.md`.
11. **Hands catalogue.** Generate `docs/HANDS.md` from the hand definitions (name, args, what the proof reads back).

## Code health
12. **Split `OdinHands.cs`** into one file per hand family (windows, spotify, drawing, sessions) without behaviour
    change; keep the registry in one place.
13. **Build warnings.** The Debug build has 14 warnings (nullable dereferences such as `OdinHands.cs:1035`). Fix
    them, then turn on warnings-as-errors in `Directory.Build.props`.
14. **English comments and identifiers**, one area per pull request; hand names and persisted tags stay.
    → [specs/004-english-codebase](../specs/004-english-codebase/spec.md)
15. **Agent scripts change PROD when run without a flag.** Make the TEST agent the default in
    `tools/create-orb-agent.mjs` and the other agent scripts; PROD only with an explicit `--prod`.
