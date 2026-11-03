using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Factory.Models;
using Factory.Services;

namespace Factory.ViewModels;

/// <summary>
/// ENTRADA: cola o contexto, escolhe (ou pede sugestao de) destino/tipo/prioridade, e vira um .md
/// em &lt;destino&gt;\inbox\. SALVAR E RODAR abre o terminal la com `claude` lendo o arquivo.
/// SUGERIR gasta so assinatura (claude -p), e so por clique.
/// </summary>
public sealed partial class InboxViewModel : ObservableObject
{
    private readonly ProjectCatalog _catalog;
    private readonly IInboxService _inbox;
    private readonly ILauncher _launcher;
    private List<Destino> _destinos = [];

    public IReadOnlyList<string> Tipos { get; } = InboxService.Tipos;
    public IReadOnlyList<string> Prioridades { get; } = InboxService.Prioridades;

    public ObservableCollection<Destino> DestinoSugestoes { get; } = [];
    public ObservableCollection<InboxItem> Recentes { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SugerirCommand), nameof(SalvarCommand), nameof(SalvarERodarCommand))]
    private string _contexto = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SalvarCommand), nameof(SalvarERodarCommand))]
    [NotifyPropertyChangedFor(nameof(DestinoCaminho))]
    private Destino? _destino;

    [ObservableProperty] private string _destinoTexto = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QuantidadeConta))]
    private string _tipo = "tarefa";

    [ObservableProperty] private string _prioridade = "P2";
    [ObservableProperty] private string _titulo = "";
    [ObservableProperty] private double _quantidade = 1;

    [ObservableProperty] private string _status = "";

    /// <summary>true = deu certo (ciano); false = aviso/erro (ambar). Guia: ambar so pra aviso.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusAviso))]
    private bool _statusOk;

    public bool StatusAviso => !StatusOk;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SemRecentes))]
    private bool _temRecentes;

    public bool SemRecentes => !TemRecentes;
    public string DestinoCaminho => Destino?.Caminho ?? "escolha um destino (digite o nome ou cole um caminho)";

    /// <summary>Quantidade so pesa em lote de artigos; nos outros tipos o campo fica apagado.</summary>
    public bool QuantidadeConta => Tipo == "artigos";

    public InboxViewModel(ProjectCatalog catalog, IInboxService inbox, ILauncher launcher)
    {
        _catalog = catalog;
        _inbox = inbox;
        _launcher = launcher;
        // enquanto o SUGERIR roda, salvar fica travado (senao a resposta atrasada cai em cima do pedido seguinte)
        SugerirCommand.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(SugerirCommand.IsRunning)) return;
            SalvarCommand.NotifyCanExecuteChanged();
            SalvarERodarCommand.NotifyCanExecuteChanged();
        };
        _catalog.Changed += (_, _) => Rebuild();
        Rebuild();
    }

    public void Rebuild()
    {
        if (_catalog.IsScanning) return;
        _destinos = _inbox.Destinos(_catalog.All).ToList();
        FiltrarDestinos(DestinoTexto);
        RefreshRecentes();
    }

    private void RefreshRecentes()
    {
        Recentes.Clear();
        foreach (var i in _inbox.Recentes(_destinos, 15)) Recentes.Add(i);
        TemRecentes = Recentes.Count > 0;
    }

    // ---------- destino ----------

    public void FiltrarDestinos(string texto)
    {
        var t = CommandLine.Fold(texto ?? "");
        DestinoSugestoes.Clear();
        var hits = t.Length == 0
            ? _destinos.Where(d => d.Origem == "favorito").Concat(_destinos.Where(d => d.Origem != "favorito")).Take(12)
            : _destinos.Where(d => CommandLine.Fold(d.Nome).Contains(t) || CommandLine.Fold(d.Caminho).Contains(t)).Take(12);
        foreach (var d in hits) DestinoSugestoes.Add(d);
    }

    public void EscolherDestino(Destino d)
    {
        Destino = d;
        DestinoTexto = d.Nome;
    }

    /// <summary>Enter no campo: nome exato, primeiro filtrado, ou caminho colado que exista.</summary>
    public void DestinoPorTexto(string texto)
    {
        texto = (texto ?? "").Trim().Trim('"');
        var d = _destinos.FirstOrDefault(x => x.Nome.Equals(texto, StringComparison.OrdinalIgnoreCase));
        if (d is null && Directory.Exists(texto))
            d = new Destino(Path.GetFileName(texto.TrimEnd('\\')), texto, "pasta");
        d ??= DestinoSugestoes.FirstOrDefault();
        if (d is not null) EscolherDestino(d);
        else Aviso("destino nao encontrado — digite parte do nome do projeto ou cole o caminho da pasta");
    }

    // ---------- estado ----------

    private void Ok(string msg) { Status = msg; StatusOk = true; }
    private void Aviso(string msg) { Status = msg; StatusOk = false; }

    // botao cinza sem dizer por que = estado invisivel (ux-review): o status explica o que falta
    partial void OnContextoChanged(string value) => ExplicarFalta();
    partial void OnDestinoChanged(Destino? value) => ExplicarFalta();

    private void ExplicarFalta()
    {
        if (TemContexto() && Destino is null)
            Aviso("falta o DESTINO — digite o nome do projeto ou clique SUGERIR");
        else if (Status.StartsWith("falta o DESTINO", StringComparison.Ordinal))
            Status = "";
    }

    // ---------- acoes ----------

    private bool TemContexto() => !string.IsNullOrWhiteSpace(Contexto);
    private bool PodeSalvar() => TemContexto() && Destino is not null && !SugerirCommand.IsRunning;

    [RelayCommand(CanExecute = nameof(TemContexto))]
    private async Task SugerirAsync()
    {
        var pedido = Contexto;
        Ok("lendo o contexto (claude -p, ~10 s)…");
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var s = await _inbox.SugerirAsync(pedido, _destinos, _catalog.All, cts.Token);
            // resposta atrasada de um pedido que ja mudou nao pode cair em cima do novo
            if (Contexto != pedido) { Aviso("sugestao descartada: o texto mudou enquanto ela rodava — clique SUGERIR de novo"); return; }

            var d = s.Destino is null ? null : _destinos.FirstOrDefault(x => x.Nome == s.Destino);
            if (d is not null) EscolherDestino(d);
            Tipo = s.Tipo;
            Prioridade = s.Prioridade;
            if (s.Titulo.Length > 0) Titulo = s.Titulo;
            Quantidade = s.Quantidade;
            if (d is null) Aviso("sugerido, mas nao achei o destino certo — escolha no campo DESTINO");
            else Ok($"sugerido: {d.Nome} · {s.Tipo} · {s.Prioridade} — confira e salve");
        }
        catch (OperationCanceledException)
        {
            Aviso("a sugestao demorou demais — tente de novo ou preencha os campos na mao");
        }
        catch (Exception ex)
        {
            Aviso($"a sugestao falhou ({ex.Message}) — preencha os campos na mao e salve");
        }
    }

    [RelayCommand(CanExecute = nameof(PodeSalvar))]
    private void Salvar() => Gravar(rodar: false);

    [RelayCommand(CanExecute = nameof(PodeSalvar))]
    private void SalvarERodar() => Gravar(rodar: true);

    private void Gravar(bool rodar)
    {
        if (Destino is not { } d) return;
        var titulo = Titulo.Trim().Length > 0 ? Titulo.Trim() : PrimeiraLinha(Contexto);
        try
        {
            var file = _inbox.Salvar(d, Tipo, Prioridade, titulo, (int)Math.Max(1, Quantidade), Contexto);
            var rel = Path.Combine("inbox", Path.GetFileName(file));
            if (rodar)
            {
                _launcher.OpenClaudeWithPrompt(d.Caminho,
                    $"Leia {rel} e execute o que ele pede, seguindo o CLAUDE.md e o STATUS.md desta pasta. " +
                    "Pare antes de publicar, enviar ou gastar e me mostre o que vai fazer.");
                Ok($"salvo em {file} — terminal aberto em {d.Nome} com claude lendo o arquivo");
            }
            else Ok($"salvo em {file}");

            Contexto = "";
            Titulo = "";
            Quantidade = 1;
            RefreshRecentes();
        }
        catch (Exception ex)
        {
            Aviso($"nao consegui gravar ({ex.Message}) — confira se a pasta do destino existe e se voce tem permissao");
        }
    }

    public void Rodar(InboxItem item) =>
        _launcher.OpenClaudeWithPrompt(item.Pasta,
            $"Leia {Path.Combine("inbox", item.NomeArquivo)} e execute o que ele pede, seguindo o CLAUDE.md e o STATUS.md desta pasta. " +
            "Pare antes de publicar, enviar ou gastar e me mostre o que vai fazer.");

    public void Abrir(InboxItem item) => _launcher.OpenFile(item.Arquivo);
    public void Pasta(InboxItem item) => _launcher.OpenExplorer(Path.Combine(item.Pasta, "inbox"));

    [RelayCommand]
    private void EditarDestinos() => _launcher.OpenFile(_inbox.DestinosFile);

    private static string PrimeiraLinha(string s)
    {
        var l = s.Trim().Split('\n')[0].Trim();
        return l.Length > 70 ? l[..70] : l.Length == 0 ? "entrada" : l;
    }
}
