using System.Diagnostics;
using System.Text.Json;
using Factory.Models;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Factory.Services;

public interface IVidaLibrary
{
    /// <summary><wiki>\vida.json — o app so LE; ele edita a mao.</summary>
    string ManifestFile { get; }
    string WikiDir { get; }
    /// <summary>Le o manifesto e resolve caminhos. Nunca lanca: <paramref name="error"/> diz o que fazer quando falta.</summary>
    VidaManifest Load(out string error);
    /// <summary>StorageFile do documento (pra arrastar e pra copiar como arquivo); null se nao existe.</summary>
    Task<StorageFile?> StorageFileAsync(LifeDoc doc);
    /// <summary>Copia o ARQUIVO pra area de transferencia (cola como anexo no Gmail/WhatsApp/Explorer).</summary>
    Task<bool> CopyFileAsync(LifeDoc doc);
    /// <summary>Explorer com o arquivo selecionado (nao so a pasta).</summary>
    void Reveal(LifeDoc doc);
}

public sealed class LifeLibrary : IVidaLibrary
{
    private static readonly JsonSerializerOptions Json = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Ordem e nomes padrao quando o JSON nao declara categorias (ou declara so parte).</summary>
    private static readonly LifeCategory[] DefaultCategories =
    [
        new() { Key = "carreira",  Name = "Carreira",  Emoji = "\U0001F4BC", Accent = "cyan",    Description = "CVs e portfolio" },
        new() { Key = "formacao",  Name = "Formação",  Emoji = "\U0001F393", Accent = "violet",  Description = "diplomas e certificacoes" },
        new() { Key = "contratos", Name = "Contratos", Emoji = "\U0001F4C4", Accent = "amber",   Description = "contratos e documentos" },
        new() { Key = "impacto",   Name = "Impacto",   Emoji = "\U0001F9EC", Accent = "magenta", Description = "projetos de impacto" },
        new() { Key = "pessoal",   Name = "Pessoal",   Emoji = "\U0001F464", Accent = "cyan",    Description = "documentos pessoais" },
    ];

    public string WikiDir { get; } = AppPaths.Wiki;
    public string ManifestFile => Path.Combine(WikiDir, "vida.json");

    public VidaManifest Load(out string error)
    {
        error = "";
        VidaManifest m;
        if (!File.Exists(ManifestFile))
        {
            error = $"vida.json nao existe em {WikiDir} — crie com o modelo de docs\\wiki-vida-spec.md e aperte RECARREGAR";
            m = new VidaManifest();
        }
        else
        {
            try { m = JsonSerializer.Deserialize<VidaManifest>(File.ReadAllText(ManifestFile), Json) ?? new VidaManifest(); }
            catch (Exception ex)
            {
                error = $"vida.json invalido ({ex.Message.Split('\n')[0].Trim()}) — corrija e aperte RECARREGAR";
                m = new VidaManifest();
            }
        }

        // categorias: as do JSON na ordem do arquivo; as padrao que faltarem entram depois
        var cats = m.Categories.Where(c => c.Key.Length > 0).ToList();
        foreach (var d in DefaultCategories)
            if (!cats.Any(c => c.Key.Equals(d.Key, StringComparison.OrdinalIgnoreCase))) cats.Add(d);
        m.Categories = cats;

        foreach (var d in m.Docs)
        {
            d.FullPath = Resolve(d.Path);
            d.Exists = d.FullPath.Length > 0 && File.Exists(d.FullPath);
            if (string.IsNullOrWhiteSpace(d.Title)) d.Title = Path.GetFileNameWithoutExtension(d.FullPath);
            var cat = cats.FirstOrDefault(c => c.Key.Equals(d.Category, StringComparison.OrdinalIgnoreCase));
            if (cat is null)
            {
                // categoria desconhecida no JSON: vira faixa propria, nao some
                cat = new LifeCategory { Key = d.Category.Length > 0 ? d.Category : "outros", Name = d.Category.Length > 0 ? d.Category : "Outros", Emoji = "\U0001F5C2", Accent = "cyan" };
                if (d.Category.Length == 0) d.Category = cat.Key;
                cats.Add(cat);
            }
            d.Accent = cat.Accent;
        }
        // dentro da faixa: ATUAL primeiro (pt-BR antes de en), depois o resto na ordem do JSON — o CV certo e o primeiro tile
        foreach (var cat in cats)
        {
            var ordered = m.Docs.Where(d => !d.Archived && ReferenceEquals(CatOf(d), cat))
                .Select((d, i) => (d, i))
                .OrderByDescending(x => x.d.Current)
                .ThenBy(x => x.d.Current ? LangRank(x.d.Lang) : 0)
                .ThenBy(x => x.i)
                .Select(x => x.d);
            foreach (var d in ordered) cat.Docs.Add(d);
        }
        return m;

        LifeCategory? CatOf(LifeDoc d) => cats.FirstOrDefault(c => c.Key.Equals(d.Category, StringComparison.OrdinalIgnoreCase));
        static int LangRank(string lang) => lang.ToLowerInvariant() switch { "pt-br" or "pt" => 0, "en" or "en-us" => 1, _ => 2 };
    }

    private string Resolve(string p)
    {
        if (string.IsNullOrWhiteSpace(p)) return "";
        p = Environment.ExpandEnvironmentVariables(p.Trim());
        return Path.GetFullPath(Path.IsPathRooted(p) ? p : Path.Combine(WikiDir, p));
    }

    public async Task<StorageFile?> StorageFileAsync(LifeDoc doc)
    {
        if (!doc.Exists) return null;
        try { return await StorageFile.GetFileFromPathAsync(doc.FullPath); }
        catch (Exception) { return null; }
    }

    public async Task<bool> CopyFileAsync(LifeDoc doc)
    {
        var file = await StorageFileAsync(doc);
        if (file is null) return false;
        var dp = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        dp.SetStorageItems([file], readOnly: true);
        // o caminho vai junto como texto: colar num campo de texto da o caminho, colar no Explorer/Gmail da o arquivo
        dp.SetText(doc.FullPath);
        Clipboard.SetContent(dp);
        Clipboard.Flush(); // fica na area de transferencia mesmo depois de fechar o app
        return true;
    }

    public void Reveal(LifeDoc doc)
    {
        var target = doc.Exists ? $"/select,\"{doc.FullPath}\"" : $"\"{(Directory.Exists(doc.Folder) ? doc.Folder : WikiDir)}\"";
        Process.Start(new ProcessStartInfo("explorer.exe", target) { UseShellExecute = true });
    }
}
