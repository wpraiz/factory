using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Factory.Models;
using Factory.Services;

namespace Factory.ViewModels;

public sealed partial class ForgeViewModel : ObservableObject
{
    private readonly IProjectForge _forge;
    private readonly ProjectCatalog _catalog;
    private readonly ILauncher _launcher;

    public IReadOnlyList<string> Kinds => ProjectManifest.Kinds;

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string? _kind = "produto";
    [ObservableProperty] private bool _gitInit = true;
    [ObservableProperty] private bool _openAfter = true;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _log = "";

    public string RootText => ProjectScanner.Root;

    public ForgeViewModel(IProjectForge forge, ProjectCatalog catalog, ILauncher launcher)
    {
        _forge = forge;
        _catalog = catalog;
        _launcher = launcher;
    }

    [RelayCommand]
    private async Task ForgeAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        Log = "";
        var progress = new Progress<string>(m => Log += (Log.Length == 0 ? "" : "\n") + "· " + m);
        try
        {
            var dir = await _forge.CreateAsync(Name, Kind ?? "", Description, GitInit, progress);
            Log += $"\n✓ pronto: {dir}";
            await _catalog.ScanAsync();
            if (OpenAfter) _launcher.OpenClaude(dir);
            Name = "";
            Description = "";
        }
        catch (Exception ex)
        {
            Log += $"\n✗ {ex.Message}";
        }
        finally { IsBusy = false; }
    }
}
