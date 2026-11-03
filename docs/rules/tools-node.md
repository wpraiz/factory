# Rules: tools (Node, PowerShell, Python)

Applies to `tools/**`, `odin-wake/**`, `web-orb/server.mjs`.

- Node 22+, ES modules, standard library only unless a dependency is clearly worth it.
- Every script starts with a comment block: what it does, usage lines, what it reads and what it writes.
- Read-only by default. A script that changes something outside the repo (ElevenLabs agents, files under
  `FACTORY_DATA`) says so in its header. **Careful:** `create-orb-agent.mjs` and friends change the PROD agent when
  run without a flag; in a development session always pass `--teste` or `--lab`. (Making TEST the default is open
  work.)
- Paths from `FACTORY_ROOT` / `FACTORY_DATA` / `FACTORY_PROFILE`, never hardcoded.
- Keys from the environment only. Fail with a clear message when missing ("set ELEVENLABS_API_KEY"), never prompt.
- Exit code 0 = ok, non-zero = failure, so CI and hooks can use them.
- File names in English kebab-case (`create-orb-agent.mjs`).
