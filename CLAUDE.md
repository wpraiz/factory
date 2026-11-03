@AGENTS.md

# Claude Code specifics

- Permissions for this repo live in `.claude/settings.json`: build, privacy guard and git read commands are allowed;
  reading outside the repo and pushing are denied. Do not widen them inside a session.
- Launch the app only with `FACTORY_DEMO=1` (see the Commands table in `AGENTS.md`).
- Workflows: `/spec <backlog item>`, `/implement <spec folder>`, `/probe <question>`, `/verify`.
- Reviewers: `code-reviewer` after any C# change, `ux-reviewer` then `ui-reviewer` before building a changed screen.
- Background agents launched by the app itself (`Services/ClaudeCli.cs`) are product behaviour, not part of a
  development session; do not run the `fazer` hand from a session.
