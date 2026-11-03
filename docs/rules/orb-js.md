# Rules: Odin voice UI (JavaScript)

Applies to `src/Factory/Assets/orb/**` and `web-orb/**`.

- Plain ES modules, no bundler, no framework. It runs inside WebView2 at `https://factory.orb/` and must keep
  working offline except for the ElevenLabs SDK.
- `orb.js` and `orb-gl.js` are shared with another project: do not edit them; add a new module instead
  (that is how `avatar.js` and `pack.js` were added).
- JS never performs an action itself. It receives the client tool call, posts it to C# and renders state; C#
  executes and returns the proof. See `docs/ARCHITECTURE.md` ("Life of a hand call").
- A new hand name must be added to `NOMES` in `factory-orb.js` together with the other three places listed in
  `AGENTS.md`, then `node tools/check-hands.mjs`.
- No `'use strict'` with default parameters in the same function (it is a SyntaxError and once left the orb stuck
  on "loading"). After any JS change, open the orb and confirm it leaves the loading state.
- Animation: 150–350 ms, only on state change; nothing that loops forever or blocks input.
