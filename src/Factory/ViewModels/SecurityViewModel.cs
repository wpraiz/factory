using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Factory.Models;
using Factory.Services;

namespace Factory.ViewModels;

/// <summary>
/// SEGURANCA — "meu PC esta bem guardado?". Postura (Defender, BitLocker, HVCI, ASR, firewall), feed do Defender 24 h,
/// portas abertas pra rede e o que inicia sozinho, cada um contra a allowlist do usuario. HardeningKitty por clique (UAC).
/// So le; o unico que escreve e CONHEÇO (allowlist) e AUDITAR (relatorio), os dois em .factory\factory.
/// </summary>
/// <summary>Grupo de achados com cabecalho (ListView agrupado nativo: CollectionViewSource + GroupStyle).</summary>
public sealed class SecGrupo : List<SecRow>
{
    public string Titulo { get; init; } = "";
    public string Cabecalho => $"{Titulo} · {Count}";
}

public sealed partial class SecurityViewModel : ObservableObject
{
    private readonly IHostSecurityService _svc;
    public LiveTelemetry Live { get; }
    private string _cautelaVista = "";
    private string _cautelaAgora = "";

    public ObservableCollection<SecBadge> Badges { get; } = [];
    public ObservableCollection<SecEvent> Eventos { get; } = [];
    public ObservableCollection<SecRow> Portas { get; } = [];
    public ObservableCollection<SecRow> Persistencia { get; } = [];
    public ObservableCollection<SecHardeningRow> Altos { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Ocupado))]
    private bool _loading;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Ocupado))]
    private bool _auditando;
    /// <summary>Um unico indicador de trabalho no cabecalho (antes eram 2 aneis soltos).</summary>
    public bool Ocupado => Loading || Auditando;
    [ObservableProperty] private string _atencaoVazio = "lendo o Windows…";
    /// <summary>Motivo da ultima leitura falha (vazio = leu). A lista nao fica muda quando o coletor falha.</summary>
    [ObservableProperty] private string _falha = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AtencaoTemMais))]
    private int _atencaoMais;
    public bool AtencaoTemMais => AtencaoMais > 0;
    [ObservableProperty] private bool _loaded;
    [ObservableProperty] private string _headline = "lendo o Windows… (~7 s)";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _portasResumo = "";
    [ObservableProperty] private string _persistResumo = "";
    [ObservableProperty] private string _eventosResumo = "";
    [ObservableProperty] private bool _semEventos;
    [ObservableProperty] private bool _mostrarWindows;
    [ObservableProperty] private string _score = "—";
    [ObservableProperty] private string _scoreLegenda = "";
    [ObservableProperty] private double _scorePct;
    [ObservableProperty] private string _scoreAccent = "violet";
    [ObservableProperty] private bool _temRevisarPortas;
    [ObservableProperty] private bool _temRevisarPersist;
    [ObservableProperty] private bool _podeDesfazer;
    [ObservableProperty] private string _altosResumo = "";

    public string AllowlistPath => _svc.AllowlistPath;

    public string WindowsLabel => MostrarWindows ? "ESCONDER WINDOWS" : "MOSTRAR WINDOWS";

    // ---------- telemetria AO VIVO (3 osciloscopios) ----------
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VivoLabel))]
    private bool _aoVivo = true;
    /// <summary>Os 3 graficos dizem se estao vivos; a postura/listas dizem a hora da leitura no status.</summary>
    public string VivoLabel => AoVivo ? "AO VIVO · 1 AMOSTRA/S · 90 S" : "PAUSADO — alavanca AO VIVO";
    [ObservableProperty] private string _redeIn = "—";
    [ObservableProperty] private string _redeOut = "—";
    [ObservableProperty] private string _conexoes = "—";
    [ObservableProperty] private string _conexoesSub = "";
    [ObservableProperty] private string _nascidos = "—";
    [ObservableProperty] private string _nascidosSub = "";
    [ObservableProperty] private string _nascidosLista = "";
    /// <summary>MASTER CAUTION aceso: tem NOVO, protecao desligada ou evento do Defender que ele ainda nao reconheceu.</summary>
    [ObservableProperty] private bool _cautela;

    // ---------- abas (horizontal tabs inside Security) ----------
    public static readonly string[] AbaNomes = ["VISÃO GERAL", "REDE", "INICIA SOZINHO", "DEFENDER", "HARDENING", "SQUAD"];
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Aba0), nameof(Aba1), nameof(Aba2), nameof(Aba3), nameof(Aba4), nameof(Aba5))]
    private int _aba = 0;   // opens on the charts dashboard; O que fazer e a 2a aba
    public bool Aba0 => Aba == 0;
    public bool Aba1 => Aba == 1;
    public bool Aba2 => Aba == 2;
    public bool Aba3 => Aba == 3;
    public bool Aba4 => Aba == 4;
    public bool Aba5 => Aba == 5;
    /// <summary>Rotulo da aba com a contagem do que pede olho: "REDE · 3".</summary>
    [ObservableProperty] private string _tabAchados = "O que fazer";
    [ObservableProperty] private string _tabAlertas = "Alertas";
    [ObservableProperty] private string _tabDefender = "DEFENDER";
    [ObservableProperty] private string _tabHardening = "Hardening";

    /// <summary>VISAO GERAL: o que mais pede atencao agora (risco, NOVO), no maximo 6.</summary>
    public ObservableCollection<SecRow> Atencao { get; } = [];
    [ObservableProperty] private bool _semAtencao;
    public ObservableCollection<SecHardeningRow> AsrRegras { get; } = [];

    // ---------- SQUAD ----------
    private readonly ISecuritySquad _squad;
    [ObservableProperty] private string _squadTexto = "";
    [ObservableProperty] private string _squadStatus = "";
    [ObservableProperty] private bool _squadRodando;
    public string SquadPasta => _squad.Pasta;

    // ---------- ACHADOS (tudo concatenado) + ALERTAS (vigia continuo) ----------
    private readonly ISecurityWatch _watch;
    public ObservableCollection<SecRow> Achados { get; } = [];
    public ObservableCollection<SecAlert> Alertas { get; } = [];
    public static readonly string[] Fontes = ["TUDO", "REDE", "INICIA", "DEFENDER", "POSTURA"];
    [ObservableProperty] private string _filtro = "TUDO";
    [ObservableProperty] private string _fTudo = "", _fRede = "", _fInicia = "", _fDefender = "", _fPostura = "";
    [ObservableProperty] private string _achadosResumo = "";
    [ObservableProperty] private bool _semAchados;
    [ObservableProperty] private bool _semAlertas = true;
    [ObservableProperty] private string _vigiaEstado = "";
    [ObservableProperty] private string _vigiaBase = "";
    [ObservableProperty] private bool _vigiaLigada = true;
    private List<SecRow> _achadosTodos = [];
    public List<SecGrupo> AchadosGrupos { get; private set; } = [];
    public List<SecGrupo> AtencaoGrupos { get; private set; } = [];
    public event EventHandler? GruposMudou;

    private static readonly (string Fonte, string Titulo)[] Ordem =
        [("POSTURA", "Proteções desligadas"), ("DEFENDER", "Defender · últimas 24 h"), ("REDE", "Rede · programas escutando"), ("INICIA", "Inicia sozinho")];

    private static List<SecGrupo> Agrupar(IEnumerable<SecRow> rows)
    {
        var l = rows.ToList();
        var r = new List<SecGrupo>();
        foreach (var (fonte, titulo) in Ordem)
        {
            var g = new SecGrupo { Titulo = titulo };
            g.AddRange(l.Where(x => x.Fonte == fonte).OrderBy(x => x.Peso).ThenBy(x => x.Titulo, StringComparer.OrdinalIgnoreCase));
            if (g.Count > 0) r.Add(g);
        }
        return r;
    }

    public SecurityViewModel(IHostSecurityService svc, LiveTelemetry live, ISecuritySquad squad, ISecurityWatch watch)
    {
        _svc = svc;
        _squad = squad;
        _watch = watch;
        _vigiaLigada = watch.Ligado || watch.Estado.StartsWith("aprendendo");
        watch.Mudou += (_, _) => App.Window?.DispatcherQueue.TryEnqueue(AtualizarAlertas);
        AtualizarAlertas();
        // calling the squad again must not erase the previous one: historico unico, mais novo em cima, sobrevive a reabrir o app
        SquadTexto = File.Exists(HistoricoSquad) ? File.ReadAllText(HistoricoSquad) : squad.Ultimo ?? "";
        SquadStatus = squad.UltimoQuando is { } q ? $"último parecer: {q:dd/MM HH:mm} (salvo em {squad.Pasta})" : "nunca convocado — CONVOCAR roda uma consulta ao Claude (assinatura) com o retrato de agora";
        Live = live;
        Live.Tick += (_, _) => AtualizarLeituras();
    }

    public static string Kb(double kb) => kb >= 1024 ? $"{kb / 1024:0.0} MB/s" : $"{kb:0} KB/s";

    private void AtualizarLeituras()
    {
        RedeIn = Live.RedeIn.Count > 0 ? Kb(Live.RedeIn[^1]) : "—";
        RedeOut = Live.RedeOut.Count > 0 ? Kb(Live.RedeOut[^1]) : "—";
        Conexoes = Live.Conexoes.Count > 0 ? Live.Conexoes[^1].ToString("0") : "—";
        ConexoesSub = $"TCP estabelecidas · {Live.RemotosUnicos} IPs do outro lado";
        var ultimo60 = Live.Nascimentos.TakeLast(60).Sum();
        Nascidos = ultimo60.ToString("0");
        NascidosSub = $"nasceram no último minuto · {Live.Processos} rodando";
        NascidosLista = Live.Recentes.Count == 0 ? "nenhum processo novo ainda"
            : string.Join("  ", Live.Recentes.Take(4).Select(n => $"{n.Quando:HH:mm:ss} {n.Nome}"));
    }

    /// <summary>DESLIGAR/BLOQUEAR/LIMPAR (a confirmacao e feita na pagina, com o texto do que vai acontecer).</summary>
    public async Task CortarAsync(SecRow row)
    {
        Status = $"{row.Corte.ToLowerInvariant()}… ({row.Titulo})";
        Status = await _svc.CortarAsync(row);
        await LoadAsync(true);
    }

    [RelayCommand]
    private async Task ReverterAsync(SecRow? row)
    {
        if (row is null) return;
        Status = $"{row.Reverte.ToLowerInvariant()}… ({row.Titulo})";
        Status = await _svc.ReverterAsync(row);
        await LoadAsync(true);
    }

    /// <summary>Clique no selo da postura: abre a tela do Windows onde aquilo se liga (so abre; nada muda sozinho).</summary>
    [RelayCommand]
    private void AbrirSelo(SecBadge? b)
    {
        if (b is null || b.Abrir.Length == 0) { Status = "este selo só mostra o estado — o Windows não deixa mudar por aqui"; return; }
        if (b.Abrir.StartsWith("tab:")) { AbrirUrl(b.Abrir); return; }
        try
        {
            var i = b.Abrir.IndexOf(' ');
            var psi = i > 0 ? new ProcessStartInfo(b.Abrir[..i], b.Abrir[(i + 1)..]) { UseShellExecute = true } : new ProcessStartInfo(b.Abrir) { UseShellExecute = true };
            Process.Start(psi);
            Status = $"abri a tela do Windows pra {b.Rotulo.ToLowerInvariant()}";
        }
        catch (Exception ex) { Status = "o Windows não abriu: " + ex.Message; }
    }

    [RelayCommand]
    private void Reconhecer()
    {
        _cautelaVista = _cautelaAgora;
        _watch.VistoTodos();
        Cautela = false;
        Status = "MASTER CAUTION reconhecido — acende de novo se aparecer coisa nova";
    }

    /// <summary>Chamado no Loaded da pagina: usa o retrato de ate 60 s atras se houver.</summary>
    /// <summary>Chamado ao abrir a secao: rele sozinho se o retrato tiver mais de 5 min (leitura e de graca, sem admin).</summary>
    public Task EnsureLoadedAsync() =>
        Loaded && _svc.Last is { } l && DateTimeOffset.Now - l.Quando < TimeSpan.FromMinutes(5) ? Task.CompletedTask : LoadAsync(Loaded);

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync(true);

    private async Task LoadAsync(bool force)
    {
        if (Loading) return;
        Loading = true;
        Status = "lendo o Windows… (~7 s, PowerShell em segundo plano)";
        if (!Loaded) AtencaoVazio = "lendo o Windows…";
        if (!Loaded) PortasResumo = PersistResumo = EventosResumo = "lendo…";
        try
        {
            var s = await _svc.GetAsync(force);
            Apply(s);
            var falhas = s.Erros.Where(e => !e.Key.StartsWith("run:")).Select(e => e.Key).ToList();
            Status = (falhas.Count > 0 ? $"sem leitura: {string.Join(", ", falhas)} (só admin lê) · " : "")
                     + $"lido {s.Quando.LocalDateTime:HH:mm:ss} em {s.Ms / 1000.0:0.0} s, {(s.Admin ? "como admin" : "sem admin")}";
            Loaded = true;
            Falha = "";
        }
        catch (Exception ex)
        {
            Falha = "A leitura do Windows falhou: " + ex.Message + " · clique Ler agora (F5).";
            Headline = "não consegui ler o Windows — clique LER AGORA (F5)";
            AtencaoVazio = "a leitura falhou — clique LER AGORA (F5)";
            Status = "falhou: " + ex.Message;
        }
        finally { Loading = false; }
    }

    private void Apply(HostSnapshot s)
    {
        Reset(Badges, HostSecurityService.Postura(s));

        var ev = s.Eventos.OrderByDescending(e => e.Quando).Select(HostSecurityService.Evento).ToList();
        Reset(Eventos, ev);
        TabDefender = ev.Count > 0 ? $"DEFENDER · {ev.Count}" : "DEFENDER";
        Reset(AsrRegras, s.Postura.Asr.Select(a => new SecHardeningRow
        {
            Titulo = HostSecurityService.AsrNome(a.Id),
            Detalhe = a.Acao == 1 ? "BLOQUEANDO" : a.Acao is 2 or 6 ? "só auditando (não bloqueia)" : "desligada",
        }));
        SemEventos = ev.Count == 0;
        EventosResumo = ev.Count == 0 ? "nenhuma ameaça nem bloqueio ASR nas últimas 24 h" : $"{ev.Count} nas últimas 24 h";

        var portas = _svc.Portas(s);
        var pers = _svc.Persistencia(s);
        ApplyListas(portas, pers);

        if (s.Hardening is { } h)
        {
            Score = h.Score.ToString("0.00");
            ScorePct = Math.Clamp((h.Score - 1) / 5 * 100, 0, 100);
            ScoreAccent = h.Score >= 5 ? "green" : h.Score >= 3.5 ? "amber" : "magenta";
            ScoreLegenda = $"de 6 · {h.Passou}/{h.Total} ok · {h.Alto} altos · {h.Medio} médios · {h.Baixo} baixos · {h.Quando.LocalDateTime:dd/MM HH:mm}";
            Reset(Altos, h.Altos.Select(a => new SecHardeningRow { Titulo = $"{a.Categoria} · {a.Nome}", Detalhe = $"hoje {Vazio(a.Atual)} → recomendado {Vazio(a.Recomendado)}" }));
            TabHardening = h.Alto > 0 ? $"Hardening · {h.Alto}" : "Hardening";
            AltosResumo = h.Alto == 0 ? $"0 falhas altas na auditoria de {h.Quando.LocalDateTime:dd/MM}" : $"{h.Alto} falhas de severidade alta (CIS) · auditoria de {h.Quando.LocalDateTime:dd/MM}";
        }
        else
        {
            Score = "—";
            ScorePct = 0;
            ScoreAccent = "violet";
            ScoreLegenda = s.HardeningKittyInstalado
                ? "nunca auditado · AUDITAR roda o HardeningKitty (só lê, pede admin, ~3 min)"
                : "HardeningKitty não está instalado (Install-Module HardeningKitty)";
            Altos.Clear();
            TabHardening = "Hardening";
            AltosResumo = "nunca auditado — AUDITAR (UAC, ~3 min, só lê)";
        }

        var alerta = portas.Count(r => r.PodeAprovar) + pers.Count(r => r.PodeAprovar);
        var novos = portas.Count(r => r.Estado == SecRow.Novo) + pers.Count(r => r.Estado == SecRow.Novo);
        var fracos = HostSecurityService.Postura(s).Where(b => b.Accent == "amber").Select(b => b.Rotulo.ToLowerInvariant()).ToList();
        // Ordem = o que mais importa primeiro (se cortar, corta o menos grave)
        // uma frase de gente: o que esta desligado + quantas decisoes faltam
        static string Nome(string r) => r switch { "bitlocker" => "BitLocker", "hvci" => "HVCI", "defender" => "Defender", "tamper" => "Proteção contra violação", "firewall" => "Firewall", "asr" => "ASR", _ => r };
        var partes = new List<string>();
        // hardening e nota (3,2/6), nao liga/desliga: "hardening desligado" nao fazia sentido
        foreach (var f in fracos) partes.Add(f == "hardening" ? "hardening com nota baixa" : $"{Nome(f)} desligado");
        if (alerta > 0) partes.Add($"{alerta} {(alerta == 1 ? "item" : "itens")} pra você decidir");
        if (novos > 0) partes.Add($"{novos} {(novos == 1 ? "novo" : "novos")} esta semana");
        Headline = partes.Count == 0 ? "Tudo certo. Nada pede você agora." : string.Join(" · ", partes);
        _cautelaAgora = novos > 0 || fracos.Count > 0 || ev.Count > 0
            ? string.Join("|", fracos) + "|" + string.Join("|", portas.Concat(pers).Where(r => r.Estado == SecRow.Novo).Select(r => r.Key)) + "|" + ev.Count
            : "";
        Cautela = AlertaPendente || (_cautelaAgora.Length > 0 && _cautelaAgora != _cautelaVista);
        PodeDesfazer = _svc.PodeDesfazer;
    }

    public void AtualizarAlertas()
    {
        Reset(Alertas, _watch.Alertas());
        SemAlertas = Alertas.Count == 0;
        VigiaEstado = _watch.Estado;
        VigiaBase = _watch.BaselineResumo();
        var n = _watch.NaoVistos;
        TabAlertas = n > 0 ? $"Alertas · {n}" : "Alertas";
        // MASTER CAUTION: alerta nao visto do vigia OU o que ja acendia pela leitura (protecao/novo/defender)
        AlertaPendente = n > 0;
        Cautela = AlertaPendente || (_cautelaAgora.Length > 0 && _cautelaAgora != _cautelaVista);
    }

    [ObservableProperty] private bool _alertaPendente;

    // ---------- master-detail (ACHADOS): selecionar uma linha mostra o que e, o risco, o que fazer, a evidencia e a NOTA do usuario ----------
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TemSelecao), nameof(SemSelecao))]
    private SecRow? _selecionado;
    [ObservableProperty] private string _selecionadoNota = "";
    public bool TemSelecao => Selecionado is not null;
    public bool SemSelecao => Selecionado is null;

    partial void OnSelecionadoChanged(SecRow? value) => SelecionadoNota = value?.Nota ?? "";

    /// <summary>Grava a nota do item selecionado (user data: &lt;data&gt;\factory\seguranca-notas.json).</summary>
    public void SalvarNota()
    {
        if (Selecionado is not { } r || (r.Nota == SelecionadoNota.Trim())) return;
        _svc.SetNota(r.NotaChave, SelecionadoNota);
        r.Nota = SelecionadoNota.Trim();
        Status = r.Nota.Length > 0 ? $"nota salva em {r.Titulo}" : $"nota removida de {r.Titulo}";
        if (r.Kind == "postura") _ = LoadAsync(true);   // o selo vira "de proposito" (ou volta a alarmar)
    }

    /// <summary>De qualquer lista: abre ACHADOS com este item selecionado.</summary>
    public void IrAoAchado(SecRow row)
    {
        Filtro = "TUDO";
        AplicarFiltro();
        Selecionado = _achadosTodos.FirstOrDefault(r => r.Kind == row.Kind && r.Key == row.Key) ?? row;
        Aba = 1;
    }

    partial void OnVigiaLigadaChanged(bool value)
    {
        _watch.Ligar(value);
        VigiaEstado = _watch.Estado;
    }

    [RelayCommand]
    private void AlertaVisto(SecAlert? a) { if (a is not null) _watch.Visto(a.Id); }

    [RelayCommand]
    private void AlertaSilenciar(SecAlert? a)
    {
        if (a is null) return;
        _watch.Silenciar(a.Id, TimeSpan.FromHours(24));
        Status = $"silenciado por 24 h: {a.Titulo}";
    }

    [RelayCommand]
    private void AlertasVistos() => _watch.VistoTodos();

    [RelayCommand]
    private void TestarAlerta() => _watch.Testar();

    [RelayCommand]
    private void Filtrar(string? f)
    {
        Filtro = f ?? "TUDO";
        AplicarFiltro();
    }

    private void AplicarFiltro()
    {
        var l = Filtro == "TUDO" ? _achadosTodos : _achadosTodos.Where(r => r.Fonte == Filtro).ToList();
        Reset(Achados, l);
        AchadosGrupos = Agrupar(l);
        GruposMudou?.Invoke(this, EventArgs.Empty);
        SemAchados = l.Count == 0;
        string C(string f) => (f == "TUDO" ? _achadosTodos.Count : _achadosTodos.Count(r => r.Fonte == f)) is var n ? $"{(Filtro == f ? "● " : "")}{f} {n}" : f;
        FTudo = C("TUDO"); FRede = C("REDE"); FInicia = C("INICIA"); FDefender = C("DEFENDER"); FPostura = C("POSTURA");
        AchadosResumo = MostrarWindows ? "tudo, inclusive o que é do Windows e o que você já marcou como confiável" : "só o que pede olho: suspeito, novo, a revisar, eventos do Defender e proteção desligada";
    }

    [RelayCommand]
    private void VerAchados() => Aba = 1;

    [RelayCommand]
    private void FecharDetalhe() => Selecionado = null;

    /// <summary>Navegacao integrada entre abas: "achados:REDE" (aba ACHADOS ja filtrada) ou "aba:2".</summary>
    public void Ir(string destino)
    {
        if (destino.StartsWith("achados:")) { Filtro = destino[8..]; AplicarFiltro(); Aba = 1; }
        else if (destino.StartsWith("aba:") && int.TryParse(destino[4..], out var n)) Aba = n;
    }

    /// <summary>Abre a tela do Windows de um achado de postura (ou, com "tab:N", a aba do proprio Factory).</summary>
    public void AbrirUrl(string abrir)
    {
        if (abrir.StartsWith("tab:") && int.TryParse(abrir[4..], out var aba)) { Aba = aba; return; }
        try
        {
            var i = abrir.IndexOf(' ');
            Process.Start(i > 0 ? new ProcessStartInfo(abrir[..i], abrir[(i + 1)..]) { UseShellExecute = true } : new ProcessStartInfo(abrir) { UseShellExecute = true });
            Status = "abri a tela do Windows";
        }
        catch (Exception ex) { Status = "o Windows não abriu: " + ex.Message; }
    }

    [RelayCommand]
    private async Task ConvocarSquadAsync()
    {
        if (SquadRodando || _svc.Last is not { } s) { if (_svc.Last is null) SquadStatus = "espere a primeira leitura terminar"; return; }
        SquadRodando = true;
        SquadStatus = "convocando o squad… (Claude, ~30 a 60 s, só com o retrato de agora)";
        try
        {
            var r = await _squad.ConvocarAsync(s, _svc.Portas(s), _svc.Persistencia(s));
            Guardar($"## Parecer geral · {DateTime.Now:dd/MM HH:mm}\n{r}\n\n---\n");
            SquadStatus = $"parecer de {DateTime.Now:HH:mm} · salvo em {_squad.Pasta}";
        }
        catch (Exception ex) { SquadStatus = "o squad não respondeu: " + ex.Message; }
        finally { SquadRodando = false; }
    }

    private string HistoricoSquad => Path.Combine(_squad.Pasta, "historico.md");
    /// <summary>Poe o parecer novo em cima do historico e grava (user data: &lt;data&gt;\factory\seguranca-pareceres).</summary>
    private void Guardar(string bloco)
    {
        SquadTexto = bloco + SquadTexto;
        try { Directory.CreateDirectory(_squad.Pasta); File.WriteAllText(HistoricoSquad, SquadTexto, new System.Text.UTF8Encoding(false)); }
        catch (Exception) { /* sem disco: fica so na tela */ }
    }

    /// <summary>PERGUNTAR AO SQUAD sobre uma linha: abre a aba SQUAD e poe a resposta no topo.</summary>
    public async Task PerguntarAsync(SecRow row)
    {
        if (SquadRodando || _svc.Last is not { } s) return;
        Aba = 5;
        SquadRodando = true;
        SquadStatus = $"perguntando ao squad sobre {row.Titulo}…";
        try
        {
            var r = await _squad.PerguntarAsync(row, s);
            Guardar($"## Sobre {row.Titulo} · {DateTime.Now:dd/MM HH:mm}\n{r}\n\n---\n");
            SquadStatus = $"parecer sobre {row.Titulo} · {DateTime.Now:HH:mm}";
        }
        catch (Exception ex) { SquadStatus = "o squad não respondeu: " + ex.Message; }
        finally { SquadRodando = false; }
    }

    private void ApplyListas(IReadOnlyList<SecRow> portas, IReadOnlyList<SecRow> pers)
    {
        if (_svc.Last is { } snap)
        {
            _achadosTodos = _svc.Achados(snap, MostrarWindows).ToList();
            var graves = _svc.Achados(snap, false).Where(r => r.Peso <= 1 && r.Estado != SecRow.Intencional && r.Kind != "defender").ToList();
            var atencao = graves.Take(10).ToList();
            Reset(Atencao, atencao);
            AtencaoGrupos = Agrupar(atencao);
            GruposMudou?.Invoke(this, EventArgs.Empty);
            SemAtencao = atencao.Count == 0;
            var revisar = _svc.Achados(snap, false).Count(r => r.Peso == 1 && r.Kind != "defender");
            AtencaoVazio = atencao.Count == 0 ? (revisar > 0 ? $"nada suspeito nem novo agora · {revisar} a revisar na aba ACHADOS" : "nada suspeito nem novo agora") : "";
            AtencaoMais = Math.Max(0, graves.Count - atencao.Count);
            var pedem = _svc.Achados(snap, false).Count(r => r.Peso <= 1);
            TabAchados = pedem > 0 ? $"O que fazer · {pedem}" : "O que fazer";
            AplicarFiltro();
        }
        Reset(Portas, portas.Where(r => MostrarWindows || r.Estado != SecRow.Sistema));
        Reset(Persistencia, pers.Where(r => MostrarWindows || r.Estado != SecRow.Sistema));
        TemRevisarPortas = portas.Any(r => r.PodeAprovar);
        TemRevisarPersist = pers.Any(r => r.PodeAprovar);
        PortasResumo = Resumo(portas, "processo", "processos");
        PersistResumo = Resumo(pers, "item", "itens");
    }

    private static string Resumo(IReadOnlyList<SecRow> rows, string um, string varios)
    {
        int n(string e) => rows.Count(r => r.Estado == e);
        var partes = new List<string>();
        if (n(SecRow.Novo) > 0) partes.Add($"{n(SecRow.Novo)} novos");
        if (n(SecRow.Revisar) > 0) partes.Add($"{n(SecRow.Revisar)} a revisar");
        partes.Add($"{n(SecRow.Conhecido)} conhecidos");
        partes.Add($"{n(SecRow.Sistema)} do Windows");
        return $"{rows.Count} {(rows.Count == 1 ? um : varios)} · " + string.Join(" · ", partes);
    }

    partial void OnMostrarWindowsChanged(bool value)
    {
        OnPropertyChanged(nameof(WindowsLabel));
        if (_svc.Last is { } s) ApplyListas(_svc.Portas(s), _svc.Persistencia(s));
    }

    [RelayCommand]
    private void ToggleWindows() => MostrarWindows = !MostrarWindows;

    [RelayCommand]
    private void Aprovar(SecRow? row)
    {
        if (row is null) return;
        Status = _svc.Aprovar([row]);
        if (_svc.Last is { } s) Apply(s);
    }

    [RelayCommand]
    private void Desfazer()
    {
        Status = _svc.Desfazer();
        if (_svc.Last is { } s) Apply(s);
    }

    [RelayCommand]
    private void AprovarPortas() => AprovarTodos(Portas);

    [RelayCommand]
    private void AprovarPersistencia() => AprovarTodos(Persistencia);

    /// <summary>"Todos" = so os REVISAR (o que ja existia). NOVO pede olho, um por um.</summary>
    private void AprovarTodos(IEnumerable<SecRow> rows)
    {
        Status = _svc.Aprovar(rows.Where(r => r.Estado == SecRow.Revisar).ToList());
        if (_svc.Last is { } s) Apply(s);
    }

    /// <summary>Abre o Explorer com o executavel selecionado (nada roda).</summary>
    [RelayCommand]
    private void Inspecionar(SecRow? row)
    {
        if (row is null || string.IsNullOrEmpty(row.Caminho)) return;
        var path = Environment.ExpandEnvironmentVariables(row.Caminho);
        if (File.Exists(path)) Process.Start("explorer.exe", $"/select,\"{path}\"");
        else if (Directory.Exists(Path.GetDirectoryName(path))) Process.Start("explorer.exe", $"\"{Path.GetDirectoryName(path)}\"");
        else Status = $"o arquivo não existe mais: {path}";
    }

    [RelayCommand]
    private async Task AuditarAsync()
    {
        if (Auditando) return;
        Auditando = true;
        Status = "HardeningKitty rodando (só lê, ~3 min) — aceite o UAC do Windows";
        try { Status = "auditoria: " + await _svc.RodarAuditoriaAsync(); if (_svc.Last is { } s) Apply(s); }
        catch (Exception ex) { Status = "auditoria falhou: " + ex.Message; }
        finally { Auditando = false; }
    }

    private static string Vazio(string s) => string.IsNullOrWhiteSpace(s) ? "(vazio)" : s;

    private static void Reset<T>(ObservableCollection<T> col, IEnumerable<T> items)
    {
        col.Clear();
        foreach (var i in items) col.Add(i);
    }
}
