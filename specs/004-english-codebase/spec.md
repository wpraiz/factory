# 004 · English comments and identifiers

## Problem
File, class and tool names are already in English, but most comments and many members (`NomeUsuario`,
`VozPreferida`, `Dados`, `Raiz`, …) are still in Portuguese. That limits who can contribute.

## Goal
Code readable by any English-speaking contributor, while the product UI stays in pt-BR.

## Acceptance criteria
- [ ] Done incrementally, one area per pull request (Services, ViewModels, Views, tools, orb), never a single
      repo-wide rewrite.
- [ ] Public members renamed with every reference updated in the same change; build and privacy guard green.
- [ ] Comments translated keeping their meaning, dates and reasons; nothing deleted for being "obvious".
- [ ] UI strings (`Text=`, user-facing messages) unchanged.
- [ ] **Not renamed:** hand names (`fazer`, `desenhar`, …), persisted section tags, JSON property names and data
      file names on disk. They are contracts with agent configs, evals and existing user data.

## Out of scope
- Translating the UI or the Odin prompt (`agent-prompt.md`), which are pt-BR by design.
