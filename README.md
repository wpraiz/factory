# Factory

**An AI that shows the receipt.** Factory is a Windows 11 cockpit for people who run many projects at once, with
**Odin**, a real-time voice agent that drives the OS through ~33 local "hands" and **proves** every action it claims.

Free, MIT, runs on your machine. Built and used daily for 6 weeks: 270+ commits, ~28k lines.

> **Run it locally:** 3 commands below.

## Why it's different

Most agents *say* they did something. Odin's hands return a `[prova]` (proof) read back from the real system state:
the window title, the pixel on the canvas, the git commit. A fiscal then marks the action confirmed or failed. No key
configured? The feature says "not configured". It never pretends.

| Capability | Where |
|---|---|
| **Hands with proof.** Every action returns a `[prova]` read back from the real system, and a fiscal decides confirmed/failed. Each run is logged as a `hand-result/v1` episode. | [OdinHands.cs](src/Factory/Services/OdinHands.cs), [HandLog.cs](src/Factory/Services/HandLog.cs) |
| **Spotify by accessibility tree** (search, filter chip, play) plus media-session state and per-app volume, without the Web API | [SpotifyHands.cs](src/Factory/Services/SpotifyHands.cs) |
| **Planned drawing**: finds the real canvas on screen, lays out shapes, draws with the mouse, verifies pixels per item | [LiveDrawing.cs](src/Factory/Services/LiveDrawing.cs), [WindowsHands.cs](src/Factory/Services/WindowsHands.cs) |
| **"Who opened it closes it"**: registry of windows opened by hands; closing never touches the user's own windows | [OpenedWindows.cs](src/Factory/Services/OpenedWindows.cs) |
| **Eval bank.** Real utterances become tool-call tests; prompt changes ship only if the score goes up | [tools/odin-evals.mjs](tools/odin-evals.mjs), [tools/evals/](tools/evals/) |
| **A pack, not one bot** (Geri, Freki, Huginn, Muninn): each member maps to real hands, has its own voice and memory, animated in WebGL | [pack.js](src/Factory/Assets/orb/pack.js), [avatar.js](src/Factory/Assets/orb/avatar.js) |
| **Profile layer**: the repo is the product; personal data lives outside git, guarded by a pre-commit check | [profile.manifest.json](profile.manifest.json), [privacy-check.mjs](tools/privacy-check.mjs) |
| **Windows security cockpit**: collector, continuous watch, allowlist, reversible actions | [HostSecurityService.cs](src/Factory/Services/HostSecurityService.cs), [SecurityWatch.cs](src/Factory/Services/SecurityWatch.cs) |
| **Project cockpit**: scans a projects root, indexes Claude Code sessions, decision board | [ProjectScanner.cs](src/Factory/Services/ProjectScanner.cs), [SessionIndex.cs](src/Factory/Services/SessionIndex.cs) |
| **Background agents** through the Claude Code CLI, headless, with deadline and a verifying fiscal | [ClaudeCli.cs](src/Factory/Services/ClaudeCli.cs) |

## Run it

Requires Windows 10 19041+ / Windows 11, .NET 8 SDK and Node 22. No Visual Studio needed.

```powershell
git clone https://github.com/wpraiz/factory
cd factory
dotnet build src/Factory/Factory.csproj -c Debug -p:Platform=x64
```

With no projects folder configured, or with `FACTORY_DEMO=1`, it opens in **demo mode** with six fictional projects.

| Variable | What it does | Default |
|---|---|---|
| `FACTORY_DEMO` | `1` forces demo mode, even if a projects folder exists | off |
| `FACTORY_ROOT` | Folder with your projects (one subfolder each, optional `STATUS.md` + `factory.json`) | `%USERPROFILE%\Projects` if it exists, else demo |
| `FACTORY_DATA` | Where your decisions, notes and alerts are written | `<root>\.factory` |
| `FACTORY_PROFILE` | Your private profile (memory, vocabulary), outside git | `<data>\profile` |
| `FACTORY_NAME` | How Odin calls you | none |
| `ELEVENLABS_API_KEY` | Turns on Odin's voice | voice off |
| `FACTORY_VOICE` | Part of the name of your preferred ElevenLabs voice in the Studio | first cloned voice |

Background agents (the `fazer` hand) need the [Claude Code](https://claude.com/claude-code) CLI. Without it the
cockpit still runs.

## Architecture

MVVM with CommunityToolkit, services registered in [App.xaml.cs](src/Factory/App.xaml.cs), one page per section in
[SectionRegistry.cs](src/Factory/Navigation/SectionRegistry.cs). The voice UI is a local web app
(`src/Factory/Assets/orb/`) served to WebView2: JS receives ElevenLabs client tools and C# executes them.
More in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md). Section-by-section guide (Portuguese): [docs/SECTIONS.pt-BR.md](docs/SECTIONS.pt-BR.md).

## Contributing and AI agents

- [AGENTS.md](AGENTS.md): instructions for any AI coding agent (boundaries, commands, harness map).
- [CONTRIBUTING.md](CONTRIBUTING.md): build, hooks, privacy guard, how a change lands.
- [specs/](specs/): spec-driven workflow and ready-to-implement specs.
- [docs/adr/](docs/adr/): architecture decisions already taken.
- [docs/rules/](docs/rules/): rules per area (C#/WinUI, orb JS, tools, privacy).
- [docs/BACKLOG.md](docs/BACKLOG.md): open work, sized per session.
- [docs/LESSONS.md](docs/LESSONS.md): evidence-backed lessons from past work.

## FAQ

**Is it really free?** Yes. MIT, no account, no telemetry.
**Windows only?** Yes. The hands use UI Automation and Win32; that is the point.
**What does the voice cost?** You pay ElevenLabs directly for the minutes you use. The cockpit itself costs nothing.
**Where does my data go?** Nowhere. Everything is written to `FACTORY_DATA` on your disk; nothing personal goes into git.
**Can I sell it / rebrand it?** Yes, MIT.

## License

[MIT](LICENSE).
