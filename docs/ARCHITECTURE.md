# Architecture

## Layers

| Layer | What | Files |
|---|---|---|
| Shell | WinUI 3 window, navigation, command palette (Ctrl+K) | [MainWindow.xaml.cs](../src/Factory/MainWindow.xaml.cs), [SectionRegistry.cs](../src/Factory/Navigation/SectionRegistry.cs) |
| Sections | one Page + ViewModel per section (Odin, Projects, Board, Security, Fleet, Studio…) | `src/Factory/Views/*Page.xaml`, `src/Factory/ViewModels/*ViewModel.cs` |
| Services | everything with side effects, behind interfaces, registered in DI | [App.xaml.cs](../src/Factory/App.xaml.cs), `src/Factory/Services/` |
| Voice UI | local web app in WebView2 (`https://factory.orb/`): orb, face, pack, speech bubbles, ElevenLabs SDK | [factory-orb.js](../src/Factory/Assets/orb/factory-orb.js), [avatar.js](../src/Factory/Assets/orb/avatar.js), [pack.js](../src/Factory/Assets/orb/pack.js) |
| Agent config | prompt + tool declarations pushed to ElevenLabs (TEST → LAB → PROD) | [create-orb-agent.mjs](../tools/create-orb-agent.mjs), [agent-prompt.md](../src/Factory/Assets/orb/agent-prompt.md) |
| Profile | personal data outside the repo, one access point, manifest-driven guard | [OwnerProfile.cs](../src/Factory/Services/OwnerProfile.cs), [profile.manifest.json](../profile.manifest.json) |

## Life of a hand call

1. The voice agent decides to call a client tool (for example `desenhar` with `forma=pacman`, `quantidade=4`).
2. `factory-orb.js` receives it, marks the pack member as *working* and posts it to C# over WebView2.
3. [OdinHands.RunAsync](../src/Factory/Services/OdinHands.cs) runs the hand. Fast hands run in-process (Win32, UIA,
   GSMTC); long ones go to a headless Claude Code agent ([ClaudeCli.cs](../src/Factory/Services/ClaudeCli.cs)) behind
   a mouse gate and a deadline.
4. The hand returns text plus a `[prova]` JSON read back from the real state (pixels, window rect, media session,
   handle registry).
5. A fiscal verdict and a `hand-result/v1` record are written ([HandLog.cs](../src/Factory/Services/HandLog.cs)).
6. The result goes back to the agent. The prompt forbids claiming anything the hand did not return as done.

## Measuring the agent

- [tools/odin-evals.mjs](../tools/odin-evals.mjs) turns [real utterances](../tools/evals/hand-cases.json) into ElevenLabs
  tool-call tests and runs them against TEST/LAB/PROD agents.
- [tools/direction-score.mjs](../tools/direction-score.mjs) joins the eval score with real-world hand confirmation rates.

## Privacy by construction

`profile.manifest.json` declares what must never enter git (classes, globs, content rules, slots). The user's own
extension lives in the profile, outside git. [privacy-check.mjs](../tools/privacy-check.mjs) runs as
pre-commit, pre-push and in CI.
