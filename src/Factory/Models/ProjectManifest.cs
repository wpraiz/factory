using System.Text.Json.Serialization;

namespace Factory.Models;

/// <summary>
/// Manifesto opcional <c>factory.json</c> na raiz de cada projeto. E o que o
/// usuario preenche (pelo painel) pra dizer o que o projeto E e em que pe esta —
/// o scanner nao inventa nada disso.
/// </summary>
public sealed class ProjectManifest
{
    public const string FileName = "factory.json";

    public static readonly string[] Kinds = ["produto", "cliente", "interno", "lab", "conteudo"];
    public static readonly string[] Stages = ["ideia", "construindo", "no-ar", "vendendo", "pausado", "morto"];
    public static readonly string[] Models = ["adsense", "infoproduto", "saas", "servico", "afiliado", "aposta", "salario", "nenhum"];

    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("stage")] public string Stage { get; set; } = "";
    [JsonPropertyName("money")] public string Money { get; set; } = "";
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("notes")] public string Notes { get; set; } = "";
    [JsonPropertyName("tags")] public List<string> Tags { get; set; } = [];
    [JsonPropertyName("pinned")] public bool Pinned { get; set; }

    /// <summary>1–5; 0 = nao avaliado. Se der certo, quanto pode render?</summary>
    [JsonPropertyName("potential")] public int Potential { get; set; }
    /// <summary>1–5; 0 = nao avaliado. Quanto falta de trabalho ate render?</summary>
    [JsonPropertyName("effort")] public int Effort { get; set; }
    /// <summary>1–5; 0 = nao avaliado. Quanto disso e chute? 5 = ja vende / cliente ja pagou.</summary>
    [JsonPropertyName("confidence")] public int Confidence { get; set; }
    /// <summary>1–5; 0 = nao avaliado. Quantas pessoas alcanca sem trafego pago? cliente unico = 1, SEO = 5.</summary>
    [JsonPropertyName("reach")] public int Reach { get; set; }
    /// <summary>Dias ate o primeiro real se comecar hoje; 0 = nao sei.</summary>
    [JsonPropertyName("first_money_days")] public int FirstMoneyDays { get; set; }
    /// <summary>Receita mensal declarada, em R$. 0 = nada ainda.</summary>
    [JsonPropertyName("revenue_monthly")] public double RevenueMonthly { get; set; }
    /// <summary>Meta realista em 90 dias, R$/mes.</summary>
    [JsonPropertyName("revenue_target")] public double RevenueTarget { get; set; }
    [JsonPropertyName("model")] public string Model { get; set; } = "";

    [JsonIgnore]
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Kind) && string.IsNullOrWhiteSpace(Stage) &&
        string.IsNullOrWhiteSpace(Money) && string.IsNullOrWhiteSpace(Url) &&
        string.IsNullOrWhiteSpace(Notes) && Tags.Count == 0 && !Pinned &&
        Potential == 0 && Effort == 0 && Confidence == 0 && Reach == 0 && FirstMoneyDays == 0 &&
        RevenueMonthly == 0 && RevenueTarget == 0 && string.IsNullOrWhiteSpace(Model);

    [JsonIgnore] public bool IsRated => Potential > 0 && Effort > 0;

    /// <summary>Confianca efetiva: manifesto antigo (sem o campo) conta como 3, pra nao zerar o score.</summary>
    [JsonIgnore] public int ConfidenceOr3 => Confidence > 0 ? Confidence : 3;

    /// <summary>ICE: potencial × confianca ÷ esforco. So faz sentido com potencial e esforco avaliados.</summary>
    [JsonIgnore] public double Ice => IsRated ? Potential * ConfidenceOr3 / (double)Effort : 0;

    /// <summary>RICE: alcance × ICE. Alcance 0 conta como 1 (cliente unico).</summary>
    [JsonIgnore] public double Rice => IsRated ? Math.Max(1, Reach) * Ice : 0;

    /// <summary>Mantido por compatibilidade: o score padrao e o ICE.</summary>
    [JsonIgnore] public double Score => Ice;

    [JsonIgnore] public bool FirstMoneySoon => FirstMoneyDays is >= 1 and <= 14;

    public static string Stars(int n) => n <= 0 ? "—" : new string('★', Math.Clamp(n, 0, 5)) + new string('☆', 5 - Math.Clamp(n, 0, 5));

    public ProjectManifest Clone() => new()
    {
        Kind = Kind, Stage = Stage, Money = Money, Url = Url, Notes = Notes, Tags = [.. Tags], Pinned = Pinned,
        Potential = Potential, Effort = Effort, Confidence = Confidence, Reach = Reach,
        FirstMoneyDays = FirstMoneyDays, RevenueMonthly = RevenueMonthly, RevenueTarget = RevenueTarget, Model = Model,
    };
}
