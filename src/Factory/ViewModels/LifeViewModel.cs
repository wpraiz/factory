using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Factory.Models;
using Factory.Services;
using Windows.Storage;

namespace Factory.ViewModels;

/// <summary>
/// VIDA — the user's life wiki: os documentos da vida (CV, portfolio, certificados, contratos) como tiles
/// grandes por categoria. Um clique abre; arrastar leva o arquivo pra fora (Gmail, WhatsApp, formulario);
/// botao direito / hover copia o arquivo, o caminho ou abre a pasta. Fonte: <wiki>\vida.json, so leitura.
/// </summary>
public sealed partial class LifeViewModel : ObservableObject
{
    private readonly IVidaLibrary _lib;
    private readonly ILauncher _launcher;

    public ObservableCollection<LifeCategory> Categories { get; } = [];
    /// <summary>Versoes antigas (arquivo: true no JSON), recolhidas atras do toggle ARQUIVO.</summary>
    public ObservableCollection<LifeDoc> Archived { get; } = [];

    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _headline = "";
    /// <summary>Erro de manifesto (faltando ou invalido): a tela mostra o que fazer.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ManifestOk))]
    private string _manifestError = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ArchiveLabel), nameof(ArchiveVisible))]
    private bool _showArchive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ArchiveLabel), nameof(ArchiveVisible), nameof(HasArchive))]
    private int _archivedCount;

    public bool ManifestOk => ManifestError.Length == 0;
    public bool HasArchive => ArchivedCount > 0;
    public bool ArchiveVisible => ShowArchive && ArchivedCount > 0;
    public string ArchiveLabel => ShowArchive ? "✓ OCULTAR ARQUIVO" : $"VER ARQUIVO ({ArchivedCount})";
    public string ManifestFile => _lib.ManifestFile;

    public LifeViewModel(IVidaLibrary lib, ILauncher launcher)
    {
        _lib = lib;
        _launcher = launcher;
        Reload();
    }

    // ---------- carregar ----------

    [RelayCommand]
    public void Reload()
    {
        var m = _lib.Load(out var error);
        ManifestError = error;
        Categories.Clear();
        foreach (var c in m.Categories) Categories.Add(c);
        Archived.Clear();
        foreach (var d in m.Docs.Where(d => d.Archived)) Archived.Add(d);
        ArchivedCount = Archived.Count;

        var total = m.Docs.Count;
        var current = m.Docs.Count(d => d.Current && !d.Archived);
        var missing = m.Docs.Count(d => !d.Exists);
        Headline = total == 0
            ? "seus documentos, num lugar so: CV, portfolio, certificados, contratos"
            : $"{total} documentos · {current} atuais · {Categories.Count(c => c.HasDocs)} categorias com conteudo";
        Status = error.Length > 0 ? error
            : missing > 0 ? $"{missing} arquivo(s) do vida.json nao existem no disco — tile marcado; confira o caminho"
            : "clique abre · arraste o tile pro Gmail/WhatsApp pra anexar · botao direito: copiar arquivo, caminho, pasta";
    }

    // ---------- acoes por documento ----------

    [RelayCommand]
    private void Open(LifeDoc? d)
    {
        if (d is null) return;
        if (!d.Exists) { Status = $"{d.Title}: arquivo nao existe ({d.FullPath}) — corrija o caminho em vida.json"; return; }
        try { _launcher.OpenFile(d.FullPath); Status = $"abrindo {d.FileName}…"; }
        catch (Exception ex) { Status = $"{d.Title}: nao abriu ({ex.Message})"; }
    }

    [RelayCommand]
    private async Task CopyFileAsync(LifeDoc? d)
    {
        if (d is null) return;
        try
        {
            var ok = await _lib.CopyFileAsync(d);
            Status = ok
                ? $"arquivo copiado: {d.FileName} — Ctrl+V no Gmail, WhatsApp ou Explorer anexa"
                : $"{d.Title}: nao consegui copiar (arquivo nao existe em {d.FullPath})";
            if (ok) _ = FlashAsync(d, "✓ ARQUIVO COPIADO");
        }
        catch (Exception ex) { Status = $"copiar arquivo: {ex.Message}"; }
    }

    public void CopyPath(LifeDoc d)
    {
        _launcher.CopyToClipboard(d.FullPath);
        Status = $"caminho copiado: {d.FullPath}";
        _ = FlashAsync(d, "✓ CAMINHO COPIADO");
    }

    /// <summary>Confirmacao no proprio tile (o Status do cabecalho fica longe quando a pagina rola).</summary>
    private static async Task FlashAsync(LifeDoc d, string text)
    {
        d.Flash = text;
        await Task.Delay(1500);
        if (d.Flash == text) d.Flash = "";
    }

    public void OpenFolder(LifeDoc d)
    {
        try { _lib.Reveal(d); Status = $"Explorer em {d.Folder}"; }
        catch (Exception ex) { Status = $"nao abriu a pasta: {ex.Message}"; }
    }

    /// <summary>Pra o DragStarting do tile: o StorageFile que vai no DataPackage.</summary>
    public Task<StorageFile?> StorageFileAsync(LifeDoc d) => _lib.StorageFileAsync(d);

    public void DragStarted(LifeDoc d, bool ok) =>
        Status = ok ? $"arrastando {d.FileName} — solte no Gmail, WhatsApp ou no campo de anexo" : $"{d.Title}: nao da pra arrastar, o arquivo nao existe";

    // ---------- secao ----------

    [RelayCommand]
    private void ToggleArchive() => ShowArchive = !ShowArchive;

    [RelayCommand]
    private void OpenManifest()
    {
        try
        {
            if (File.Exists(ManifestFile)) _launcher.OpenFile(ManifestFile);
            else _launcher.OpenExplorer(_lib.WikiDir);
            Status = File.Exists(ManifestFile) ? $"abrindo {ManifestFile} — salve e aperte RECARREGAR" : $"Explorer em {_lib.WikiDir} — crie o vida.json ali";
        }
        catch (Exception ex) { Status = $"nao abriu o vida.json: {ex.Message}"; }
    }
}
