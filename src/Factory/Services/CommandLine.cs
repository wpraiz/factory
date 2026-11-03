using System.Globalization;
using System.Text;
using Factory.Models;
using Factory.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Factory.Services;

/// <summary>Uma sugestao da linha de comando: o que vai acontecer se der Enter.</summary>
public sealed record CommandHit(string Label, string Detail, string Glyph, Action Run)
{
    /// <summary>Texto curto pro historico do chat depois de executar.</summary>
    public string Echo => Label;
}

public interface ICommandLine
{
    /// <summary>
    /// Sugestoes pro texto digitado, melhor primeiro. Vazio = nao e comando (vai pro chat).
    /// Nunca executa nada: so monta as acoes.
    /// </summary>
    IReadOnlyList<CommandHit> Suggest(string text);

    /// <summary>Uma linha de ajuda pro cabecalho.</summary>
    string Help { get; }
}

/// <summary>
/// Verbos em portugues, sem IA, resolvidos na hora: `ir atlas` abre o Windows Terminal na
/// pasta com `claude --resume` da sessao mais recente daquele projeto. O projeto e achado por
/// token (qualquer palavra da frase que pareca um nome de projeto), entao enchimento e acento
/// nao atrapalham. Sem verbo, o padrao e VER (nao abre nada); IR vem como segunda opcao.
/// Texto sem verbo e sem projeto parecido nao vira comando: o chat manda pro Claude.
/// </summary>
public sealed class CommandLine : ICommandLine
{
    private readonly ProjectCatalog _catalog;
    private readonly ISessionIndex _index;
    private readonly ILauncher _launcher;
    private readonly NavigationService _nav;

    // Glyphs Segoe Fluent: E756 terminal · E8B7 pasta · E890 ver · E8A5 documento · E8C8 copiar
    private const string GlyphTerminal = "", GlyphFolder = "", GlyphView = "", GlyphDoc = "", GlyphCopy = "";

    private static readonly HashSet<string> Filler = new(StringComparer.Ordinal)
    {
        "para", "pra", "pro", "pras", "pros", "o", "a", "os", "as", "um", "uma", "no", "na", "nos", "nas",
        "do", "da", "dos", "das", "de", "em", "ao", "projeto", "pasta", "sessao", "session", "me", "la", "ai",
        "parei", "paramos", "estou", "estava", "to", "ta", "eu",
    };

    private enum Verb { Go, Fresh, Terminal, Folder, Show, Status, Copy }

    private static readonly (Verb Verb, string[] Words)[] Verbs =
    [
        (Verb.Go,       ["ir", "va", "vai", "abrir", "abre", "retomar", "retoma", "continuar", "continua", "claude", "voltar", "volta", "entrar", "entra"]),
        (Verb.Fresh,    ["novo", "nova", "zerar", "zero", "limpo"]),
        (Verb.Terminal, ["terminal", "term", "shell", "pwsh", "powershell", "cmd"]),
        (Verb.Folder,   ["explorer", "pasta", "folder", "abrirpasta"]),
        (Verb.Show,     ["ver", "mostrar", "mostra", "card", "olhar", "olha", "selecionar", "seleciona"]),
        (Verb.Status,   ["status", "onde", "parei"]),
        (Verb.Copy,     ["copiar", "copia", "caminho", "path"]),
    ];

    public CommandLine(ProjectCatalog catalog, ISessionIndex index, ILauncher launcher, NavigationService nav)
    {
        _catalog = catalog;
        _index = index;
        _launcher = launcher;
        _nav = nav;
    }

    public string Help => "clique no orb ou Ctrl+Alt+O · na caixa: ir <projeto> · ou pergunte";

