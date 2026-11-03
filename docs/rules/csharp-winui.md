# Rules: C# / WinUI 3

Applies to `src/Factory/**/*.cs` and `*.xaml`.

## Structure
- MVVM with CommunityToolkit.Mvvm: `[ObservableProperty]`, `[RelayCommand]`. No logic in code-behind beyond view
  wiring.
- Side effects live in `Services/`, behind an interface when a ViewModel uses them, registered in `App.xaml.cs`.
- New section = service → ViewModel → Page → one entry in `Navigation/SectionRegistry.cs`. Section tags
  (`"seguranca"`, `"vida"`, …) are persisted identifiers: do not rename them.
- Paths come from `Services/AppPaths.cs` (`Root`, `Dados`, demo mode). Personal data paths come only from
  `Services/OwnerProfile.cs`. Never hardcode a drive letter or user folder.

## Build
- Build with the dotnet CLI, no Visual Studio. `Directory.Build.props` (MSIX tooling on, PriGen off) is required;
  see `docs/adr/0001-dotnet-cli-build.md`.
- Windows App SDK stays on 1.7.x until an ADR says otherwise.
- New code compiles without new warnings. Nullable is on.

## Style
- New identifiers, files and comments in English. UI strings in pt-BR.
- Theme resources from `Themes/Cyber.xaml` (styles `Cyber*`), spacing on the 4/8/12/16/20/28 scale, no inline
  repeated styles. Details in `docs/ui-guide.md`.
- Async all the way; no `.Result`/`.Wait()` on the UI thread. Long work shows state (running, done, failed).

## Hands
- A hand returns text **and** a proof read back from the system (window rect, pixel, media session, git). If it
  cannot prove it, it says so; it never reports success on intent. See `docs/adr/0003-hands-return-proof.md`.
- Hands that open things register what they opened (`Services/OpenedWindows.cs`); closing only touches that list.
