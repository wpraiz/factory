using System.Collections.ObjectModel;
using System.Text;
using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Factory.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Factory.ViewModels;

/// <summary>Uma linha do papo: quem falou, o texto e a cor do rotulo.</summary>
public sealed partial class ChatLine : ObservableObject
{
    public required string Who { get; init; }      // VOCE · FACTORY · CLAUDE · ODIN
    [ObservableProperty] private string _text = "";
    public string When { get; init; } = DateTime.Now.ToString("HH:mm");
    public bool IsUser => Who == "VOCE";
    public bool IsClaude => Who is "CLAUDE" or "ODIN";
    /// <summary>VOCE = cyan, ODIN = ambar, CLAUDE = texto, FACTORY = dim.</summary>
    public Brush WhoBrush => (Brush)Application.Current.Resources[IsUser ? "NeonCyan" : Who == "ODIN" ? "NeonAmber" : IsClaude ? "CyberText" : "CyberDim"];
}

/// <summary>
/// Entrada do app: uma caixa. Texto que casa com um verbo vira comando na hora (sem IA, sem
/// custo); o resto vai pro Claude via `claude -p` (assinatura) e volta como linha do papo.
/// Enter executa a sugestao selecionada; se nao ha sugestao, pergunta ao Claude.
/// </summary>
public sealed partial class ChatViewModel : ObservableObject
{
    private readonly ICommandLine _commands;
    private readonly IChatService _chat;
    private readonly ProjectCatalog _catalog;
    private readonly ILauncher _launcher;
    private readonly IVoiceAgent _voice;
    private readonly IOdinHands _hands;
    private CancellationTokenSource? _cts;
    private readonly StringBuilder _transcript = new();

    public ObservableCollection<ChatLine> Lines { get; } = [];
    public ObservableCollection<CommandHit> Suggestions { get; } = [];

    [ObservableProperty] private string _input = "";
    [ObservableProperty] private int _selectedIndex = -1;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isBusy;
    public bool IsIdle => !IsBusy;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _hint = "";
    [ObservableProperty] private bool _hasSuggestions;

    // ---- voz (orb) ----
    /// <summary>idle · connecting · listening · speaking · error (vem do JS).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVoiceOn), nameof(VoiceButton))]
    private string _voiceMode = "idle";
    [ObservableProperty] private string _voiceStatus = "";
    public bool IsVoiceOn => VoiceMode is "connecting" or "listening" or "thinking" or "speaking";
    public string VoiceButton => IsVoiceOn ? "CALAR" : "VOZ";
    /// <summary>A pagina (dona do WebView2) atende: true = ligar, false = desligar.</summary>
    public event EventHandler<bool>? VoiceToggleRequested;
    /// <summary>WebView2 subiu e a pagina do orb respondeu "ready" (ou falhou de vez): libera o botao VOZ.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OrbLoading), nameof(VoiceEnabled))]
    private bool _orbReady;
    public bool OrbLoading => !OrbReady && VoiceMode != "error";
    public bool VoiceEnabled => OrbReady && _voice.Configured;
    private bool _summonPending;

    /// <summary>Atalho global: liga a voz agora ou assim que a orb subir.</summary>
    public void SummonVoice()
    {
        if (IsVoiceOn) return;
        if (VoiceEnabled) VoiceToggleRequested?.Invoke(this, true);
        else _summonPending = true;
    }

    partial void OnOrbReadyChanged(bool value)
    {
        if (value && _summonPending && VoiceEnabled) { _summonPending = false; VoiceToggleRequested?.Invoke(this, true); }
    }
    private DateTimeOffset _voiceSince;
    private string _voiceModeText = "";

