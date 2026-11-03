namespace Factory.Services;

/// <summary>
/// Every Factory path comes from here; no other file hardcodes a location.
/// Projects root: FACTORY_DEMO=1 forces demo mode; else FACTORY_ROOT; else %USERPROFILE%\Projects if it exists;
/// else demo mode with the fictional projects in Assets\demo.
/// User data (decisions, Odin notes, alerts): FACTORY_DATA, else &lt;root&gt;\.factory (inside the projects root on
/// purpose, so a backup of the projects also takes the data). In demo mode: %LOCALAPPDATA%\Factory\demo-data.
/// Regenerable cache: %LOCALAPPDATA%\Factory.
/// </summary>
public static class AppPaths
{
    // order matters: static initializers run in file order (Dados uses Cache)
    public static string Cache { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Factory");

    private static readonly string Projetos =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Projects");

    /// <summary>FACTORY_DEMO=1, or no FACTORY_ROOT and no projects folder: opens with the fictional projects of Assets\demo.</summary>
    public static bool ModoDemo { get; } =
        Environment.GetEnvironmentVariable("FACTORY_DEMO") == "1"
        || (Environment.GetEnvironmentVariable("FACTORY_ROOT") is not { Length: > 0 } && !Directory.Exists(Projetos));

    public static string Root { get; } =
        ModoDemo ? Path.Combine(AppContext.BaseDirectory, "Assets", "demo")
        : Environment.GetEnvironmentVariable("FACTORY_ROOT") is { Length: > 0 } r ? r
        : Projetos;

    /// <summary>In demo mode data goes to the cache, never inside the app folder.</summary>
    public static string Dados { get; } =
        ModoDemo ? Path.Combine(Cache, "demo-data")
        : Environment.GetEnvironmentVariable("FACTORY_DATA") is { Length: > 0 } d ? d
        : Path.Combine(Root, ".factory");

    /// <summary>Dado do cockpit (decisoes, allowlist, alertas, favoritos): &lt;dados&gt;\factory.</summary>
    public static string Factory => Path.Combine(Dados, "factory");

    /// <summary>Notas, tarefas, falhas e recibos das maos do Odin: &lt;dados&gt;\odin.</summary>
    public static string Odin => Path.Combine(Dados, "odin");

    /// <summary>Wiki pessoal opcional (vida.json, notas): FACTORY_WIKI, senao &lt;raiz&gt;\wiki.</summary>
    public static string Wiki { get; } =
        Environment.GetEnvironmentVariable("FACTORY_WIKI") is { Length: > 0 } w ? w : Path.Combine(Root, "wiki");

    /// <summary>Checkout do proprio Factory (docs, STATUS, tools): FACTORY_REPO, senao subindo a partir do exe ate achar o .sln/README.</summary>
    public static string Repo { get; } = AcharRepo();

    /// <summary>Como o Odin chama o usuario (FACTORY_NAME; FACTORY_NOME ainda vale). Vazio = sem nome, frase neutra.</summary>
    public static string NomeUsuario { get; } = Environment.GetEnvironmentVariable("FACTORY_NAME") ?? Environment.GetEnvironmentVariable("FACTORY_NOME") ?? "";

    /// <summary>Trecho do nome da voz ElevenLabs preferida no ESTUDIO (FACTORY_VOICE; FACTORY_VOZ ainda vale). Vazio = primeira clonada.</summary>
    public static string VozPreferida { get; } = Environment.GetEnvironmentVariable("FACTORY_VOICE") ?? Environment.GetEnvironmentVariable("FACTORY_VOZ") ?? "";

    private static string AcharRepo()
    {
        if (Environment.GetEnvironmentVariable("FACTORY_REPO") is { Length: > 0 } e) return e;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "profile.manifest.json"))) return dir.FullName;
            dir = dir.Parent;
        }
        return Path.Combine(Root, "factory");
    }
}
