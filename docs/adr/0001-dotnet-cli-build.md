# ADR 0001 · Build WinUI 3 with the dotnet CLI, no Visual Studio

- Status: accepted (2026-09-04)

## Context
The app must build on a plain Windows machine and in CI with only the .NET 8 SDK. The classic PRI generation
(MrtCore PriGen) used by WinUI 3 needs MSBuild tasks that only ship inside Visual Studio.

## Decision
`Directory.Build.props` at the repo root sets `EnableMsixTooling=true` and `EnablePriGenTooling=false`, so the
Windows App SDK imports its own MSIX tooling whose PRI tasks run on MSBuild Core. It has to be in
`Directory.Build.props` (evaluated before the package props), not in the `.csproj`. The Windows App SDK stays on
1.7.x.

## Consequences
- `dotnet build src/Factory/Factory.csproj -c Debug -p:Platform=x64` works with no Visual Studio, locally and in CI.
- Features that need Windows App SDK 2.0 (for example Acrylic on cards) wait for a new ADR.
