using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Factory.Models;
using Factory.Services;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Factory.ViewModels;

/// <summary>
/// APPS — a primeira tela: grid dos PROJETOS nossos que rodam, escolhidos por ele (ESCOLHER),
/// em faixas com nome e cor (grupo, renomeavel no proprio tile), um clique abre, LED mostra o
/// que esta rodando, Ctrl+1..9 abre pela ordem, botao direito da pasta/terminal/Claude.
/// BRIEFING DO DIA: proximas acoes em voz, gerado 1x por dia (cache por data), toca no rodape.
/// </summary>
public sealed partial class AppsViewModel : ObservableObject
{
    private readonly IAppLibrary _lib;
    private readonly ProjectCatalog _catalog;
    private readonly IAudioStudio _audio;
    private readonly AudioPlayer _player;
    private readonly ILauncher _launcher;
    private CancellationTokenSource? _candCts;
    private bool _suspendRebuild;

    /// <summary>Ordem plana do grid (a que e salva). As faixas sao derivadas dela.</summary>
    public ObservableCollection<AppTile> Tiles { get; } = [];
    public ObservableCollection<AppGroup> Groups { get; } = [];
    public ObservableCollection<AppTile> Candidates { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditLabel))]
    private bool _isEditing;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _loadingCandidates;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _briefingStatus = "";
    [ObservableProperty] private string _headline = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredCandidates))]
    private string _filter = "";

    public string EditLabel => IsEditing ? "✓ PRONTO" : "ESCOLHER APPS";
    public string ConfigFile => _lib.ConfigFile;

    public IEnumerable<AppTile> FilteredCandidates =>
        string.IsNullOrWhiteSpace(Filter)
            ? Candidates
            : Candidates.Where(c => c.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase) || c.Subtitle.Contains(Filter, StringComparison.OrdinalIgnoreCase));

    public AppsViewModel(IAppLibrary lib, ProjectCatalog catalog, IAudioStudio audio, AudioPlayer player, ILauncher launcher)
    {
        _lib = lib;
        _catalog = catalog;
        _audio = audio;
        _player = player;
        _launcher = launcher;
        _catalog.Changed += (_, _) => UpdateHeadline();
        Tiles.CollectionChanged += (_, _) => { if (!_suspendRebuild) RebuildGroups(); };
        Load();
        UpdateHeadline();
    }

    // ---------- grid ----------

    private void Load()
    {
        var list = _lib.Load();
        var seeded = false;
        if (list.Count == 0) { list = _lib.Seed(); seeded = true; }
        _suspendRebuild = true;
        Tiles.Clear();
        foreach (var t in list) Tiles.Add(t);
        _suspendRebuild = false;
        RebuildGroups();
        Status = seeded
            ? $"primeira vez: {Tiles.Count} projetos que rodam. ESCOLHER APPS pra montar o seu grid."
            : $"{Tiles.Count} apps · clique abre · Ctrl+1…9 abre pela ordem · botao direito: pasta, terminal, Claude";
        // salva sempre: a limpeza do Load (programas do Windows fora) e os grupos padrao ficam persistidos
        _ = _lib.SaveAsync(Tiles);
        _ = LoadIconsAsync(Tiles.ToList());
        _ = RefreshRunningAsync();
    }

    /// <summary>Faixas na ordem em que aparecem no grid; cor por posicao da faixa; tile herda a cor da faixa.</summary>
    public void RebuildGroups()
    {
        var names = new List<string>();
        foreach (var t in Tiles)
        {
            if (string.IsNullOrWhiteSpace(t.Group)) t.Group = AppGroup.Default;
            t.Group = t.Group.Trim().ToUpperInvariant();
            if (!names.Contains(t.Group)) names.Add(t.Group);
        }
        var options = names.ToList();
        Groups.Clear();
        var hotkey = 0;
        for (var i = 0; i < names.Count; i++)
        {
            var g = new AppGroup { Name = names[i], Accent = AppGroup.Palette[i % AppGroup.Palette.Length] };
            foreach (var t in Tiles.Where(t => t.Group == g.Name))
            {
                t.Accent = g.Accent;
                t.GroupOptions = options;
                t.Hotkey = ++hotkey <= 9 ? hotkey : 0;
                g.Tiles.Add(t);
            }
            Groups.Add(g);
        }
    }

    /// <summary>Do seletor no tile (modo ESCOLHER): nome novo cria faixa nova.</summary>
    public void Regroup(AppTile t, string? group)
    {
        var name = (group ?? "").Trim().ToUpperInvariant();
        if (name.Length == 0 || name == t.Group) return;
        t.Group = name;
        RebuildGroups();
        _ = _lib.SaveAsync(Tiles);
        Status = $"{t.Name} → {name}";
    }

    private void UpdateHeadline()
    {
        var now = DateTime.Now;
        var pt = CultureInfo.GetCultureInfo("pt-BR");
        var active = _catalog.All.Count(p => (DateTimeOffset.Now - p.LastTouch) <= TimeSpan.FromDays(7));
        var day = now.ToString("dddd, d 'de' MMMM", pt);
        day = char.ToUpper(day[0], pt) + day[1..];
        Headline = _catalog.IsScanning ? day : $"{day}  ·  {active} projetos ativos na semana";
    }

    public async Task RefreshRunningAsync()
    {
        try { await _lib.RefreshRunningAsync(Tiles.ToList()); }
        catch (Exception) { }
    }

    private async Task LoadIconsAsync(IReadOnlyList<AppTile> tiles)
    {
        foreach (var t in tiles)
        {
            if (t.Icon is not null) continue;
            try
            {
                using var stream = await _lib.IconStreamAsync(t);
                if (stream is null) continue;
                var bmp = new BitmapImage { DecodePixelWidth = 96 };
                await bmp.SetSourceAsync(stream);
                t.Icon = bmp;
            }
            catch (Exception) { /* fica o glyph */ }
        }
    }

    // ---------- abrir ----------

    [RelayCommand]
    private void Launch(AppTile? tile)
    {
        if (tile is null) return;
        if (IsEditing) { Status = "em ESCOLHER APPS o clique nao abre — aperte ✓ PRONTO primeiro"; return; }
        var target = tile.ResolvedTarget.Length > 0 ? tile.ResolvedTarget : tile.Target;
        if (tile.Kind != AppTile.KindUrl && !File.Exists(target) && !Directory.Exists(target))
        {
            Status = $"{tile.Name}: alvo nao existe mais ({target}) — tire do grid em ESCOLHER APPS ou re-adicione com + ARQUIVO";
            return;
        }
        try
        {
            _lib.Launch(tile);
            Status = $"abrindo {tile.Name}…";
            _ = DelayedRefreshAsync();
        }
        catch (Exception ex) { Status = $"{tile.Name}: nao abriu ({ex.Message}). Confira o caminho em ESCOLHER APPS."; }
    }

    /// <summary>Ctrl+1..9: pela ordem do grid (faixa a faixa).</summary>
    public void LaunchIndex(int oneBased)
    {
        var flat = Groups.SelectMany(g => g.Tiles).ToList();
        if (oneBased >= 1 && oneBased <= flat.Count) Launch(flat[oneBased - 1]);
    }

    private static string DirOf(AppTile t)
    {
        if (t.WorkingDir.Length > 0 && Directory.Exists(t.WorkingDir)) return t.WorkingDir;
        var target = t.ResolvedTarget.Length > 0 ? t.ResolvedTarget : t.Target;
        if (Directory.Exists(target)) return target;
        return Path.GetDirectoryName(target) ?? ProjectScanner.Root;
    }

    public void OpenFolder(AppTile t) => Try(() => _launcher.OpenExplorer(DirOf(t)), $"Explorer em {DirOf(t)}");
    public void OpenTerminal(AppTile t) => Try(() => _launcher.OpenTerminal(DirOf(t)), $"terminal em {DirOf(t)}");
    public void OpenClaude(AppTile t) => Try(() => _launcher.OpenClaude(DirOf(t)), $"claude em {DirOf(t)}");

    private void Try(Action act, string what)
    {
        try { act(); Status = "abrindo " + what; }
        catch (Exception ex) { Status = $"nao abriu {what}: {ex.Message}"; }
    }

    public void CopyPath(AppTile t)
    {
        var p = t.ResolvedTarget.Length > 0 ? t.ResolvedTarget : t.Target;
        _launcher.CopyToClipboard(p);
        Status = $"copiado: {p}";
    }

    private async Task DelayedRefreshAsync()
    {
        await Task.Delay(2500);
        await RefreshRunningAsync();
    }

    // ---------- escolher ----------

    [RelayCommand]
    private async Task ToggleEditAsync()
    {
        IsEditing = !IsEditing;
        foreach (var t in Tiles) t.Editing = IsEditing;
        if (IsEditing) await LoadCandidatesAsync();
        else
        {
            _candCts?.Cancel();
            await _lib.SaveAsync(Tiles);
            Status = $"{Tiles.Count} apps em {Groups.Count} faixas · salvo em {Path.GetFileName(ConfigFile)}";
        }
    }

    private async Task LoadCandidatesAsync()
    {
        _candCts?.Cancel();
        var cts = _candCts = new CancellationTokenSource();
        LoadingCandidates = true;
        try
        {
            var list = await _lib.CandidatesAsync(_catalog.All.ToList(), cts.Token);
            var keys = new HashSet<string>(Tiles.Select(t => t.Key));
            Candidates.Clear();
            foreach (var c in list)
            {
                c.Enabled = keys.Contains(c.Key);
                Candidates.Add(c);
            }
            OnPropertyChanged(nameof(FilteredCandidates));
            Status = $"{Candidates.Count} candidatos (so projetos nossos) · marque o que quer · + ARQUIVO pra qualquer exe/script · faixa: seletor no tile";
            _ = LoadIconsAsync(Candidates.ToList());
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Status = "candidatos: " + ex.Message; }
        finally { LoadingCandidates = false; }
    }

    public void ToggleCandidate(AppTile c)
    {
        var existing = Tiles.FirstOrDefault(t => t.Key == c.Key);
        if (c.Enabled && existing is null) { c.Editing = true; Tiles.Add(c); }
        else if (!c.Enabled && existing is not null) Tiles.Remove(existing);
        _ = _lib.SaveAsync(Tiles);
    }

    public void AddFromFile(string path)
    {
        var t = _lib.FromFile(path);
        if (Tiles.Any(x => x.Key == t.Key)) { Status = $"{t.Name} ja esta no grid"; return; }
        t.Editing = IsEditing;
        Tiles.Add(t);
        _ = LoadIconsAsync([t]);
        _ = _lib.SaveAsync(Tiles);
        Status = $"{t.Name} adicionado em {t.Group}";
    }

    [RelayCommand]
    private void MoveUp(AppTile? t) => Move(t, -1);

    [RelayCommand]
    private void MoveDown(AppTile? t) => Move(t, +1);

    /// <summary>Move dentro da faixa (troca com o vizinho do mesmo grupo na ordem plana).</summary>
    private void Move(AppTile? t, int delta)
    {
        if (t is null) return;
        var i = Tiles.IndexOf(t);
        if (i < 0) return;
        var j = i + delta;
        while (j >= 0 && j < Tiles.Count && Tiles[j].Group != t.Group) j += delta;
        if (j < 0 || j >= Tiles.Count) return;
        Tiles.Move(i, j);
        _ = _lib.SaveAsync(Tiles);
    }

    [RelayCommand]
    private void Remove(AppTile? t)
    {
        if (t is null) return;
        Tiles.Remove(t);
        var c = Candidates.FirstOrDefault(x => x.Key == t.Key);
        if (c is not null) c.Enabled = false;
        _ = _lib.SaveAsync(Tiles);
    }

    [RelayCommand]
    private void ResetToSeed()
    {
        try { if (File.Exists(ConfigFile)) File.Copy(ConfigFile, ConfigFile + ".bak", overwrite: true); } catch (Exception) { }
        _suspendRebuild = true;
        Tiles.Clear();
        foreach (var t in _lib.Seed()) { t.Editing = IsEditing; Tiles.Add(t); }
        _suspendRebuild = false;
        RebuildGroups();
        _ = _lib.SaveAsync(Tiles);
        _ = LoadIconsAsync(Tiles.ToList());
        Status = "grid voltou ao padrao (projetos que existem no disco)";
    }

    // ---------- briefing do dia ----------

    [RelayCommand]
    private async Task DailyBriefingAsync()
    {
        if (IsBusy) return;
        var stem = $"briefing-dia-{DateTime.Now:yyyyMMdd}";
        var cached = _audio.FindOutput(stem);
        if (cached is not null)
        {
            _player.Play(cached, "Briefing do dia");
            BriefingStatus = "tocando o briefing de hoje (ja gerado)";
            return;
        }
        if (!_audio.Configured) { BriefingStatus = "ELEVENLABS_API_KEY nao definida — defina e reabra o app"; return; }

        IsBusy = true;
        BriefingStatus = "montando o texto…";
        try
        {
            var text = BuildDailyText();
            var voice = await _audio.DefaultVoiceAsync();
            if (voice is null) { BriefingStatus = "nenhuma voz disponivel na conta"; return; }
            BriefingStatus = $"gerando na voz {voice.Name}… ({text.Length} chars, 1x por dia)";
            var file = await _audio.SpeakAsync(text, voice.Id, stem);
            _player.Play(file, "Briefing do dia");
            BriefingStatus = "tocando · amanha gera de novo";
        }
        catch (Exception ex) { BriefingStatus = "erro: " + ex.Message; }
        finally { IsBusy = false; }
    }

    /// <summary>Texto falado: data, ativos, proxima acao dos mais recentes, bloqueios. Sem claude -p: zero custo.</summary>
    private string BuildDailyText()
    {
        var pt = CultureInfo.GetCultureInfo("pt-BR");
        var now = DateTime.Now;
        var sb = new StringBuilder();
        var greeting = now.Hour < 12 ? "Bom dia" : now.Hour < 18 ? "Boa tarde" : "Boa noite";
        sb.Append($"{greeting}{(AppPaths.NomeUsuario.Length > 0 ? ", " + AppPaths.NomeUsuario : "")}. Hoje é {now.ToString("dddd, d 'de' MMMM", pt)}. ");

        var all = _catalog.All.ToList();
        var active = all.Where(p => (DateTimeOffset.Now - p.LastTouch) <= TimeSpan.FromDays(7)).OrderByDescending(p => p.LastTouch).ToList();
        sb.Append(active.Count == 0 ? "Nenhum projeto tocado nesta semana. " : $"{active.Count} projetos ativos na semana: {Join(active.Take(6).Select(p => Speak(p.Name)))}. ");

        var next = all.Where(p => p.HasNextAction).OrderByDescending(p => p.LastTouch).Take(6).ToList();
        if (next.Count > 0)
        {
            sb.Append("Próximas ações. ");
            foreach (var p in next) sb.Append($"{Speak(p.Name)}: {Clean(FirstLine(p.NextAction))}. ");
        }
        else sb.Append("Nenhum projeto com próxima ação registrada no STATUS. ");

        var blocked = all.Where(p => p.HasBlockers).OrderByDescending(p => p.LastTouch).Take(4).ToList();
        if (blocked.Count > 0)
        {
            sb.Append("Bloqueios. ");
            foreach (var p in blocked) sb.Append($"{Speak(p.Name)}: {Clean(FirstLine(p.Blockers))}. ");
        }

        var noStatus = active.Count(p => !p.HasStatus);
        if (noStatus > 0) sb.Append($"{noStatus} dos ativos ainda não têm STATUS. ");
        sb.Append("É isso. Bora.");
        return sb.ToString();
    }

    private static string Join(IEnumerable<string> items)
    {
        var l = items.ToList();
        return l.Count switch { 0 => "", 1 => l[0], _ => string.Join(", ", l.Take(l.Count - 1)) + " e " + l[^1] };
    }

    private static string Speak(string name) => name.Replace('-', ' ').Replace('_', ' ');

    private static string Clean(string s) =>
        s.Replace("**", "").Replace("`", "").Replace("→", "vira").Replace("\\", " ").Trim().TrimEnd('.');

    private static string FirstLine(string s)
    {
        var i = s.IndexOf('\n');
        return (i < 0 ? s : s[..i]).Trim().TrimStart('-', '*', ' ');
    }
}
