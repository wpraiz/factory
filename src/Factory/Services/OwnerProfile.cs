namespace Factory.Services;

/// <summary>
/// The single access point to the user's personal data. The repo is the generic product; memory, conversations and
/// the personal prompt live in the PROFILE folder, outside git (profile.manifest.json describes the rules; the root
/// comes from FACTORY_PROFILE (or FACTORY_PERFIL), else &lt;data&gt;\profile).
/// Without a profile Factory runs generic. While the profile folder does not exist, chat history is read from the
/// cache location (%LOCALAPPDATA%\Factory\chat).
/// </summary>
public static class OwnerProfile
{
    public static string Raiz { get; } = (Environment.GetEnvironmentVariable("FACTORY_PROFILE") ?? Environment.GetEnvironmentVariable("FACTORY_PERFIL")) is { Length: > 0 } e
        ? e : Path.Combine(AppPaths.Dados, "profile");

    /// <summary>Profile present on this machine (without it: generic Factory, no personal data).</summary>
    public static bool Existe => Directory.Exists(Raiz);

    private static readonly string Legado = Path.Combine(
        AppPaths.Cache, "chat");

    /// <summary>Pasta das conversas e da memoria do Odin (slot "chat"/"memoria"): perfil\chat quando ja migrado.</summary>
    public static string ChatDir
    {
        get
        {
            var novo = Path.Combine(Raiz, "chat");
            if (Directory.Exists(novo)) return novo;
            Directory.CreateDirectory(Legado);
            return Legado;
        }
    }

    /// <summary>Arquivo dentro de um slot do perfil (ex.: Arquivo("prompt", "pessoal.md")). Nao cria nada.</summary>
    public static string Arquivo(string slot, string nome) => Path.Combine(Raiz, slot, nome);

    /// <summary>Le um arquivo do perfil; vazio se nao existe (o produto segue generico).</summary>
    public static string Ler(string slot, string nome)
    {
        try { var f = Arquivo(slot, nome); return File.Exists(f) ? File.ReadAllText(f) : ""; }
        catch (Exception) { return ""; }
    }
}
