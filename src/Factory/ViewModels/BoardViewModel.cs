using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Factory.Models;
using Factory.Services;

namespace Factory.ViewModels;

/// <summary>
/// BOARD: what is open in STATUS and in docs\plano-*.md, as cards the user decides GOOD / BAD / LATER on without
/// opening the .md. Sources are read-only; the decision is appended to &lt;data&gt;\factory\decisoes.jsonl and the card
/// moves from TO DECIDE to DECIDED (the newest decision per id wins).
/// </summary>
public sealed partial class BoardViewModel : ObservableObject
{
    private readonly IPlanoService _plano;
    private readonly ILauncher _launcher;
    private readonly List<BoardCard> _todos = [];
    private bool _carregado;

    /// <summary>Filtro de fonte (mesmo chip do SETUP; Contagem = a decidir naquela fonte).</summary>
    public ObservableCollection<SetupFiltro> Fontes { get; } = [];
    public ObservableCollection<BoardCard> ADecidir { get; } = [];
    public ObservableCollection<BoardCard> Rumo { get; } = [];
    [ObservableProperty] private bool _temRumo;
    public ObservableCollection<BoardCard> Decididos { get; } = [];

    [ObservableProperty] private string _resumo = "lendo…";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _aDecidirTitulo = "A DECIDIR";
    [ObservableProperty] private string _decididosTitulo = "DECIDIDO";
    [ObservableProperty] private bool _ocupado;
    [ObservableProperty] private bool _semADecidir;
    [ObservableProperty] private bool _temDecididos;
    [ObservableProperty] private string _faltas = "";
    [ObservableProperty] private bool _temFaltas;

