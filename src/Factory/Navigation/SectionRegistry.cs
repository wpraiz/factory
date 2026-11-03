using Factory.Views;

namespace Factory.Navigation;

public sealed record SectionDescriptor(string Tag, string Title, string Glyph, Type PageType);

/// <summary>
/// Secoes do NavigationView. Para ampliar o app: criar a pagina/ViewModel e
/// acrescentar uma linha aqui — a sidebar e montada a partir desta lista.
/// Glyphs: Segoe Fluent Icons (escapados pra nao depender de encoding).
/// </summary>
public static class SectionRegistry
{
    /// <summary>
    /// Grupos da sidebar, na ordem em que aparecem (design direction B: lista plana de 12
    /// itens virou 4 grupos com cabecalho). Secao sem grupo aqui cai no ultimo.
    /// </summary>
    public static readonly IReadOnlyList<(string Titulo, string[] Tags)> Groups =
    [
        // APPS logo depois do ODIN (ui-guide: "Primeira tela = ODIN... APPS logo depois")
        ("COMANDO", ["chat", "apps", "entrada", "plano", "search"]),
        ("PROJETOS", ["projects", "frota", "setup", "contexts"]),
        ("NEGOCIO", ["money", "forge", "studio"]),
        ("PESSOAL", ["vida", "radar", "seguranca"]),
    ];

    /// <summary>Secoes na ordem dos grupos, com o titulo do grupo de cada uma.</summary>
    public static IEnumerable<(string Grupo, SectionDescriptor Secao)> Grouped()
    {
        var usados = new HashSet<string>();
        foreach (var (titulo, tags) in Groups)
            foreach (var tag in tags)
                if (Sections.FirstOrDefault(s => s.Tag == tag) is { } s && usados.Add(tag))
                    yield return (titulo, s);
        foreach (var s in Sections.Where(s => !usados.Contains(s.Tag)))
            yield return (Groups[^1].Titulo, s);
    }

    public static readonly IReadOnlyList<SectionDescriptor> Sections =
    [
        new("chat",     "Odin",      "", typeof(ChatPage)),  // Message — entrada: uma caixa, comando ou papo
        new("entrada",  "Entrada",   "", typeof(InboxPage)), // Add — colar contexto -> inbox do projeto (Ctrl+N)
        new("plano",    "Plano",     "", typeof(BoardPage)),     // BulletedList — ver o planejamento aberto e decidir BOM/RUIM/DEPOIS
        new("apps",     "Apps",      "", typeof(AppsPage)),  // AllApps — primeira tela: o que ele abre todo dia
        new("vida",     "Vida",      "", typeof(LifePage)),  // Contact — Wiki Vida: CV, portfolio, certificados; pegar em 1 clique
        new("radar",    "Radar",     "", typeof(RadarPage)), // Wifi — tem gente perto? (BLE ao vivo + Wi-Fi + rede)
        new("seguranca", "Segurança", "", typeof(SecurityPage)), // Shield — postura do Windows, Defender, portas, o que inicia sozinho
        new("projects", "Projetos",  "", typeof(ProjectsPage)),   // FolderOpen
        new("frota",    "Frota",     "", typeof(FleetPage)),      // People — visao cruzada: quem tem sessao Claude viva agora
        new("setup",    "Setup",     "\uE713", typeof(SetupPage)),     // Settings — skills/agentes/plugins/MCP/hooks do Claude Code + uso real
        new("search",   "Busca",     "", typeof(SearchPage)),     // Search
        new("contexts", "Contextos", "", typeof(ContextsPage)),   // Document
        new("money",    "Grana",     "", typeof(MoneyPage)),      // Tag
        new("forge",    "Fabrica",   "", typeof(ForgePage)),      // Repair
        new("studio",   "Estudio",   "", typeof(StudioPage)),     // Microphone
    ];
}
