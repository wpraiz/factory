# Contributing

## Prerequisites
- Windows 10 19041+ or Windows 11
- .NET 8 SDK
- Node 22

## Build and hooks
```powershell
dotnet build src/Factory/Factory.csproj -c Debug -p:Platform=x64
git config core.hooksPath .githooks   # once per clone: privacy guard on commit, local CI on push
```
Run the app with `FACTORY_DEMO=1` to get demo mode with fictional projects (it is also the default when no
projects folder exists). Data goes to `FACTORY_DATA` (default `<root>\.factory`).

## Privacy guard
`tools/privacy-check.mjs` runs on every commit, push and in CI. Blocks cannot be bypassed; warnings need a human
look. No personal data, no secrets, no real paths in code, tests or docs. Rules: [docs/rules/privacy.md](docs/rules/privacy.md).

## How a change lands
1. Pick an item from [docs/BACKLOG.md](docs/BACKLOG.md) and write or read its spec ([specs/README.md](specs/README.md)).
2. Branch from `main`, implement task by task.
3. Open a pull request using the template; CI (privacy + build) must be green.
4. Merge into `main`.

## Commits
Imperative subject line in English ("Add offline eval mode"), body says why. One concern per commit.

## AI agents
Instructions for coding agents live in [AGENTS.md](AGENTS.md) (Claude Code also reads `CLAUDE.md` and `.claude/`).
Area rules are in [docs/rules/](docs/rules/), decisions in [docs/adr/](docs/adr/).
