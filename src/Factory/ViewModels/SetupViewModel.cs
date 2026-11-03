using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Factory.Models;
using Factory.Services;
using Microsoft.UI.Dispatching;

namespace Factory.ViewModels;

/// <summary>
/// SETUP: painel de controle do setup do Claude Code — o que esta instalado (skills, agentes,
/// plugins, MCP, hooks), de onde vem, e quanto foi usado de verdade nas sessoes. Selos: 🔥 nucleo
/// (top do tipo), 🪦 morto (0 no historico retido), 💎 joia (marca do usuario). So leitura em ~/.claude;
/// nada desliga nada nesta versao. Segundo bloco: MAPA DO FACTORY (maos, scripts, onde escreve).
/// </summary>
public sealed partial class SetupViewModel : ObservableObject
{
    private const int NucleoPorTipo = 5;
    private const int NucleoMinimo = 3;
    private static readonly TimeSpan AutoRefreshApos = TimeSpan.FromMinutes(10);

    private readonly ISetupInventory _inventory;
    private readonly ISetupUsage _usage;
    private readonly ILauncher _launcher;
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _relogio;

    private SetupInventario? _inv;
    private UsoResumo? _uso;
    private HashSet<string> _joias = new(StringComparer.OrdinalIgnoreCase);
    private List<SetupItem> _todos = [];
    private bool _carregado;

    public ObservableCollection<SetupItem> Itens { get; } = [];
    public ObservableCollection<SetupFiltro> Tipos { get; } = [];
    public ObservableCollection<SetupFiltro> Selos { get; } = [];
    public ObservableCollection<MaoOdin> Maos { get; } = [];
    public ObservableCollection<ScriptFactory> Scripts { get; } = [];

