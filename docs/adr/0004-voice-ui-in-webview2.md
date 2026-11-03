# ADR 0004 · The voice UI is a local web app inside WebView2

- Status: accepted (2026-09)

## Context
The voice agent runs on ElevenLabs Conversational AI, whose client SDK and WebGL rendering are web-first. The rest
of the app is native WinUI 3 and owns every side effect on the OS.

## Decision
- The orb, the avatar, the pack and the speech bubbles are plain ES modules in `src/Factory/Assets/orb/`, served to
  WebView2 at `https://factory.orb/`.
- JavaScript receives client tool calls and renders state; it never acts on the system. C# (`OdinHands`) executes
  and returns the proof.
- `orb.js` and `orb-gl.js` are shared with another project and are not edited here; new behaviour goes in new
  modules.

## Consequences
- The same UI runs in a browser for the web demo (`web-orb/`).
- The JS/C# boundary is the hand contract (see `AGENTS.md`): four places must agree, guarded by
  `tools/check-hands.mjs`.
