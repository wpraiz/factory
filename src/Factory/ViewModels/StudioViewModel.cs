using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Factory.Models;
using Factory.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Factory.ViewModels;

/// <summary>
/// ESTUDIO: pega um texto (STATUS, briefing, contextos, o que voce colar) e vira audio na
/// voz escolhida. Cada geracao gasta credito da ElevenLabs — nunca dispara sozinha.
/// </summary>
public sealed partial class StudioViewModel : ObservableObject
{
    private readonly IAudioStudio _audio;
    private readonly ProjectCatalog _catalog;
    private readonly ILauncher _launcher;
    private readonly IContextBundler _bundler;
    private readonly AudioPlayer _player;
    private CancellationTokenSource? _cts;

    public ObservableCollection<VoiceInfo> Voices { get; } = [];
    public ObservableCollection<Chip> Outputs { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CharsText))]
    private string _text = "";

    [ObservableProperty] private VoiceInfo? _selectedVoice;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _lastFile = "";
    [ObservableProperty] private bool _hasLastFile;
    [ObservableProperty] private string _fileStem = "factory";

    public bool Configured => _audio.Configured;
    public string CharsText => $"{Text.Length} / {AudioStudio.MaxChars} caracteres" + (Text.Length > AudioStudio.MaxChars ? " — vai cortar" : "");
    public string OutputDir => _audio.OutputDir;

    public StudioViewModel(IAudioStudio audio, ProjectCatalog catalog, ILauncher launcher, IContextBundler bundler, AudioPlayer player)
    {
        _audio = audio;
        _catalog = catalog;
        _launcher = launcher;
        _bundler = bundler;
        _player = player;
        RefreshOutputs();
        _ = LoadVoicesAsync();
    }

    private ProjectsViewModel Projects => App.Services.GetRequiredService<ProjectsViewModel>();

    private async Task LoadVoicesAsync()
    {
        if (!Configured) { Status = "ELEVENLABS_API_KEY nao definida — defina a variavel e reabra o app"; return; }
        try
        {
            var list = await _audio.VoicesAsync();
            Voices.Clear();
            foreach (var v in list) Voices.Add(v);
            SelectedVoice = Voices.FirstOrDefault(v => AppPaths.VozPreferida.Length > 0 && v.Name.Contains(AppPaths.VozPreferida, StringComparison.OrdinalIgnoreCase))
                            ?? Voices.FirstOrDefault(v => v.Category is "cloned" or "professional")
                            ?? Voices.FirstOrDefault();
            Status = $"{Voices.Count} vozes · saida em {OutputDir}";
        }
        catch (Exception ex) { Status = "erro ao listar vozes: " + ex.Message; }
    }

    // ---------- fontes de texto ----------

    [RelayCommand]
    private void FromStatus()
    {
        var p = Projects.Selected;
        if (p is null) { Status = "selecione um projeto em PROJETOS primeiro"; return; }
        var f = Path.Combine(p.Path, "STATUS.md");
        if (!File.Exists(f)) { Status = $"{p.Name} nao tem STATUS.md"; return; }
        Text = Humanize(File.ReadAllText(f));
        FileStem = $"{p.Name}-status";
        Status = $"STATUS.md de {p.Name} carregado";
    }

    [RelayCommand]
    private void FromBriefing()
    {
        var pv = Projects;
        if (!pv.HasBriefing) { Status = "gere um briefing (◈) numa sessao primeiro"; return; }
        Text = pv.BriefingText;
        FileStem = $"{pv.Selected?.Name ?? "projeto"}-briefing";
        Status = "briefing carregado";
    }

    [RelayCommand]
    private void FromContexts()
    {
        var latest = _bundler.ListBundles().FirstOrDefault();
        if (latest is null) { Status = "nenhum bundle em .factory\\contextos — gere em CONTEXTOS"; return; }
        var raw = File.ReadAllText(latest);
        // so o indice (proxima acao de cada projeto): o bundle inteiro nao cabe em audio
        var idx = raw.IndexOf("## Indice", StringComparison.OrdinalIgnoreCase);
        var end = raw.IndexOf("\n---", idx < 0 ? 0 : idx, StringComparison.Ordinal);
        Text = Humanize(idx >= 0 && end > idx ? raw[idx..end] : raw);
        FileStem = "contextos";
        Status = $"indice de {Path.GetFileName(latest)} carregado";
    }

    [RelayCommand]
    private void FromNextActions()
    {
        var lines = _catalog.All.Where(p => p.HasNextAction).OrderByDescending(p => p.LastTouch).Take(12)
            .Select(p => $"{p.Name}: {FirstLine(p.NextAction)}");
        Text = "Proximas acoes dos projetos. " + string.Join(" ", lines.Select(l => l.TrimEnd('.') + "."));
        FileStem = "proximas-acoes";
        Status = "proximas acoes dos 12 projetos mais recentes";
    }

    // ---------- gerar ----------

    [RelayCommand]
    private async Task GenerateAsync()
    {
        if (IsBusy) return;
        if (SelectedVoice is null) { Status = "escolha uma voz"; return; }
        if (string.IsNullOrWhiteSpace(Text)) { Status = "texto vazio"; return; }
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        IsBusy = true;
        Status = $"gerando com {SelectedVoice.Name}… ({Math.Min(Text.Length, AudioStudio.MaxChars)} chars)";
        try
        {
            var file = await _audio.SpeakAsync(Text, SelectedVoice.Id, FileStem, cts.Token);
            LastFile = file;
            HasLastFile = true;
            Status = $"pronto: {Path.GetFileName(file)} — tocando";
            RefreshOutputs();
            _player.Play(file, FileStem);
        }
        catch (OperationCanceledException) { Status = "cancelado"; }
        catch (Exception ex) { Status = "erro: " + ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void Play() { if (HasLastFile) _player.Play(LastFile, FileStem); }

    [RelayCommand]
    private void OpenFolder()
    {
        Directory.CreateDirectory(OutputDir);
        _launcher.OpenExplorer(OutputDir);
    }

    private void RefreshOutputs()
    {
        Outputs.Clear();
        foreach (var f in _audio.ListOutputs())
        {
            var path = f;
            Outputs.Add(new Chip
            {
                Label = $"{File.GetLastWriteTime(f):MM-dd HH:mm}  {Path.GetFileName(f)}",
                Command = new RelayCommand(() => _player.Play(path)), Tooltip = path + "  (toca aqui no app)",
            });
        }
    }

    /// <summary>Markdown → fala: tira cerquilhas, crases, bullets e caminhos gritantes.</summary>
    private static string Humanize(string md)
    {
        var lines = md.Split('\n').Select(l => l.TrimEnd('\r'))
            .Where(l => !l.StartsWith("---") && !l.StartsWith('|') && !l.StartsWith("```"))
            .Select(l => l.TrimStart('#', ' ').Replace("**", "").Replace("`", "").Replace("- ", "").Replace("→", "vira"));
        return string.Join("\n", lines).Trim();
    }

    private static string FirstLine(string s)
    {
        var i = s.IndexOf('\n');
        return (i < 0 ? s : s[..i]).Trim();
    }
}
