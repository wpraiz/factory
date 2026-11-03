using System.Text.Json.Serialization;

namespace Factory.Models;

/// <summary>
/// Alerta do VIGIA (monitoramento continuo da seguranca). User data: &lt;data&gt;\factory\seguranca-alertas.json.
/// Severidade: 1 INFO (so historico) · 2 ATENCAO (acende o MASTER CAUTION e a bolinha da secao) · 3 CRITICO (alem disso:
/// toast, pisca a barra de tarefas e bipe). Alerta repetido nao vira linha nova: sobe Contagem (padrao de repeticao).
/// </summary>
public sealed class SecAlert
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    [JsonPropertyName("chave")] public string Chave { get; set; } = "";
    [JsonPropertyName("fonte")] public string Fonte { get; set; } = "";
    [JsonPropertyName("sev")] public int Sev { get; set; } = 2;
    [JsonPropertyName("titulo")] public string Titulo { get; set; } = "";
    [JsonPropertyName("detalhe")] public string Detalhe { get; set; } = "";
    [JsonPropertyName("quando")] public DateTimeOffset Quando { get; set; } = DateTimeOffset.Now;
    [JsonPropertyName("ultima")] public DateTimeOffset Ultima { get; set; } = DateTimeOffset.Now;
    [JsonPropertyName("contagem")] public int Contagem { get; set; } = 1;
    [JsonPropertyName("visto")] public bool Visto { get; set; }

    [JsonIgnore] public string SevTexto => Sev >= 3 ? "CRÍTICO" : Sev == 2 ? "ATENÇÃO" : "INFO";
    [JsonIgnore] public string Accent => Sev >= 3 ? "magenta" : Sev == 2 ? "amber" : "dim";
    [JsonIgnore] public bool PodeVisto => !Visto;
    /// <summary>Filtro de ACHADOS que corresponde a esta origem (alerta de rede/inicia/defender/postura tem linha la).</summary>
    [JsonIgnore] public string FiltroAchados => Fonte switch { "rede" => "REDE", "inicia" => "INICIA", "defender" => "DEFENDER", "postura" => "POSTURA", _ => "" };
    [JsonIgnore] public bool TemAchado => FiltroAchados.Length > 0;
    [JsonIgnore] public string QuandoTexto =>
        Contagem > 1 ? $"{Quando.LocalDateTime:dd/MM HH:mm} · repetiu {Contagem}x · última {Ultima.LocalDateTime:HH:mm}" : Quando.LocalDateTime.ToString("dd/MM HH:mm");
    [JsonIgnore] public double Opacidade => Visto ? 0.55 : 1;
}
