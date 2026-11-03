using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Factory.Models;
using Factory.Services;

namespace Factory.ViewModels;

public sealed partial class ContextsViewModel : ObservableObject
{
    private readonly ProjectCatalog _catalog;
    private readonly IContextBundler _bundler;
    private readonly ILauncher _launcher;

    /// <summary>Projetos com STATUS.md, mais recente primeiro.</summary>
    public ObservableCollection<ProjectInfo> Rows { get; } = [];
    public ObservableCollection<BundleInfo> Bundles { get; } = [];

    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private bool _isBusy;

    public string OutputDir => _bundler.OutputDir;

    public ContextsViewModel(ProjectCatalog catalog, IContextBundler bundler, ILauncher launcher)
    {
        _catalog = catalog;
        _bundler = bundler;
        _launcher = launcher;
        _catalog.Changed += (_, _) => Rebuild();
        Rebuild();
    }

    public void Rebuild()
    {
        Rows.Clear();
        foreach (var p in _catalog.All.Where(p => p.HasStatus).OrderByDescending(p => p.LastTouch))
            Rows.Add(p);

        var without = _catalog.All.Count - Rows.Count;
        Summary = _catalog.IsScanning
            ? "varrendo…"
            : $"{Rows.Count} projetos com STATUS.md · {without} sem · destino: {OutputDir}";

        RefreshBundles();
    }

    private void RefreshBundles()
    {
        Bundles.Clear();
        foreach (var f in _bundler.ListBundles().Take(8))
        {
            var path = f;
            Bundles.Add(new BundleInfo
            {
                Name = Path.GetFileNameWithoutExtension(f).Replace("factory-contextos-", ""),
                Path = f,
                OpenCommand = new RelayCommand(() => _launcher.OpenFile(path)),
            });
        }
    }

    [RelayCommand]
    private async Task BundleAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var file = await _bundler.BundleAsync(_catalog.All.ToList());
            Message = $"gerado: {file}";
            RefreshBundles();
        }
        catch (Exception ex)
        {
            Message = $"erro: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        Directory.CreateDirectory(OutputDir);
        _launcher.OpenExplorer(OutputDir);
    }

    public void OpenClaude(ProjectInfo p) => _launcher.OpenClaude(p.Path);
}