    public IReadOnlyList<CommandHit> Suggest(string text)
    {
        var tokens = Tokenize(text);
        if (tokens.Count == 0) return [];

        // 1) verbo na frente?
        Verb? verb = null;
        var rest = tokens;
        foreach (var (v, words) in Verbs)
            if (words.Contains(tokens[0])) { verb = v; rest = tokens.Skip(1).ToList(); break; }
        // "pasta" tambem e enchimento ("ir pra pasta do atlas"); como verbo so vale sozinho na frente.
        rest = rest.Where(t => !Filler.Contains(t)).ToList();

        // Sem verbo, so frase curta (ate 3 palavras) pode ser comando. Frase longa e papo:
        // "o que voce acha da ideia..." nunca pode virar VER <projeto> (it happened).
        if (verb is null && tokens.Count > 3) return [];

        var hits = new List<CommandHit>();

        // 2) secao pelo nome (sem verbo ou com "ir"): "grana", "ir pro radar"
        if (verb is null or Verb.Go or Verb.Show)
            foreach (var s in Navigation.SectionRegistry.Sections)
                if (rest.Count == 1 && Fold(s.Title) == rest[0])
                    hits.Add(new CommandHit(s.Title.ToUpperInvariant(), "ir pra secao", s.Glyph, () => _nav.GoToSection(s.Tag)));

        // 3) projeto por nome (fuzzy), ate 3 candidatos. Match por token: "ir atlas agora mesmo"
        //    acha "atlas" sem precisar listar todo enchimento possivel.
        if (rest.Count > 0)
        {
            // So match forte (prefixo/pedaco do nome, palavra com 3+ letras); subsequencia fuzzy
            // batia em qualquer coisa ("seil" achava estagIA).
            var joined = string.Join(" ", rest);
            var projects = _catalog.All
                .Where(p =>
                {
                    var name = Fold(p.Name);
                    return name.Contains(joined) || rest.Any(t => t.Length >= 3 && (name.Contains(t) || t.Contains(name)));
                })
                .Select(p => (p, score: Math.Max(PaletteMatcher.Score(joined, p.Name, ""), rest.Max(t => PaletteMatcher.Score(t, p.Name, "")))))
                .OrderByDescending(x => x.score).ThenByDescending(x => x.p.LastTouch)
                .Take(3).Select(x => x.p).ToList();
            foreach (var p in projects) hits.AddRange(ForProject(verb ?? Verb.Show, p, alsoGo: verb is null));
        }
        else if (verb is not null)
        {
            // so o verbo: oferece os 3 projetos mais recentes
            foreach (var p in _catalog.All.OrderByDescending(p => p.LastTouch).Take(3))
                hits.AddRange(ForProject(verb.Value, p, alsoGo: false));
        }

        return hits;
    }

    private IEnumerable<CommandHit> ForProject(Verb verb, ProjectInfo p, bool alsoGo)
    {
        var path = p.Path;
        switch (verb)
        {
            case Verb.Go:
            {
                var last = _index.ForProject(path).FirstOrDefault();
                if (last is not null)
                {
                    var id = last.Id;
                    yield return new CommandHit($"IR  {p.Name}",
                        $"terminal na pasta + claude --resume · {last.Title} · {ProjectInfo.Humanize(last.LastWrite)}",
                        GlyphTerminal, () => { Select(path); _launcher.OpenClaude(path, id); });
                }
                else
                    yield return new CommandHit($"IR  {p.Name}", "sem sessao anterior nesta pasta: terminal + claude limpo",
                        GlyphTerminal, () => { Select(path); _launcher.OpenClaude(path); });
                break;
            }
            case Verb.Fresh:
                yield return new CommandHit($"NOVO  {p.Name}", "terminal na pasta + claude sem resume", GlyphTerminal,
                    () => { Select(path); _launcher.OpenClaude(path); });
                break;
            case Verb.Terminal:
                yield return new CommandHit($"TERMINAL  {p.Name}", path, GlyphTerminal, () => _launcher.OpenTerminal(path));
                break;
            case Verb.Folder:
                yield return new CommandHit($"PASTA  {p.Name}", path, GlyphFolder, () => _launcher.OpenExplorer(path));
                break;
            case Verb.Show:
                yield return new CommandHit($"VER  {p.Name}", p.Badges, GlyphView, () => _nav.GoToProject(path));
                if (alsoGo)
                    foreach (var h in ForProject(Verb.Go, p, alsoGo: false)) yield return h;
                break;
            case Verb.Status:
            {
                var status = Path.Combine(path, "STATUS.md");
                yield return File.Exists(status)
                    ? new CommandHit($"STATUS  {p.Name}", $"abrir STATUS.md · {p.StatusUpdated}", GlyphDoc, () => _launcher.OpenFile(status))
                    : new CommandHit($"STATUS  {p.Name}", "nao tem STATUS.md: mostrar o card", GlyphView, () => _nav.GoToProject(path));
                break;
            }
            case Verb.Copy:
                yield return new CommandHit($"COPIAR  {p.Name}", path, GlyphCopy, () => _launcher.CopyToClipboard(path));
                break;
        }
    }

    /// <summary>Deixa o projeto selecionado em PROJETOS sem trocar de secao (o terminal ja rouba a tela).</summary>
    private static void Select(string path) =>
        App.Services.GetRequiredService<ProjectsViewModel>().SelectByPath(path);

    // ---------- texto ----------

    private static List<string> Tokenize(string text) =>
        text.Split([' ', '\t', ',', ';', '"', '\''], StringSplitOptions.RemoveEmptyEntries)
            .Select(Fold).Where(t => t.Length > 0).ToList();

    /// <summary>minusculas sem acento: "Ír pró" → "ir pro".</summary>
    public static string Fold(string s)
    {
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString().Trim('?', '!', '.');
    }
}
