using System.Collections.ObjectModel;
using Factory.Models;
using Microsoft.UI.Dispatching;

namespace Factory.Services;

/// <summary>
/// Estado compartilhado entre as paginas: a lista viva de projetos. Construido na
/// thread de UI (MainWindow) — toda mutacao de <see cref="All"/> passa pelo dispatcher.
/// </summary>
public sealed class ProjectCatalog
{
    private readonly IProjectScanner _scanner;
    private readonly IGitProbe _git;
    private readonly ISessionIndex _index;
    private readonly DispatcherQueue _dispatcher;
    private readonly SemaphoreSlim _gitGate = new(4);

    public ObservableCollection<ProjectInfo> All { get; } = [];
    public bool IsScanning { get; private set; }

    /// <summary>Disparado na thread de UI apos scan (inicio e fim).</summary>
    public event EventHandler? Changed;

    /// <summary>Progresso do indice de sessoes (thread de UI). String vazia = pronto/ocioso.</summary>
    public event EventHandler<string>? IndexProgress;

    public ProjectCatalog(IProjectScanner scanner, IGitProbe git, ISessionIndex index)
    {
        _scanner = scanner;
        _git = git;
        _index = index;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _index.Updated += (_, _) => _dispatcher.TryEnqueue(() => IndexProgress?.Invoke(this, _index.Progress));
    }

    public async Task ScanAsync()
    {
        if (IsScanning) return;
        IsScanning = true;
        Changed?.Invoke(this, EventArgs.Empty);

        List<ProjectInfo> list;
        try { list = await _scanner.ScanAsync(); }
        catch (Exception) { list = []; }

        All.Clear();
        foreach (var p in list) All.Add(p);

        IsScanning = false;
        Changed?.Invoke(this, EventArgs.Empty);

        // Enriquecimentos lentos depois da lista aparecer: git e indice de sessoes.
        foreach (var p in list.Where(p => p.HasGit))
            _ = ProbeGitAsync(p);
        _ = EnrichFromIndexAsync(list);
    }

    private async Task EnrichFromIndexAsync(List<ProjectInfo> list)
    {
        try
        {
            await _index.EnsureBuiltAsync();
            var counts = await Task.Run(() => list.Select(p =>
            {
                var own = _index.ForProject(p.Path);
                var mentions = _index.Mentions(TrailService.AliasRegex(p.Name), p.Path);
                return (p, own.Count, own.Count > 0 ? own[0].LastWrite : (DateTimeOffset?)null, mentions.Count);
            }).ToList());

            _dispatcher.TryEnqueue(() =>
            {
                foreach (var (p, own, last, mentions) in counts)
                {
                    p.SessionCount = own;
                    p.LastSession = last;
                    p.MentionCount = mentions;
                }
            });
        }
        catch (Exception) { /* indice e enriquecimento; a lista ja esta de pe */ }
    }

    private async Task ProbeGitAsync(ProjectInfo p)
    {
        await _gitGate.WaitAsync();
        try
        {
            var r = await _git.ProbeAsync(p.Path);
            if (r is null) return;
            _dispatcher.TryEnqueue(() =>
            {
                p.GitBranch = r.Value.Branch;
                p.GitDirty = r.Value.Dirty;
            });
        }
        finally { _gitGate.Release(); }
    }

    public void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
