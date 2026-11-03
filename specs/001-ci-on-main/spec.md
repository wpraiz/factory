# 001 · CI and local CI run on the `main` branch

## Problem
The default branch is `main`, but both CI paths still point at `master`:
- `.github/workflows/ci.yml` triggers on `push: branches: [master]`, so pushes to `main` never run CI.
- `tools/ci-local.ps1` defaults `-Base 'origin/master'`, so the pre-push hook (`.githooks/pre-push`) compares
  against a branch that does not exist.

## Goal
Every push and pull request to `main` runs the privacy guard and the build, in GitHub Actions and locally.

## Acceptance criteria
- [ ] `ci.yml` triggers on push to `main` and on pull requests; a push shows both jobs (privacy, build).
- [ ] `ci-local.ps1` uses the remote default branch (or `origin/main`) as base; `pwsh -File tools/ci-local.ps1
      -SemPublicar` exits 0 on a clean tree.
- [ ] `.githooks/pre-push` passes on a normal push.
- [ ] No other file still references `origin/master` or `branches: [master]` (`git grep master` reviewed).

## Out of scope
- Adding tests to CI (spec 002) or evals (spec 003).

## Notes
Related: `docs/rules/tools-node.md` (exit codes).