    // 107 cards to approve was a wall: em TODOS so os 5 mais importantes; o resto vira 1 faixa com
    // ARQUIVAR (grava DEPOIS em lote, reversivel em DECIDIDO) ou VER TODOS.
    private const int FilaMax = 5;
    private readonly List<BoardCard> _resto = [];
    [ObservableProperty] private bool _verTodos;
    [ObservableProperty] private bool _temResto;
    [ObservableProperty] private string _restoTexto = "";
    [ObservableProperty] private string _verTodosRotulo = "VER TODOS";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DecididosChevron))]
    private bool _decididosAberto;

    /// <summary>E70D = chevron pra baixo (abrir), E70E = pra cima (fechar) — mesmo da FROTA.</summary>
    public string DecididosChevron => DecididosAberto ? "" : "";

    private string _fonte = "todos";

    public BoardViewModel(IPlanoService plano, ILauncher launcher)
    {
        _plano = plano;
        _launcher = launcher;
        foreach (var (k, r, tip) in new[]
                 {
                     ("todos", "TODOS", "todas as fontes"),
                     ("rumo", "RUMO", @"factory\docs\plano-rumo.md — norte, placar do Odin e etapas (sempre no topo)"),
                     ("status", "STATUS", @"factory\STATUS.md — Onde paramos (3 do topo), Próxima ação, Bloqueios, Fila"),
                     ("planos", "PLANOS", @"factory\docs\plano-*.md — cada seção ##"),
                 })
            Fontes.Add(new SetupFiltro { Chave = k, Rotulo = r, Tooltip = tip, Marcado = k == _fonte });
    }

    /// <summary>Primeira abertura le do disco; as seguintes so re-filtram (ATUALIZAR rele).</summary>
    public async Task AbrirAsync()
    {
        if (_carregado) return;
        await AtualizarAsync();
    }

    [RelayCommand]
    private void AlternarDecididos() => DecididosAberto = !DecididosAberto;

    [RelayCommand]
    private void AlternarVerTodos() { VerTodos = !VerTodos; Refiltrar(); }

    /// <summary>Grava DEPOIS em lote pro que ficou fora dos 5 de cima (append no decisoes.jsonl; muda em DECIDIDO).</summary>
    [RelayCommand]
    private void ArquivarResto()
    {
        var n = 0;
        try
        {
            foreach (var c in _resto.ToList())
            {
                Aplicar(c, _plano.Gravar(c, "depois", "arquivado em lote no PLANO: fora dos 5 de cima"));
                n++;
            }
            Status = $"✓ {n} arquivados como DEPOIS — continuam em DECIDIDO, dá pra mudar um por um";
        }
        catch (Exception ex) { Status = $"arquivei {n} e parei: {ex.Message}"; }
        Refiltrar();
    }

    // order of importance: rumo (north star/scoreboard/stages) > STATUS > plans
    private static int Peso(BoardCard c) => c.Fonte switch { "rumo" => 0, "status" => 1, "planos" => 2, _ => 3 };

    [RelayCommand]
    private async Task AtualizarAsync()
    {
        if (Ocupado) return;
        Ocupado = true;
        try
        {
            var (leitura, decisoes, invalidas) = await Task.Run(() =>
            {
                var l = _plano.Ler();
                var d = _plano.LerDecisoes(out var inv);
                return (l, d, inv);
            });

            _todos.Clear();
            foreach (var c in leitura.Cards)
            {
                if (decisoes.TryGetValue(c.Id, out var d)) Aplicar(c, d);
                _todos.Add(c);
            }

            var faltas = leitura.Faltas.ToList();
            if (invalidas > 0) faltas.Add($"decisoes.jsonl: {invalidas} linha(s) inválida(s) ignorada(s)");
            Faltas = string.Join('\n', faltas);
            TemFaltas = faltas.Count > 0;
            _carregado = true;
            Status = $"lido agora · {_todos.Count} itens nas fontes · decisões em {_plano.DecisoesPath}";
            Refiltrar();
        }
        catch (Exception ex)
        {
            Status = $"não deu pra ler as fontes: {ex.Message}";
        }
        finally { Ocupado = false; }
    }

    public void EscolherFonte(SetupFiltro f)
    {
        _fonte = f.Chave;
        foreach (var x in Fontes) x.Marcado = x == f;
        Refiltrar();
    }

    public void Decidir(BoardCard card, string decisao)
    {
        try
        {
            var d = _plano.Gravar(card, decisao, card.Nota);
            Aplicar(card, d);
            Status = $"✓ {d.Decisao.ToUpperInvariant()} gravado em decisoes.jsonl — {card.Titulo} (dá pra mudar em DECIDIDO)";
            Refiltrar();
        }
        catch (Exception ex)
        {
            Status = $"não gravou ({ex.Message}) — confira se {_plano.DecisoesPath} está aberto em outro programa";
        }
    }

    public void AbrirFonte(BoardCard card)
    {
        try { _launcher.OpenFile(card.Caminho); }
        catch (Exception)
        {
            // extensao sem app associado (.jsonl): abre a pasta, que sempre funciona
            _launcher.OpenExplorer(System.IO.Path.GetDirectoryName(card.Caminho) ?? card.Caminho);
            Status = $"{card.ArquivoNome} sem app padrão — abri a pasta; o item está na linha {card.Linha}";
        }
    }

    private static void Aplicar(BoardCard c, DecisaoPlano d)
    {
        c.NotaGravada = d.Nota;
        c.DecididoEm = d.Em == default ? null : d.Em;
        c.Decisao = d.Decisao;
        if (string.IsNullOrEmpty(c.Nota)) c.Nota = d.Nota;
    }

    private void Refiltrar()
    {
        // RUMO (norte + placar) sempre em cima; o resto na ordem das fontes
        var pendentes = _todos.Where(c => !c.Decidido).OrderBy(Peso).ToList();
        foreach (var f in Fontes)
            f.Contagem = f.Chave == "todos" ? pendentes.Count : pendentes.Count(c => c.Fonte == f.Chave);

        bool Passa(BoardCard c) => _fonte == "todos" || c.Fonte == _fonte;

        // RUMO e o mapa (tiles so de leitura, sempre inteiro); DECIDA AGORA sao os 5 de cima do resto
        Rumo.Clear();
        if (_fonte is "todos" or "rumo")
            foreach (var c in _todos.Where(c => c.Fonte == "rumo")) Rumo.Add(c);
        TemRumo = Rumo.Count > 0;

        var lista = pendentes.Where(Passa).Where(c => c.Fonte != "rumo").ToList();
        _resto.Clear();
        if (_fonte == "todos" && !VerTodos && lista.Count > FilaMax)
        {
            _resto.AddRange(lista.Skip(FilaMax));
            lista = lista.Take(FilaMax).ToList();
        }
        ADecidir.Clear();
        foreach (var c in lista) ADecidir.Add(c);
        TemResto = _resto.Count > 0 || (VerTodos && _fonte == "todos" && pendentes.Count > FilaMax);
        var porFonte = string.Join(" · ", _resto.GroupBy(c => c.Selo).Select(g => $"{g.Key} {g.Count()}"));
        RestoTexto = _resto.Count > 0 ? $"+ {_resto.Count} itens menos importantes, antigos ou de outras sessões ({porFonte})" : "mostrando todos";
        VerTodosRotulo = VerTodos ? "SÓ OS 5 DE CIMA" : "VER TODOS";

        // decididos: o mais recente em cima
        Decididos.Clear();
        foreach (var c in _todos.Where(c => c.Decidido && Passa(c)).OrderByDescending(c => c.DecididoEm ?? DateTimeOffset.MinValue))
            Decididos.Add(c);

        Resumo = _resto.Count > 0 ? $"{ADecidir.Count} na tela · {_resto.Count} pra arquivar" : pendentes.Count == 1 ? "1 a decidir" : $"{pendentes.Count} a decidir";
        ADecidirTitulo = _resto.Count > 0 ? $"DECIDA AGORA · {ADecidir.Count}" : $"A DECIDIR · {ADecidir.Count}";
        DecididosTitulo = $"DECIDIDO · {Decididos.Count}";
        SemADecidir = _carregado && ADecidir.Count == 0;
        TemDecididos = Decididos.Count > 0;
    }
}
