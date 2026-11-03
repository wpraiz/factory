# AGENTS.md

Canonical instructions for any AI coding agent working in this repository (Claude Code, Codex, Cursor, OpenCode…).
Tool-specific files (`CLAUDE.md`, `.claude/`) only add to this one; when they disagree, this file wins.

## What this is
Factory is a Windows 11 cockpit (WinUI 3, .NET 8) for people who run many projects at once, with **Odin**, a
real-time voice agent that acts on the OS through local "hands" and returns a **proof** (`[prova]`) for every action
it claims. Read `README.md` for the product and `docs/ARCHITECTURE.md` for the layers before changing code.

## Commands
| Goal | Command |
|---|---|
| Build | `dotnet build src/Factory/Factory.csproj -c Debug -p:Platform=x64` |
| Run (demo mode) | `$env:FACTORY_DEMO=1; src/Factory/bin/x64/Debug/net8.0-windows10.0.22621.0/win-x64/Factory.exe` |
| Privacy guard | `node tools/privacy-check.mjs` (staged) · `--tudo` (tree) · `--historico` (history) |
| Hands manifest check | `node tools/check-hands.mjs` |
| Local CI | `pwsh -File tools/ci-local.ps1 -SemPublicar` |
| Enable hooks (once) | `git config core.hooksPath .githooks` |

There is no unit test project yet (see `specs/002-unit-tests/`). Until it exists, "verified" means: build green,
privacy guard green, hands check green, and the behaviour exercised in demo mode.

## Boundaries (non-negotiable)
- **Stay inside this repository.** Never read or list the parent folder, `%LOCALAPPDATA%`, `%USERPROFILE%`, a real
  projects root or any Claude Code memory/session file. Session transcripts may leave the machine.
- **Always run the app with `FACTORY_DEMO=1`** in a development session, so it shows only the fictional projects
  and never scans a real projects folder.
- **No personal data, ever.** Real names, clients, e-mails, paths, conversations and money stay out of code, tests,
  fixtures, docs and commit messages. Use the fictional projects in `src/Factory/Assets/demo/`.
- **Secrets only from the environment** (`ELEVENLABS_API_KEY`, …). Never print, log, write or paste them.
- **Never bypass the guard**: no `--no-verify`, no editing `profile.manifest.json` rules to make a check pass.
- **Hands are a contract.** A hand name lives in four places: `OdinHands.cs` switch, `Assets/hands.json`,
  `NOMES` in `Assets/orb/factory-orb.js` and the tool list in `tools/create-orb-agent.mjs`. Change all four or none,
  then run `node tools/check-hands.mjs`.
- **Nothing irreversible without the owner.** Publishing, pushing agent configs to ElevenLabs (`create-orb-agent.mjs`
  without `--teste`/`--lab`), deleting user data: prepare, then stop and ask.

## How work is done here (spec-driven)
1. Pick an item from `docs/BACKLOG.md`. If it has no spec, write one first: `specs/README.md` explains the flow and
   `specs/_template/` has the files.
2. `spec.md` (what and why, acceptance criteria) → `plan.md` (how, files touched, risks) → `tasks.md` (small,
   checkable steps). Get the spec agreed before writing code.
3. Implement task by task. Each task ends with its own verification, not "should work".
4. Close with `/verify` (or the checklist in `.claude/commands/verify.md`) and record what was learned in
   `docs/LESSONS.md` **only if there is evidence** (a commit, a failing-then-passing check, a measured number).

## Conventions
- **New code, identifiers and file names in English.** UI text stays pt-BR (the product speaks Portuguese).
  Existing Portuguese comments are translated only when the file is touched for another reason
  (`specs/004-english-codebase/`).
- Area rules: `docs/rules/csharp-winui.md`, `docs/rules/orb-js.md`, `docs/rules/tools-node.md`,
  `docs/rules/privacy.md`. Decisions already taken: `docs/adr/`. Read the relevant one before touching an area.
- Small commits, imperative subject line in English, body says why. One concern per commit.

## The harness, in one table
This repo's agent setup is organised by the eight harness components described in
[Harness²](https://github.com/google-research/harness2) (system prompt, guardrails, memory, tool guidance,
subagents, skills, scripts, plugins), so each piece has one obvious home:

| Component | Here |
|---|---|
| System prompt | `AGENTS.md` (this file), `CLAUDE.md` |
| Guardrails | Boundaries above, `docs/rules/privacy.md`, `tools/privacy-check.mjs`, `.claude/settings.json` deny list |
| Memory | `docs/adr/` (decisions), `docs/LESSONS.md` (evidence-backed lessons) |
| Tool guidance | Commands table above, `docs/rules/*` |
| Subagents | `.claude/agents/` (code, UX and UI reviewers) |
| Skills / workflows | `.claude/commands/` (`/spec`, `/implement`, `/verify`, `/probe`) and `specs/` |
| Scripts | `tools/` |
| Plugins / middleware | `.githooks/` (pre-commit, pre-push), CI in `.github/workflows/` |
