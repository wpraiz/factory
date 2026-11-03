# ADR 0002 · The repo is the product; the profile is the owner's life

- Status: accepted (2026-10-13)

## Context
Factory is used every day by its owner with real projects, conversations and memory, and is also published as
open source. Mixing the two leaks personal data into git and makes the product impossible to share.

## Decision
- Everything personal (memory, conversations, vocabulary, people, money) lives in a **profile folder** outside git
  (`FACTORY_PROFILE`), reached only through `Services/OwnerProfile.cs` and `tools/profile.mjs`.
- `profile.manifest.json` in the repo declares generic rules; each owner keeps a private extension next to their
  profile. `tools/privacy-check.mjs` enforces both as pre-commit, pre-push and CI.
- Without a profile the app runs as a generic product; without a projects folder it runs in demo mode with
  fictional projects.

## Consequences
- The repository can be public and can be used in recorded AI sessions without exposing anyone.
- Features that need personal context read it at runtime from the profile; tests and docs use demo data.
- Warnings from the guard need a human decision; blocks cannot be bypassed.
