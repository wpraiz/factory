using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Factory.Models;

/// <summary>Uma faixa da Wiki Vida (Carreira, Formacao...): emoji, cor e os documentos dela, na ordem do JSON.</summary>
public sealed class LifeCategory
{
    [JsonPropertyName("chave")] public string Key { get; set; } = "";
    [JsonPropertyName("nome")] public string Name { get; set; } = "";
    [JsonPropertyName("emoji")] public string Emoji { get; set; } = "";
    /// <summary>cyan | magenta | amber | violet (verde e do selo ATUAL, nao de faixa).</summary>
    [JsonPropertyName("cor")] public string Accent { get; set; } = "cyan";
    [JsonPropertyName("descricao")] public string Description { get; set; } = "";

    [JsonIgnore] public ObservableCollection<LifeDoc> Docs { get; } = [];

    [JsonIgnore] public string NameUpper => Name.ToUpperInvariant();
    [JsonIgnore] public bool IsEmpty => Docs.Count == 0;
    [JsonIgnore] public bool HasDocs => Docs.Count > 0;

    [JsonIgnore]
    public string CountText => Docs.Count switch
    {
        0 => "vazia",
        1 => "1 documento",
        var n => $"{n} documentos",
    };

    [JsonIgnore] public string EmptyText => $"nada aqui ainda — adicione em vida.json com \"categoria\": \"{Key}\"";
}

/// <summary>Raiz do vida.json.</summary>
public sealed class VidaManifest
{
    [JsonPropertyName("versao")] public int Version { get; set; } = 1;
    [JsonPropertyName("nota")] public string Note { get; set; } = "";
    [JsonPropertyName("categorias")] public List<LifeCategory> Categories { get; set; } = [];
    [JsonPropertyName("documentos")] public List<LifeDoc> Docs { get; set; } = [];
}