    // ---- painel (sidebar direita) ----
    // Estado = o que o Odin declarou pela mao `estado` + o que as maos fizeram. Nada aqui e inventado:
    // sem declaracao, o painel diz que o velho ainda nao falou.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PanelGlyph), nameof(PanelBadge))]
    private bool _panelOpen = true;
    /// <summary>E76C (chevron direita) = fecha; E76B (chevron esquerda) = abre.</summary>
    public string PanelGlyph => PanelOpen ? "\uE76C" : "\uE76B";
    /// <summary>LED no botao do painel: ha recado ou urgente e o painel esta fechado (senao ele nao veria).</summary>
    public bool PanelBadge => !PanelOpen && (WantsToTalk || HasUrgent);
    // Persistencia do painel (aberto, volume, intensidade): app desempacotado nao tem ApplicationData; e um JSON.
    private static readonly string PanelFile = Path.Combine(AppPaths.Cache, "painel.json");
    private bool _loadingPanel;
    private void SavePanel()
    {
        if (_loadingPanel) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PanelFile)!);
            File.WriteAllText(PanelFile, JsonSerializer.Serialize(new { aberto = PanelOpen, volume = Volume, intensidade = Intensity, caos = Caos, humor = Humor, recado = Recado, declarado = StateDeclared, declaradoEm = DeclaredAt }));
        }
        catch (Exception) { /* sem disco: fica so na sessao */ }
    }
    private void LoadPanel()
    {
        try
        {
            if (!File.Exists(PanelFile)) return;
            _loadingPanel = true;
            using var doc = JsonDocument.Parse(File.ReadAllText(PanelFile));
            var r = doc.RootElement;
            if (r.TryGetProperty("aberto", out var a) && a.ValueKind is JsonValueKind.True or JsonValueKind.False) PanelOpen = a.GetBoolean();
            if (r.TryGetProperty("volume", out var v) && v.TryGetDouble(out var vd)) Volume = Math.Clamp(vd, 0, 100);
            if (r.TryGetProperty("intensidade", out var i) && i.TryGetDouble(out var id)) Intensity = Math.Clamp(id, 20, 200);
            // o estado que ele declarou por ultimo volta como estava, com a hora: "ver como ele ta" antes de chamar
            if (r.TryGetProperty("declarado", out var d) && d.ValueKind == JsonValueKind.True)
            {
                StateDeclared = true;
                if (r.TryGetProperty("caos", out var c) && c.TryGetInt32(out var ci)) Caos = Math.Clamp(ci, 0, 100);
                if (r.TryGetProperty("humor", out var h) && h.ValueKind == JsonValueKind.String) Humor = h.GetString() ?? "";
                if (r.TryGetProperty("recado", out var rc) && rc.ValueKind == JsonValueKind.String) Recado = rc.GetString() ?? "";
                if (r.TryGetProperty("declaradoEm", out var de) && de.ValueKind == JsonValueKind.String) DeclaredAt = de.GetString() ?? "";
            }
        }
        catch (Exception) { /* arquivo torto: padroes */ }
        finally { _loadingPanel = false; }
    }
    partial void OnPanelOpenChanged(bool value) => SavePanel();
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CaosText))]
    private int _caos;
    [ObservableProperty] private bool _stateDeclared;
    public string CaosText => !StateDeclared ? "—" : Caos >= 80 ? $"{Caos} · faminto" : Caos >= 50 ? $"{Caos} · inquieto" : Caos >= 25 ? $"{Caos} · sereno" : $"{Caos} · em repouso";
    [ObservableProperty] private string _humor = "o velho ainda nao falou. Chame (VOZ) e ele declara no primeiro turno.";
    /// <summary>When the user last declared it ("declarado dd/mm hh:mm"); empty if never.</summary>
    [ObservableProperty] private string _declaredAt = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WantsToTalk), nameof(RecadoText), nameof(PanelBadge))]
    private string _recado = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WantsToTalk), nameof(RecadoText), nameof(PanelBadge))]
    private int _pendingContexts;
    public bool WantsToTalk => Recado.Length > 0 || PendingContexts > 0;
    public string RecadoText => Recado.Length > 0 ? Recado : PendingContexts > 0 ? $"{PendingContexts} resultado(s) de tarefa esperando a proxima conversa" : "—";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUrgent), nameof(UrgentText), nameof(PanelBadge))]
    private int _urgent;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UrgentText))]
    private string _urgentLast = "";
    public bool HasUrgent => Urgent > 0;
    public string UrgentText => Urgent == 0 ? "—" : $"{Urgent} · {UrgentLast}";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TasksRunning), nameof(TasksText))]
    private int _tasksCount;
    public bool TasksRunning => TasksCount > 0;
    public string TasksText => TasksCount == 0 ? "—" : $"{TasksCount} em andamento · 3 mãos, 3 olhos, 1 mouse";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumeText))]
    private double _volume = 100;
    public string VolumeText => $"{(int)Volume}%";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IntensityText))]
    private double _intensity = 100;
    public string IntensityText => $"{(int)Intensity}%";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MicButton))]
    private bool _micMuted;
    public string MicButton => MicMuted ? "MIC MUDO" : "MIC ABERTO";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AgentLabel))]
    private string _agentLabelTick = "";   // so pra re-avaliar AgentLabel quando o cerebro muda
    public string AgentLabel => _voice.AgentLabel.Length > 0 ? _voice.AgentLabel : "agente nao configurada";

    // ---- cerebro (LLM) da agente em uso, trocavel no painel ----
    public IReadOnlyList<string> Brains => Services.Brains.All;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BrainInfo))]
    private string? _selectedBrain;
    public string BrainInfo => Services.Brains.Info(SelectedBrain);
    [ObservableProperty] private bool _brainBusy;
    private bool _loadingBrain;
    /// <summary>Le na API qual cerebro esta no ar; nao dispara PATCH.</summary>
    private async Task LoadBrainAsync()
    {
        try
        {
            var llm = await _voice.GetLlmAsync(CancellationToken.None);
            _loadingBrain = true; SelectedBrain = llm; _loadingBrain = false;
            AgentLabelTick = DateTime.Now.Ticks.ToString();
        }
        catch (Exception) { _loadingBrain = false; }
    }
    partial void OnSelectedBrainChanged(string? value)
    {
        if (_loadingBrain || string.IsNullOrWhiteSpace(value)) return;
        _ = ApplyBrainAsync(value.Trim());
    }
    /// <summary>Nome digitado no ComboBox editavel (fora da lista) tambem vale.</summary>
    public void SubmitBrain(string text) { if (!string.IsNullOrWhiteSpace(text)) SelectedBrain = text.Trim(); }
    private async Task ApplyBrainAsync(string llm)
    {
        BrainBusy = true;
        Status = $"cerebro: trocando pra {llm}…";
        var err = await _voice.SetLlmAsync(llm, CancellationToken.None);
        BrainBusy = false;
        if (err.Length > 0) { Status = "cerebro: " + err; await LoadBrainAsync(); return; }
        AgentLabelTick = DateTime.Now.Ticks.ToString();
        Status = IsVoiceOn ? $"cerebro: {llm} · vale na PROXIMA conversa (a atual segue no anterior)" : $"cerebro: {llm} · pronto pra proxima conversa";
        Lines.Add(new ChatLine { Who = "FACTORY", Text = $"cerebro do Odin → {llm} ({Services.Brains.Info(llm)})" });
        try
        {
            // o script tools\create-orb-agent.mjs le isto e mantem a escolha do painel ao reaplicar a agente
            var f = Path.Combine(Path.GetDirectoryName(PanelFile)!, "cerebro.json");
            var node = File.Exists(f) ? System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(f)) as System.Text.Json.Nodes.JsonObject : null;
            node ??= new System.Text.Json.Nodes.JsonObject();
            node[_voice.AgentId ?? "?"] = llm;
            File.WriteAllText(f, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        }
        catch (Exception) { /* sem disco: a agente ja mudou */ }
    }

    // ---- latencia e log local (%LOCALAPPDATA%\Factory\odin-log\<data>.jsonl; resumo: node tools\odin-log.mjs) ----
    private static readonly string LogDir = Path.Combine(AppPaths.Cache, "odin-log");
    private int _msVoz, _msTexto;
    [ObservableProperty] private string _latencyText = "—";
    private void Log(string kind, int ms, string? name = null)
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            var line = JsonSerializer.Serialize(new { t = DateTime.Now.ToString("HH:mm:ss"), kind, ms, name, agente = _voice.AgentLabel });
            File.AppendAllText(Path.Combine(LogDir, DateTime.Today.ToString("yyyy-MM-dd") + ".jsonl"), line + Environment.NewLine);
        }
        catch (Exception) { /* sem disco: a metrica fica so na tela */ }
    }
    /// <summary>O JS mediu: da transcricao do usuario ate o primeiro texto ('resposta-texto') ou a primeira voz ('resposta-voz') do Odin.</summary>
    public void OnMetric(string kind, int ms)
    {
        if (kind == "movimento") { _lastMovement = DateTime.Now; if (WakeMode) Status = $"camera: movimento ({ms}) · {WakeText}"; return; }
        // voz no microfone antes do turno fechar. Monologo longo (ou musica/gente em volta) nao vira frase final
        // por minutos e o corte de 4 min derrubava o usuario falando (6 times in one night). Conta como fala real.
        if (kind == "fala-bruta") { if (!WakeMode) _lastUserSpeech = DateTime.Now; return; }
        if (kind == "resposta-voz") _msVoz = ms; else if (kind == "resposta-texto") _msTexto = ms; else return;
        Log(kind, ms);
        LatencyText = $"{(_msVoz > 0 ? (_msVoz / 1000.0).ToString("0.0") + " s voz" : "")}{(_msVoz > 0 && _msTexto > 0 ? " · " : "")}{(_msTexto > 0 ? (_msTexto / 1000.0).ToString("0.0") + " s texto" : "")}";
    }
    /// <summary>A pagina (dona do WebView2) executa: window.factory.{Name}({Arg}).</summary>
    public event EventHandler<(string Name, string Arg)>? OrbCommand;
    partial void OnVolumeChanged(double value) { OrbCommand?.Invoke(this, ("volume", (value / 100).ToString("0.00", CultureInfo.InvariantCulture))); SavePanel(); }
    partial void OnIntensityChanged(double value) { OrbCommand?.Invoke(this, ("intensidade", (value / 100).ToString("0.00", CultureInfo.InvariantCulture))); SavePanel(); }
    [RelayCommand] private void TogglePanel() => PanelOpen = !PanelOpen;
    [RelayCommand] private void ToggleMic() { MicMuted = !MicMuted; OrbCommand?.Invoke(this, ("mic", MicMuted ? "true" : "false")); }
    /// <summary>A pagina devolve o que o JS respondeu ao comando `mic` ("ok" | "sem-sessao" | "sem-metodo").</summary>
    public void OnMicResult(string r)
    {
        if (r == "sem-metodo") { MicMuted = false; Status = "mic: o SDK da ElevenLabs nesta versao nao tem setMicMuted; o botao nao muda nada. Registrei."; }
        else if (r == "sem-sessao") Status = MicMuted ? "mic mudo: vale quando a conversa abrir" : "";
    }
    [RelayCommand]
    private void ClearUrgent()
    {
        // fica no papo (e no historico do dia) o que foi marcado como visto; o painel zera
        if (Urgent > 0) Lines.Add(new ChatLine { Who = "FACTORY", Text = $"urgente visto: {UrgentText}" });
        Urgent = 0; UrgentLast = "";
    }

    private readonly DispatcherQueue? _dq;
    private void OnUi(Action a) { if (_dq is null || _dq.HasThreadAccess) a(); else _dq.TryEnqueue(() => a()); }

    // ---- ACORDAR: alarme humano. O Factory aberto e a unica exigencia. ----
    private readonly DispatcherTimer _alarmClock = new();
    private DateTime? _alarmAt; private string _alarmMotivo = "", _alarmDor = "", _alarmSono = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAlarm))]
    private string _alarmText = "—";
    public bool HasAlarm => _alarmAt is not null;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WakeText))]
    private bool _wakeMode;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WakeText))]
    private int _wakeLevel;
    public string WakeText => WakeMode ? $"ACORDANDO · nivel {WakeLevel} · {(int)(DateTime.Now - _wakeSince).TotalMinutes} min" : "";
    private DateTime _wakeSince, _lastUserSpeech, _lastEscalation, _lastMovement;
    private string? _wakeGreeting;

    private void LoadAlarm()
    {
        _alarmAt = null;
        try
        {
            if (File.Exists(OdinHands.AlarmFile))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(OdinHands.AlarmFile));
                var r = doc.RootElement;
                if (r.TryGetProperty("ativo", out var a) && a.ValueKind == JsonValueKind.True && r.TryGetProperty("quando", out var q) && DateTime.TryParse(q.GetString(), out var when))
                {
                    _alarmAt = when;
                    _alarmMotivo = r.TryGetProperty("motivo", out var m) ? m.GetString() ?? "" : "";
                    _alarmDor = r.TryGetProperty("dor", out var d) ? d.GetString() ?? "" : "";
                    _alarmSono = r.TryGetProperty("sono", out var so) ? so.GetString() ?? "" : "";
                }
            }
        }
        catch (Exception) { _alarmAt = null; }
        AlarmText = _alarmAt is null ? "—" : $"{_alarmAt:ddd dd/MM HH:mm}{(_alarmMotivo.Length > 0 ? " · " + _alarmMotivo : "")}";
    }

    private void CheckAlarm()
    {
        if (WakeMode) { OnPropertyChanged(nameof(WakeText)); Escalate(); return; }
        if (_alarmAt is null || DateTime.Now < _alarmAt) return;
        if (DateTime.Now > _alarmAt.Value.AddHours(3)) { _alarmAt = null; AlarmText = "— (alarme vencido sem o app aberto)"; return; }
        StartWake();
    }

    private void StartWake()
    {
        WakeMode = true; WakeLevel = 1; _wakeSince = DateTime.Now; _lastUserSpeech = DateTime.MinValue; _lastEscalation = DateTime.Now;
        PanelOpen = true; MicMuted = false; OrbCommand?.Invoke(this, ("mic", "false"));
        Volume = 100; OrbCommand?.Invoke(this, ("volume", "1"));
        for (var i = 0; i < 15; i++) WindowsHands.MediaKey(0xAF);   // volume do Windows pra cima (tira do mudo tambem)
        var ctx = $"{(_alarmDor.Length > 0 ? "dor nas costas " + _alarmDor + "/10; " : "")}{(_alarmSono.Length > 0 ? "dormiu ~" + _alarmSono + " h; " : "")}{_alarmMotivo}";
        _wakeGreeting = $"{(AppPaths.NomeUsuario.Length > 0 ? AppPaths.NomeUsuario.ToUpperInvariant() + ". " : "")}ACORDA. Aqui é Odin. Tu me pediste pra te acordar{(ctx.Length > 0 ? ": " + ctx : "")}. Levanta devagar por causa da coluna. Responde: que dia é hoje?";
        Status = "ALARME: chamando o Odin pra te acordar";
        Log("acordar", 0, "inicio");
        Lines.Add(new ChatLine { Who = "FACTORY", Text = "⏰ ALARME disparado: Odin vai te acordar. Diga 'tô acordado' e responda a pergunta do Odin." });
        OrbCommand?.Invoke(this, ("camera", "true"));
        App.Window?.SummonOdin();
        if (IsVoiceOn) OrbCommand?.Invoke(this, ("contexto", JsonSerializer.Serialize("ALARME AGORA: " + _wakeGreeting)));
        PlayAlarmSound();
    }

    /// <summary>A cada 20 s em modo acordar: sem fala do usuario ha 40 s → som + volume + cutucada no Odin, subindo o nivel.</summary>
    private void Escalate()
    {
        var quietFor = DateTime.Now - (_lastUserSpeech == DateTime.MinValue ? _wakeSince : _lastUserSpeech);
        if (quietFor < TimeSpan.FromSeconds(40) || DateTime.Now - _lastEscalation < TimeSpan.FromSeconds(40)) return;
        _lastEscalation = DateTime.Now; WakeLevel++;
        for (var i = 0; i < 4; i++) WindowsHands.MediaKey(0xAF);
        PlayAlarmSound();
        var mov = _lastMovement > _wakeSince ? $"camera: ultimo movimento ha {(int)(DateTime.Now - _lastMovement).TotalSeconds} s" : "camera: nenhum movimento desde o alarme";
        if (!IsVoiceOn) App.Window?.SummonOdin();
        else OrbCommand?.Invoke(this, ("contexto", JsonSerializer.Serialize($"ALARME nivel {WakeLevel}: ele nao respondeu ha {(int)quietFor.TotalSeconds} s ({mov}). Chama pelo nome, MAIS ALTO, frase curta e diferente da anterior, e faz uma pergunta simples. Nao desligue.")));
        Log("acordar", WakeLevel, "escalada");
        if (WakeLevel >= 45) StopWake("30 min sem resposta; parei o alarme");   // ~30 min
    }

    [System.Runtime.InteropServices.DllImport("winmm.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern bool PlaySound(string name, IntPtr hmod, uint flags);

    private static void PlayAlarmSound()
    {
        try
        {
            var f = new[] { @"C:\Windows\Media\Alarm05.wav", @"C:\Windows\Media\Alarm01.wav", @"C:\Windows\Media\Ring05.wav" }.FirstOrDefault(File.Exists);
            if (f is not null) PlaySound(f, IntPtr.Zero, 0x20001);   // SND_FILENAME | SND_ASYNC (winmm, sem referencia extra)
        }
        catch (Exception) { /* sem som: a voz e o volume continuam */ }
    }

    private void StopWake(string why)
    {
        if (!WakeMode) return;
        WakeMode = false; _wakeGreeting = null;
        OrbCommand?.Invoke(this, ("camera", "false"));
        Status = $"alarme encerrado {DateTime.Now:HH:mm}: {why} ({(int)(DateTime.Now - _wakeSince).TotalMinutes} min, nivel {WakeLevel})";
        Lines.Add(new ChatLine { Who = "FACTORY", Text = "⏰ " + Status });
        Log("acordar", (int)(DateTime.Now - _wakeSince).TotalSeconds, "fim: " + why);
        LoadAlarm();
    }

    public string Help => _commands.Help;
    public bool ChatAvailable => _chat.Available;

    public ChatViewModel(ICommandLine commands, IChatService chat, ProjectCatalog catalog, ILauncher launcher, IVoiceAgent voice, IOdinHands hands)
    {
        _commands = commands;
        _chat = chat;
        _catalog = catalog;
        _launcher = launcher;
        _voice = voice;
        _hands = hands;
        VoiceStatus = voice.Configured ? "Odin carregando…" : "Odin sem voz: " + voice.Missing;
        // As maos podem chegar por outra thread (HandsServer/link de casa): tudo que toca a UI passa pelo dispatcher.
        _dq = DispatcherQueue.GetForCurrentThread();
        _hands.StateDeclared += (_, st) => OnUi(() =>
        {
            Caos = st.Caos; Humor = st.Humor; Recado = st.Recado; StateDeclared = true; _declaredThisSession = true;
            DeclaredAt = "declarado " + DateTime.Now.ToString("dd/MM HH:mm");
            SavePanel();
        });
        _hands.AlarmChanged += (_, _) => OnUi(LoadAlarm);
        _hands.WokeUp += (_, _) => OnUi(() => StopWake("ele provou que acordou"));
        LoadAlarm();
        _alarmClock.Interval = TimeSpan.FromSeconds(20);
        _alarmClock.Tick += (_, _) => CheckAlarm();
        _alarmClock.Start();
        LoadPanel();
        _ = LoadBrainAsync();

        Lines.Add(new ChatLine
        {
            Who = "FACTORY",
            Text = _chat.Available
                ? (_chat.HasSession ? "papo de hoje continua de onde parou. " : "")
                  + "clique no orb pra falar com Odin. Na caixa: `ir <projeto>` retoma o Claude na pasta; o resto vai pro Claude de texto."
                : "claude CLI nao encontrado (nem em ~\\.local\\bin nem no PATH): os comandos funcionam, o papo de texto nao.",
        });
        UpdateHint();
    }

    partial void OnInputChanged(string value)
    {
        Suggestions.Clear();
        foreach (var h in _commands.Suggest(value)) Suggestions.Add(h);
        HasSuggestions = Suggestions.Count > 0;
        SelectedIndex = HasSuggestions ? 0 : -1;
        UpdateHint();
    }

    private void UpdateHint()
    {
        var text = Input.Trim();
        Hint = text.Length == 0 ? "Enter envia · Shift+Enter quebra linha · ↑↓ escolhe sugestao · Esc limpa/cancela"
             : HasSuggestions ? $"Enter executa a sugestao marcada · {Suggestions.Count} opcoes"
             : _chat.Available ? "sem comando pra isso: Enter pergunta ao Claude (~8 s)"
             : "sem comando pra isso, e o claude CLI nao esta disponivel";
    }

    public void Move(int delta)
    {
        if (Suggestions.Count == 0) return;
        SelectedIndex = Math.Clamp(SelectedIndex + delta, 0, Suggestions.Count - 1);
    }

    /// <summary>Esc: com texto, limpa; sem texto e ocupado, cancela a pergunta.</summary>
    public void Escape()
    {
        if (Input.Length > 0) { Input = ""; Status = ""; return; }
        if (IsBusy) { Cancel(); return; }
        if (WakeMode) { StopWake("Esc na tela"); return; }
        if (IsVoiceOn) { VoiceToggleRequested?.Invoke(this, false); return; }   // Esc tambem cala a orb
        if (PanelOpen) PanelOpen = false;   // por ultimo, Esc fecha o painel (e uma sidebar)
    }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        var text = Input.Trim();
        if (text.Length == 0) return;

        // comando roda mesmo com o claude pensando: e a acao de 1 tecla da tela
        if (SelectedIndex >= 0 && SelectedIndex < Suggestions.Count)
        {
            Run(Suggestions[SelectedIndex]);
            return;
        }
        if (IsBusy) { Status = "o claude ainda esta respondendo: espere ou Esc cancela."; return; }
        await AskAsync(text);
    }

    public void Run(CommandHit hit)
    {
        Lines.Add(new ChatLine { Who = "VOCE", Text = Input.Trim() });
        Input = "";
        try
        {
            hit.Run();
            Lines.Add(new ChatLine { Who = "FACTORY", Text = $"{hit.Echo}  ·  {hit.Detail}" });
            Status = "";
        }
        catch (Exception ex)
        {
            Lines.Add(new ChatLine { Who = "FACTORY", Text = $"nao consegui: {ex.Message}" });
        }
    }

    private async Task AskAsync(string text)
    {
        if (!_chat.Available)
        {
            Status = "claude CLI nao encontrado: instale o Claude Code ou deixe `claude` no PATH e reabra.";
            return;
        }
        Lines.Add(new ChatLine { Who = "VOCE", Text = text });
        Input = "";
        IsBusy = true;
        Status = _chat.HasSession ? "claude pensando…" : "primeira mensagem: mandando o catalogo junto (mais lenta)…";
        _cts = new CancellationTokenSource();
        try
        {
            var reply = await _chat.AskAsync(text, _catalog.All.ToList(), _cts.Token);
            Lines.Add(new ChatLine { Who = "CLAUDE", Text = reply.Text });
            Status = $"{reply.DurationMs / 1000.0:0.0} s · salvo em {Path.Combine(_chat.HistoryDir, DateTime.Today.ToString("yyyy-MM-dd") + ".md")}";
        }
        catch (OperationCanceledException)
        {
            Lines.Add(new ChatLine { Who = "FACTORY", Text = "cancelado." });
            Status = "";
        }
        catch (Exception ex)
        {
            Lines.Add(new ChatLine { Who = "FACTORY", Text = $"o claude nao respondeu: {ex.Message}" });
            Status = "";
        }
        finally
        {
            IsBusy = false;
            _cts = null;
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    // ---------- voz ----------

    [RelayCommand]
    private void ToggleVoice()
    {
        if (!_voice.Configured) { VoiceStatus = "voz desligada: " + _voice.Missing; return; }
        VoiceToggleRequested?.Invoke(this, !IsVoiceOn);
    }

    /// <summary>Chamado pela pagina quando o JS pede sessao: URL assinada + catalogo. Gasta minutos so daqui em diante.</summary>
    public async Task<(string SignedUrl, string Catalogo, string Memoria, string Hoje, string Ultima, string? Saudacao, string Familia)?> StartVoiceAsync()
    {
        try
        {
            VoiceMode = "connecting";
            VoiceStatus = "chamando Odin…";
            _sessaoInicio = DateTime.Now;   // relogio do corte por silencio (TickVoice)
            // Conversa anterior caiu sem resumo (app fechado no meio)? Resume agora, em paralelo.
            _ = _voice.SummarizePendingAsync(CancellationToken.None);
            var ultima = _voice.LoadLastTranscript();
            var saudacao = _wakeGreeting ?? _voice.ResumeGreeting();
            var url = await _voice.GetSignedUrlAsync(CancellationToken.None);
            _transcript.Clear();
            _voice.BeginLive();
            return (url, _voice.BuildCatalog(_catalog.All.ToList()), _voice.LoadMemory(),
                DateTime.Now.ToString("dddd, dd/MM/yyyy HH:mm", new System.Globalization.CultureInfo("pt-BR")), ultima, saudacao, _voice.LoadFamily());
        }
        catch (Exception ex)
        {
            VoiceMode = "error";
            VoiceStatus = "voz: " + ex.Message;
            return null;
        }
    }

    public void OnVoiceStatus(string mode, string text)
    {
        var wasOn = IsVoiceOn;
        VoiceMode = mode;
        if (mode == "listening" && !wasOn) { _voiceSince = DateTimeOffset.Now; PendingContexts = 0; _declaredThisSession = false; _nudged = false; }   // conectou: comeca a contar; a fila de contexto entrou
        _voiceModeText = mode switch
        {
            "off" => "Odin sem voz: " + _voice.Missing,
            "idle" => wasOn ? $"Odin se recolheu · {Elapsed()} de conversa · clique pra chamar de novo" : "Odin em repouso · clique no orb ou em VOZ · cobra por minuto de conversa",
            "connecting" => "chamando Odin…",
            "listening" => "Odin ouve",
            "thinking" => "Odin pensa",
            "speaking" => "Odin fala",
            _ => "voz: " + text,
        };
        TickVoice();
        if (wasOn && mode == "idle" && _transcript.Length > 0)
        {
            // Fim de conversa: resumo vai pra memoria (claude -p, assinatura). Sem bloquear a UI.
            var t = _transcript.ToString();
            _transcript.Clear();
            _ = Task.Run(async () => { await _voice.SummarizeAsync(t, CancellationToken.None); _voice.EndLive(); });
        }
    }

    /// <summary>Ferramenta pedida pela agente: executa nas maos do Odin e registra no papo.</summary>
    public async Task<string> RunHandAsync(string name, JsonElement args, string? conv = null, string? episode = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await _hands.RunAsync(name, args, CancellationToken.None, "voz", conv, episode);
        Log("mao", (int)sw.ElapsedMilliseconds, name);
        var first = result.Split('\n')[0];
        var echo = $"⚙ {name}: {(first.Length > 140 ? first[..140] + "…" : first)}";
        Lines.Add(new ChatLine { Who = "FACTORY", Text = echo });
        Status = echo;   // a fala no ceu desbota; a linha de status fica
        if (name is "fazer" or "pesquisar" or "ver_tela" && first.StartsWith("Comecei", StringComparison.OrdinalIgnoreCase)) TasksCount++;
        if (name == "reportar_falha") { Urgent++; UrgentLast = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("texto", out var tx) ? (tx.GetString() ?? "") : "falha reportada"; }
        _transcript.AppendLine($"[ferramenta {name}] {first}");
        _voice.AppendLive($"[ferramenta {name}] {first}");
        return result;
    }

    /// <summary>A pagina chama a cada segundo enquanto a voz esta ligada: e o "quanto" do gasto.</summary>
    private bool _declaredThisSession, _nudged;
    private DateTime _sessaoInicio;
    public void TickVoice()
    {
        if (IsVoiceOn && !_declaredThisSession && !_nudged && VoiceMode != "connecting" && (DateTimeOffset.Now - _voiceSince) > TimeSpan.FromSeconds(20))
        {
            _nudged = true;
            OrbCommand?.Invoke(this, ("contexto", JsonSerializer.Serialize("o painel ainda esta sem teu estado: chame a mao `estado` agora (caos, humor, recado), sem anunciar.")));
        }
        // Ghost conversation: voz aberta sem ninguem, o microfone pega ruido ("..."), o Odin responde "." e
        // cada volta cobra LLM + minuto. 68 voltas em 18 min. Sem fala REAL por 4 min (8 com tarefa rodando), encerra.
        // Alarme de acordar fica de fora: la o silencio e exatamente o que ele combate.
        // usava _voiceSince, que nao zera na passagem connecting->listening; ficava em 0001-01-01 e a voz
        // morria no 1o segundo (6 attempts in a row). Agora conta de _sessaoInicio, marcado em StartVoiceAsync; sem marca, nao corta.
        if (IsVoiceOn && !WakeMode && VoiceMode != "connecting" && _sessaoInicio != default)
        {
            var desde = _lastUserSpeech > _sessaoInicio ? _lastUserSpeech : _sessaoInicio;
            var limite = TimeSpan.FromMinutes(TasksCount > 0 ? 8 : 4);
            if (DateTime.Now - desde > limite)
            {
                _sessaoInicio = default;
                Log("voz-silencio", (int)Math.Min(int.MaxValue, (DateTime.Now - desde).TotalMilliseconds), $"encerrada apos {(int)limite.TotalMinutes} min sem fala real");
                Lines.Add(new ChatLine { Who = "FACTORY", Text = $"🔇 Encerrei a voz: {(int)limite.TotalMinutes} min sem fala tua (só ruído). Ctrl+Alt+O chama de novo." });
                VoiceToggleRequested?.Invoke(this, false);
                return;
            }
        }
        VoiceStatus = IsVoiceOn && VoiceMode != "connecting"
            ? $"{_voiceModeText} · {Elapsed()} de agente · CALAR ou Esc encerra"
            : _voiceModeText;
    }

    private string Elapsed()
    {
        var t = DateTimeOffset.Now - _voiceSince;
        return t < TimeSpan.Zero ? "00:00" : $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";
    }

    /// <summary>Uma tarefa `fazer` terminou (ja na thread de UI). Vira linha no ceu e vai pro Odin como contexto.</summary>
    public string OnTaskFinished(string task, string result, bool ok)
    {
        var first = result.Split('\n')[0];
        var echo = $"{(ok ? "✔" : "✖")} {task}: {(first.Length > 160 ? first[..160] + "…" : first)}";
        Lines.Add(new ChatLine { Who = "FACTORY", Text = echo });
        Status = echo;
        if (TasksCount > 0) TasksCount--;
        Log(ok ? "tarefa-ok" : "tarefa-falhou", 0, task.Length > 80 ? task[..80] : task);
        if (!ok) { Urgent++; UrgentLast = first; }
        if (!IsVoiceOn) PendingContexts++;   // o JS guarda o contexto e entrega na abertura da proxima conversa
        _transcript.AppendLine($"[tarefa concluida] {task}: {first}");
        return $"tarefa \"{task}\" terminou. Resultado: {result}";
    }

    /// <summary>Tarefa `fazer` ainda rodando/na fila (ja na thread de UI). Linha no ceu sempre; contexto pro Odin so com
    /// a voz ligada (sem voz nao guarda: progresso velho nao interessa na proxima conversa).</summary>
    public string? OnTaskProgress(string task, string msg)
    {
        var curta = task.Length > 60 ? task[..60] + "…" : task;
        Lines.Add(new ChatLine { Who = "FACTORY", Text = $"⏳ {curta}: {msg}" });
        Status = $"⏳ {curta}";
        if (!IsVoiceOn) return null;
        return $"tarefa \"{curta}\" {msg}. Diga isso a ele numa frase curta, na tua voz, sem inventar resultado. Se ele pedir outra coisa, atenda: a tarefa segue sozinha.";
    }

    /// <summary>Fala transcrita entra no papo. Versao nova do mesmo turno do Odin substitui a linha (devolve true).</summary>
    public bool OnVoiceMessage(string who, string text, bool update)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var isUser = who == "pessoa";
        if (isUser && !System.Text.RegularExpressions.Regex.IsMatch(text, @"^[\s.…]*$")) _lastUserSpeech = DateTime.Now;
        _transcript.AppendLine((isUser ? "VOCE: " : "ODIN: ") + text);
        if (!update) _voice.AppendLive((isUser ? "VOCE: " : "ODIN: ") + text);
        if (update && !isUser && Lines.Count > 0 && Lines[^1].Who == "ODIN") { Lines[^1].Text = text; return true; }
        Lines.Add(new ChatLine { Who = isUser ? "VOCE" : "ODIN", Text = text });
        return false;
    }

    [RelayCommand]
    private void NewChat()
    {
        _chat.Reset();
        Lines.Add(new ChatLine { Who = "FACTORY", Text = "papo novo: a proxima mensagem vai com o catalogo atualizado." });
        Status = "";
    }

    [RelayCommand]
    private void OpenHistory()
    {
        Directory.CreateDirectory(_chat.HistoryDir);
        _launcher.OpenExplorer(_chat.HistoryDir);
    }
}