    /// <summary>Espelha a regra de escrita do CLAUDE.md do projeto (lista estatica de proposito).</summary>
    public IReadOnlyList<EscritaFactory> Escritas { get; } =
    [
        new(@"<projeto>\factory.json", "SALVAR no card do projeto"),
        new(@"<projeto>\STATUS.md", "só com diálogo de confirmação, deixa .bak"),
        new(@".factory\contextos\", "pacotes de CONTEXTOS"),
        new(@".factory\deliverables\audio\", "áudios do ESTÚDIO"),
        new(@"<raiz>\<nome>", "projeto novo da FÁBRICA"),
        new(@"<destino>\inbox\*.md", "ENTRADA — só arquivo novo (entrada/v1)"),
        new(@".factory\factory\destinos.json", "favoritos da ENTRADA"),
        new(@".factory\factory\setup-joias.json", "jóias marcadas no SETUP"),
        new(@".factory\factory\decisoes.jsonl", "decisões do PLANO — só acrescenta linha (decisao/v1)"),
        new(@".factory\odin\", "notas, tarefas e falhas das mãos do Odin"),
        new(@"%LOCALAPPDATA%\Factory\", "cache (índice de sessões, briefings, setup-uso.json) e chat/odin-log/apps/painel — dado do usuario fora do backup"),
    ];

    public string[] Ordens { get; } = ["mais usado", "último uso", "nome"];

    [ObservableProperty] private string _busca = "";
    /// <summary>Anel do cabecalho: 1a leitura do inventario E scan das sessoes (ux-review: so o scan ligava o anel).</summary>
    [ObservableProperty] private bool _ocupado;
    [ObservableProperty] private string _ordem = "mais usado";
    [ObservableProperty] private string _resumo = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _nota = "";
    [ObservableProperty] private string _claudeMd = "";
    [ObservableProperty] private string _listaTitulo = "";
    [ObservableProperty] private string _maosTitulo = "";
    [ObservableProperty] private string _scriptsTitulo = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SemItens))]
    private bool _temItens;

    public bool SemItens => !TemItens && _carregado;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MostraSetup), nameof(SetupAbaBorda), nameof(MapaAbaBorda), nameof(SetupAbaFrente), nameof(MapaAbaFrente))]
    private bool _mostraMapa;

    public bool MostraSetup => !MostraMapa;
    public Microsoft.UI.Xaml.Media.Brush SetupAbaBorda => Brush(!MostraMapa ? "NeonCyan" : "CyberPanelEdge");
    public Microsoft.UI.Xaml.Media.Brush MapaAbaBorda => Brush(MostraMapa ? "NeonCyan" : "CyberPanelEdge");
    public Microsoft.UI.Xaml.Media.Brush SetupAbaFrente => Brush(!MostraMapa ? "NeonCyan" : "CyberDim");
    public Microsoft.UI.Xaml.Media.Brush MapaAbaFrente => Brush(MostraMapa ? "NeonCyan" : "CyberDim");

    public SetupViewModel(ISetupInventory inventory, ISetupUsage usage, ILauncher launcher)
    {
        _inventory = inventory;
        _usage = usage;
        _launcher = launcher;
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        foreach (var (k, r, tip) in new[]
                 {
                     ("", "TUDO", "todos os tipos"),
                     ("skill", "SKILLS", "skills (~/.claude/skills, plugins e projetos)"),
                     ("agente", "AGENTES", "subagentes (~/.claude/agents, plugins e projetos)"),
                     ("plugin", "PLUGINS", "plugins instalados ou sincronizados"),
                     ("mcp", "MCP", "servidores MCP (~/.claude.json e plugins)"),
                     ("hook", "HOOKS", "hooks do settings.json (uso não medido)"),
                 })
            Tipos.Add(new SetupFiltro { Chave = k, Rotulo = r, Tooltip = tip, Marcado = k == "" });

        foreach (var (k, r, tip) in new[]
                 {
                     ("", "TODOS", "sem filtro de selo"),
                     ("nucleo", "🔥 NÚCLEO", $"top {NucleoPorTipo} de cada tipo em uso (mínimo {NucleoMinimo} usos)"),
                     ("morto", "🪦 MORTOS", "0 usos no histórico retido — candidatos a limpar"),
                     ("joia", "💎 JÓIAS", "marcadas por você"),
                 })
            Selos.Add(new SetupFiltro { Chave = k, Rotulo = r, Tooltip = tip, Marcado = k == "" });

        // "atualizado ha X" envelhece sozinho, sem reler nada
        _relogio = _dispatcher.CreateTimer();
        _relogio.Interval = TimeSpan.FromMinutes(1);
        _relogio.Tick += (_, _) => AtualizarStatusIdade();
        _relogio.Start();
    }

    /// <summary>Chamado pela pagina ao abrir: inventario e cache na hora; scan incremental em fundo
    /// se o cache tiver mais de 10 min (so le disco, nao gasta nada).</summary>
    public async Task AbrirAsync()
    {
        if (!_carregado)
        {
            Ocupado = true;
            Status = "lendo inventário do Claude Code…";
            try
            {
                _joias = _inventory.LerJoias();
                await RecarregarInventarioAsync();
                _uso = await Task.Run(_usage.Cache);
                var (maos, scripts) = await Task.Run(_inventory.LerMapa);
                Maos.Clear();
                foreach (var m in maos) Maos.Add(m);
                Scripts.Clear();
                foreach (var s in scripts) Scripts.Add(s);
                MaosTitulo = $"MÃOS DO ODIN · {Maos.Count}";
                ScriptsTitulo = $"SCRIPTS EM tools\\ · {Scripts.Count}";
                _carregado = true;
            }
            finally { Ocupado = false; }
            Montar();
        }

        if ((_uso is null || DateTimeOffset.Now - _uso.Atualizado > AutoRefreshApos) && !AtualizarCommand.IsRunning)
            await AtualizarCommand.ExecuteAsync(null);
        else AtualizarStatusIdade();
    }

    private async Task RecarregarInventarioAsync()
    {
        _inv = await Task.Run(_inventory.Ler);
        ClaudeMd = $"CLAUDE.md: {(_inv.ClaudeMdGlobal ? "1 global" : "sem global")} + {_inv.ClaudeMdProjetos} em projetos da raiz";
    }

    /// <summary>ATUALIZAR: rele o inventario e faz o scan incremental das sessoes.</summary>
    [RelayCommand]
    private async Task AtualizarAsync()
    {
        Ocupado = true;
        try
        {
            await RecarregarInventarioAsync();
            Montar();
            var progresso = new Progress<string>(s => Status = s);
            Status = _uso is null ? "primeira leitura das sessões — pode levar um minuto…" : "procurando sessões novas…";
            _uso = await _usage.AtualizarAsync(progresso, CancellationToken.None);
            Montar();
            AtualizarStatusIdade(forcar: true);
        }
        catch (Exception ex)
        {
            Status = $"falhou ao ler as sessões: {ex.Message} — tente ATUALIZAR de novo";
        }
        finally { Ocupado = false; }
    }

    [RelayCommand]
    private void VerSetup() => MostraMapa = false;

    [RelayCommand]
    private void VerMapa() => MostraMapa = true;

    partial void OnBuscaChanged(string value) => Filtrar();
    partial void OnOrdemChanged(string value) => Filtrar();

    public void EscolherTipo(SetupFiltro f)
    {
        foreach (var t in Tipos) t.Marcado = t == f;
        Filtrar();
    }

    public void EscolherSelo(SetupFiltro f)
    {
        foreach (var s in Selos) s.Marcado = s == f;
        Filtrar();
    }

    public void AlternarJoia(SetupItem item)
    {
        item.IsJoia = !item.IsJoia;
        if (item.IsJoia) _joias.Add(item.Chave); else _joias.Remove(item.Chave);
        try
        {
            _inventory.GravarJoias(_joias);
            Status = item.IsJoia ? $"💎 {item.Nome} marcada — salvo em setup-joias.json" : $"{item.Nome} desmarcada — salvo em setup-joias.json";
        }
        catch (Exception ex)
        {
            Status = $"não consegui gravar {_inventory.JoiasFile}: {ex.Message}";
        }
        ContarSelos();
    }

    public void AbrirPasta(string caminho)
    {
        if (string.IsNullOrEmpty(caminho)) return;
        // pasta sempre: explorer com caminho de arquivo ABRE o arquivo (e .mjs pode executar)
        var dir = Directory.Exists(caminho) ? caminho : Path.GetDirectoryName(caminho);
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) _launcher.OpenExplorer(dir);
    }

    // ================= montagem =================

    /// <summary>Cruza inventario + uso + joias em SetupItem. Uso sem item no inventario (agente
    /// embutido, conector claude.ai, skill apagada) entra como "sem arquivo".</summary>
    private void Montar()
    {
        if (_inv is null) return;
        var uso = _uso?.Itens ?? new Dictionary<string, UsoItem>();
        var medido = _uso is not null;
        var lista = new List<SetupItem>();
        var cobertos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // uso de plugin = soma das pecas do plugin (skill/agente/MCP com o prefixo)
        var usoPlugin = new Dictionary<string, (int Vezes, string Ultimo, int Projs)>(StringComparer.OrdinalIgnoreCase);

        foreach (var i in _inv.Itens.Where(i => i.Tipo != "plugin"))
        {
            var chave = $"{i.Tipo}:{i.Nome}";
            uso.TryGetValue(chave, out var u);
            cobertos.Add(chave);
            lista.Add(Novo(i, u, medido));
            if (i.PluginDono is { } dono && u is not null)
            {
                usoPlugin.TryGetValue(dono, out var s);
                usoPlugin[dono] = (s.Vezes + u.Vezes, string.CompareOrdinal(u.Ultimo, s.Ultimo ?? "") > 0 ? u.Ultimo : s.Ultimo ?? "", Math.Max(s.Projs, u.Projetos));
            }
        }

        foreach (var p in _inv.Itens.Where(i => i.Tipo == "plugin"))
        {
            usoPlugin.TryGetValue(p.Nome, out var s);
            lista.Add(Novo(p, s.Vezes > 0 ? new UsoItem(s.Vezes, s.Ultimo, s.Projs) : null, medido));
        }

        foreach (var (chave, u) in uso)
        {
            if (cobertos.Contains(chave)) continue;
            var sep = chave.IndexOf(':');
            var tipo = chave[..sep];
            var nome = chave[(sep + 1)..];
            lista.Add(Novo(new ItemInventario(tipo, nome, OrigemSemArquivo(tipo, nome), "sem arquivo", ""), u, medido));
        }

        // selos: nucleo = top N do tipo com minimo de usos; morto = medido e 0
        foreach (var grupo in lista.GroupBy(i => i.Tipo))
            foreach (var item in grupo.Where(i => i.Vezes >= NucleoMinimo).OrderByDescending(i => i.Vezes).Take(NucleoPorTipo))
                item.IsNucleo = true;
        foreach (var item in lista)
            item.IsMorto = medido && item.Medido && item.Vezes == 0;

        _todos = lista;

        foreach (var t in Tipos)
            t.Contagem = t.Chave == "" ? lista.Count : lista.Count(i => i.Tipo == t.Chave);
        ContarSelos();

        var mortos = lista.Count(i => i.IsMorto);
        Resumo = medido
            ? $"{lista.Count} peças · {lista.Count(i => i.IsNucleo)} no núcleo · {mortos} sem uso"
            : $"{lista.Count} peças · uso ainda não lido";
        Nota = _uso is { DesdeData.Length: 10 } u2
            ? $"O Claude Code apaga sessões antigas (padrão 30 dias, cleanupPeriodDays). \"0 usos\" = 0 no histórico retido: {u2.Sessoes} sessões, desde {u2.DesdeData[8..10]}/{u2.DesdeData[5..7]}/{u2.DesdeData[..4]}."
            : "O Claude Code apaga sessões antigas (padrão 30 dias, cleanupPeriodDays). \"0 usos\" = 0 no histórico que ainda existe.";
        Filtrar();
    }

    private SetupItem Novo(ItemInventario i, UsoItem? u, bool medido) => new()
    {
        Tipo = i.Tipo,
        Nome = i.Nome,
        Descricao = i.Descricao,
        Origem = i.Origem,
        Caminho = i.Caminho,
        Desligado = i.Desligado,
        Medido = i.Medido && medido,
        Vezes = u?.Vezes ?? 0,
        Ultimo = u?.Ultimo ?? "",
        Projetos = u?.Projetos ?? 0,
        IsJoia = _joias.Contains($"{i.Tipo}:{i.Nome}"),
    };

    private static string OrigemSemArquivo(string tipo, string nome) => tipo switch
    {
        "agente" => "agente embutido do Claude Code, ou apagado",
        "mcp" when nome.StartsWith("claude_ai_", StringComparison.OrdinalIgnoreCase) => "conector do claude.ai (vem da conta, não do disco)",
        "mcp" => "servidor que não está mais em ~/.claude.json nem em plugin",
        _ => "skill embutida do Claude Code, ou apagada/renomeada",
    };

    private void ContarSelos()
    {
        foreach (var s in Selos)
            s.Contagem = s.Chave switch
            {
                "nucleo" => _todos.Count(i => i.IsNucleo),
                "morto" => _todos.Count(i => i.IsMorto),
                "joia" => _todos.Count(i => i.IsJoia),
                _ => _todos.Count,
            };
    }

    private void Filtrar()
    {
        var tipo = Tipos.FirstOrDefault(t => t.Marcado)?.Chave ?? "";
        var selo = Selos.FirstOrDefault(s => s.Marcado)?.Chave ?? "";
        var q = Busca.Trim();

        IEnumerable<SetupItem> r = _todos;
        if (tipo != "") r = r.Where(i => i.Tipo == tipo);
        r = selo switch
        {
            "nucleo" => r.Where(i => i.IsNucleo),
            "morto" => r.Where(i => i.IsMorto),
            "joia" => r.Where(i => i.IsJoia),
            _ => r,
        };
        if (q != "")
            r = r.Where(i => i.Nome.Contains(q, StringComparison.OrdinalIgnoreCase)
                             || i.Descricao.Contains(q, StringComparison.OrdinalIgnoreCase)
                             || i.Origem.Contains(q, StringComparison.OrdinalIgnoreCase));
        r = Ordem switch
        {
            "último uso" => r.OrderByDescending(i => i.Ultimo, StringComparer.Ordinal).ThenByDescending(i => i.Vezes).ThenBy(i => i.Nome),
            "nome" => r.OrderBy(i => i.Nome, StringComparer.OrdinalIgnoreCase),
            _ => r.OrderByDescending(i => i.Vezes).ThenByDescending(i => i.IsJoia).ThenBy(i => i.Nome, StringComparer.OrdinalIgnoreCase),
        };

        var final = r.ToList();
        Itens.Clear();
        foreach (var i in final) Itens.Add(i);
        TemItens = final.Count > 0;
        ListaTitulo = final.Count == _todos.Count ? $"{final.Count} PEÇAS" : $"{final.Count} DE {_todos.Count} PEÇAS";
        OnPropertyChanged(nameof(SemItens));
    }

    private void AtualizarStatusIdade(bool forcar = false)
    {
        if (AtualizarCommand.IsRunning && !forcar) return;
        if (_uso is null) { Status = "uso ainda não lido — clique ATUALIZAR"; return; }
        var d = DateTimeOffset.Now - _uso.Atualizado;
        var idade = d.TotalMinutes < 1 ? "agora" : d.TotalHours < 1 ? $"há {(int)d.TotalMinutes} min"
            : d.TotalDays < 1 ? $"há {(int)d.TotalHours} h" : $"há {(int)d.TotalDays} dia(s)";
        Status = $"uso atualizado {idade} · {_uso.Sessoes} sessões lidas";
    }

    private static Microsoft.UI.Xaml.Media.Brush Brush(string key) =>
        (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources[key];
}
