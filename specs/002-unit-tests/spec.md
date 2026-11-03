# 002 · Unit test project for pure logic

## Problem
There is no test project. Parsing and classification logic that the voice agent and the command line depend on is
only checked by hand, so regressions show up in use (for example a long sentence once became a command).

## Goal
A fast xUnit project that covers the pure logic and runs in CI.

## Acceptance criteria
- [ ] `tests/Factory.Tests/` (xUnit, net8.0) builds with `dotnet test` without Visual Studio.
- [ ] `Services/CommandLine.cs`: verbs, token match, filler words, "sentence longer than 3 words without a verb is
      never a command", accent folding.
- [ ] `Services/ProjectScanner.cs` tag rules and `Services/StackDetector.cs` detection on fixture folders built in a
      temp directory (no real paths).
- [ ] Logic that needs the WinUI runtime is either extracted behind a seam or explicitly left out with a note.
- [ ] CI runs the tests after the build job.

## Out of scope
- UI tests, hand execution against the real desktop.

## Notes
If the app project's target framework makes direct referencing hard, probe two options (`/probe`): a shared
class library vs. linking source files into the test project.
