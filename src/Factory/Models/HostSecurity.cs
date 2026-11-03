using System.Text.Json.Serialization;

namespace Factory.Models;

// ---------- JSON do coletor (Assets\security\collect-host-security.ps1, contrato host-security/v1) ----------

public sealed class HostSnapshot
{
    [JsonPropertyName("quando")] public DateTimeOffset Quando { get; set; }
    [JsonPropertyName("admin")] public bool Admin { get; set; }
    [JsonPropertyName("postura")] public HostPostura Postura { get; set; } = new();
    [JsonPropertyName("portas")] public List<HostPorta> Portas { get; set; } = [];
    [JsonPropertyName("persistencia")] public List<HostPersistencia> Persistencia { get; set; } = [];
    [JsonPropertyName("eventos")] public List<HostEvento> Eventos { get; set; } = [];
    [JsonPropertyName("hardening")] public HostHardening? Hardening { get; set; }
    [JsonPropertyName("hardeningKittyInstalado")] public bool HardeningKittyInstalado { get; set; }
    [JsonPropertyName("erros")] public Dictionary<string, string> Erros { get; set; } = [];
    /// <summary>Quanto o coletor levou (ms). Preenchido pelo servico, nao pelo script.</summary>
    [JsonIgnore] public long Ms { get; set; }
}

public sealed class HostPostura
{
    [JsonPropertyName("bitlocker")] public HostBitLocker? BitLocker { get; set; }
    [JsonPropertyName("hvci")] public bool? Hvci { get; set; }
    [JsonPropertyName("credentialGuard")] public bool? CredentialGuard { get; set; }
    [JsonPropertyName("secureBoot")] public bool? SecureBoot { get; set; }
    [JsonPropertyName("defenderLigado")] public bool? DefenderLigado { get; set; }
    [JsonPropertyName("assinaturas")] public DateTimeOffset? Assinaturas { get; set; }
    [JsonPropertyName("tamperProtection")] public bool? TamperProtection { get; set; }
    [JsonPropertyName("asrLeitura")] public bool AsrLeitura { get; set; }
    [JsonPropertyName("asr")] public List<HostAsr> Asr { get; set; } = [];
    [JsonPropertyName("firewall")] public List<HostFirewall> Firewall { get; set; } = [];
}

public sealed class HostBitLocker
{
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("fonte")] public string Fonte { get; set; } = "";
}

public sealed class HostAsr
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    /// <summary>0 desligada, 1 bloqueio, 2 auditoria, 6 avisar.</summary>
    [JsonPropertyName("acao")] public int Acao { get; set; }
}

public sealed class HostFirewall
{
    [JsonPropertyName("perfil")] public string FirewallProfile { get; set; } = "";
    [JsonPropertyName("ligado")] public bool Ligado { get; set; }
}

public sealed class HostPorta
{
    [JsonPropertyName("proto")] public string Proto { get; set; } = "";
    [JsonPropertyName("endereco")] public string Endereco { get; set; } = "";
    [JsonPropertyName("porta")] public int Porta { get; set; }
    [JsonPropertyName("pid")] public int Pid { get; set; }
    [JsonPropertyName("processo")] public string Processo { get; set; } = "";
    [JsonPropertyName("caminho")] public string Caminho { get; set; } = "";
    [JsonPropertyName("assinatura")] public string Assinatura { get; set; } = "";
    [JsonPropertyName("assinante")] public string Assinante { get; set; } = "";
}

public sealed class HostPersistencia
{
    /// <summary>tarefa | run | inicializar</summary>
    [JsonPropertyName("tipo")] public string Tipo { get; set; } = "";
    [JsonPropertyName("nome")] public string Nome { get; set; } = "";
    [JsonPropertyName("onde")] public string Onde { get; set; } = "";
    [JsonPropertyName("acao")] public string Acao { get; set; } = "";
    [JsonPropertyName("autor")] public string Autor { get; set; } = "";
    /// <summary>null = nao se aplica (pasta Inicializar).</summary>
    [JsonPropertyName("existe")] public bool? Existe { get; set; }
    /// <summary>Valid | NotSigned | HashMismatch | UnknownError… (vazio = dentro do Windows ou arquivo sumiu).</summary>
    [JsonPropertyName("assinatura")] public string Assinatura { get; set; } = "";
    [JsonPropertyName("assinante")] public string Assinante { get; set; } = "";
}

public sealed class HostEvento
{
    [JsonPropertyName("eventId")] public int EventId { get; set; }
    [JsonPropertyName("quando")] public DateTimeOffset Quando { get; set; }
    [JsonPropertyName("tipo")] public string Tipo { get; set; } = "";
    [JsonPropertyName("ameaca")] public string Ameaca { get; set; } = "";
    [JsonPropertyName("processo")] public string Processo { get; set; } = "";
    [JsonPropertyName("regra")] public string Regra { get; set; } = "";
    [JsonPropertyName("caminho")] public string Caminho { get; set; } = "";
}

public sealed class HostHardening
{
    [JsonPropertyName("arquivo")] public string Arquivo { get; set; } = "";
    [JsonPropertyName("quando")] public DateTimeOffset Quando { get; set; }
    [JsonPropertyName("score")] public double Score { get; set; }
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("passou")] public int Passou { get; set; }
    [JsonPropertyName("baixo")] public int Baixo { get; set; }
    [JsonPropertyName("medio")] public int Medio { get; set; }
    [JsonPropertyName("alto")] public int Alto { get; set; }
    [JsonPropertyName("altos")] public List<HostHardeningItem> Altos { get; set; } = [];
}

public sealed class HostHardeningItem
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("categoria")] public string Categoria { get; set; } = "";
    [JsonPropertyName("nome")] public string Nome { get; set; } = "";
    [JsonPropertyName("atual")] public string Atual { get; set; } = "";
    [JsonPropertyName("recomendado")] public string Recomendado { get; set; } = "";
}

/// <summary>
/// SECURITY allowlist: what the user already said they know. User data (a choice) -> lives in
/// &lt;data&gt;\factory\security-allowlist.json. Nome aceita * no fim (ex. "VoiceAlarm_*").
/// </summary>
public sealed class SecurityAllowlist
{
    [JsonPropertyName("versao")] public string Versao { get; set; } = "security-allowlist/v1";
    [JsonPropertyName("processos")] public List<string> Processos { get; set; } = [];
    [JsonPropertyName("portas")] public List<int> Portas { get; set; } = [];
    [JsonPropertyName("persistencia")] public List<string> Persistencia { get; set; } = [];
}

// ---------- linhas da tela ----------

/// <summary>Uma linha de PORTAS ou PERSISTENCIA ja classificada contra a allowlist.</summary>
public sealed class SecRow
{
    // the state says o que a coisa E, o botao diz o que vai FAZER.
    public const string Conhecido = "IGNORADO", Sistema = "WINDOWS", Revisar = "REVISAR", Novo = "NOVO",
        Desligado = "DESLIGADO", Bloqueado = "BLOQUEADO", Orfao = "ÓRFÃO", Intencional = "INTENCIONAL", Ameaca = "AMEAÇA", Bloqueada = "BLOQUEADA", Asr = "ASR", Protecao = "PROTEÇÃO";

    /// <summary>porta | persistencia | defender | postura.</summary>
    public string Kind { get; init; } = "";
    /// <summary>Origin in the ACHADOS screen (everything merged, without losing meaning).</summary>
    public string Fonte => Kind switch { "porta" => "REDE", "persistencia" => "INICIA", "defender" => "DEFENDER", _ => "POSTURA" };
    /// <summary>Atalho do Windows que abre a tela onde se liga a protecao (achado de postura).</summary>
    public string Abrir { get; init; } = "";
    public bool PodeAbrir => Abrir.Length > 0;
    public string? AccentFixo { get; init; }
    /// <summary>User note about this item (e.g. "HVCI off on purpose because..."). User data: seguranca-notas.json.</summary>
    public string Nota { get; set; } = "";
    public bool TemNota => Nota.Length > 0;
    /// <summary>Chave da nota: porta:nome · inicia:tipo:nome · postura:ROTULO · ev:...</summary>
    public string NotaChave => Kind switch { "porta" => "porta:" + Key, "persistencia" => $"inicia:{Tipo}:{Key}", _ => Key };
    public int? PesoFixo { get; init; }
    /// <summary>So porta e inicia sozinho tem o que cortar/aprovar.</summary>
    private bool Acionavel => Kind is "porta" or "persistencia";
    /// <summary>O que vai pra allowlist: nome do processo (porta) ou nome da entrada (persistencia).</summary>
    public string Key { get; init; } = "";
    public string Titulo { get; init; } = "";
    public string Detalhe { get; init; } = "";
    public string Caminho { get; init; } = "";
    public string Estado { get; init; } = Revisar;
    public int Pid { get; init; }
    /// <summary>persistencia: tarefa | run | inicializar (pra DESLIGAR saber como).</summary>
    public string Tipo { get; init; } = "";
    /// <summary>persistencia: pasta da tarefa, chave do registro ou pasta Inicializar.</summary>
    public string Onde { get; init; } = "";
    /// <summary>persistencia: linha de comando (Run) ou nome do atalho (Inicializar).</summary>
    public string Valor { get; init; } = "";
    /// <summary>Do guia de triagem: sem assinatura valida ou rodando de Temp/Public/AppData. Sobe na lista e fica magenta.</summary>
    public bool Risco { get; init; }

    public bool PodeAprovar => Acionavel && Estado is Revisar or Novo;
    /// <summary>DESLIGAR (persistencia) ou BLOQUEAR (porta): nunca no que e do Windows nem no que ja esta desligado.</summary>
    public bool PodeCortar => Acionavel && Estado is not (Sistema or Desligado or Bloqueado) && (Kind == "persistencia" || PodeInspecionar);
    public bool PodeReverter => Acionavel && Estado is Desligado or Bloqueado;
    public bool PodePorQue => Acionavel;
    /// <summary>O "…" so aparece se tiver o que listar (evento do Defender nao tem).</summary>
    public bool TemMenu => PodePorQue || PodeAprovar || PodeCortar || PodeReverter || PodeAbrir || PodeInspecionar;

    // ---- one main action per row: a que o parecer recomenda; o resto vai no menu "…" ----
    private Factory.Services.SecParecer? _parecer;
    public Factory.Services.SecParecer Parecer => _parecer ??= Factory.Services.SecKnowledge.Explicar(this);
    /// <summary>"O que eu faria", 1 frase, na propria linha (antes ficava atras do POR QUE?).</summary>
    public string Opiniao => Kind switch
    {
        "defender" => Estado == Ameaca ? "O Defender detectou isto. Abra o histórico de proteção e confira se foi bloqueado." : Estado == Bloqueada ? "Já bloqueado pelo Defender: nada a fazer." : "Bloqueio do ASR: se foi um programa seu (build novo), é esperado.",
        "postura" when Estado == Intencional => "Desligado de propósito: leia a nota abaixo antes de religar.",
        "postura" => PodeAbrir ? (Abrir.StartsWith("tab:") ? "Rode a auditoria na aba HARDENING." : "Ligue em Abrir no Windows (alguns pedem reiniciar).") : "",
        _ => Acionavel && Estado is not (Sistema or Conhecido) ? Parecer.Sugestao : "",
    };
    public string PrimariaAcao
    {
        get
        {
            if (PodeReverter) return "reverter";
            if (PodeAbrir) return "abrir";
            if (!Acionavel || Estado is Sistema or Conhecido) return "";
            // risco (sem assinatura / pasta de risco) NUNCA oferece IGNORAR como principal
            if (Parecer.Acao == "confiar" && PodeAprovar && !Risco) return "confiar";
            if (Parecer.Acao == "cortar" && PodeCortar) return "cortar";
            if (Risco && PodeCortar) return "cortar";
            // desconhecido sem recomendacao: a principal e IGNORAR (DESLIGAR/BLOQUEAR ficam no menu)
            if (Parecer.Acao is null && PodeAprovar && !Risco) return "confiar";
            return "porque";
        }
    }
    public string PrimariaTexto => PrimariaAcao switch
    {
        "confiar" => "Ignorar", "cortar" => CorteMenu, "reverter" => ReverteMenu, "porque" => "O que é isso?",
        "abrir" => Abrir.StartsWith("tab:") ? "Ver auditoria" : "Abrir no Windows", _ => "",
    };
    public string PrimariaEfeito => PrimariaAcao switch
    {
        "confiar" => "IGNORAR: deixa de alertar sobre isto. Não muda nada no Windows. O DESFAZER do cabeçalho volta.",
        "cortar" => Kind == "porta" ? "bloqueia a entrada no firewall; liberar desfaz" : Tipo == "tarefa" ? "desativa a tarefa; religar desfaz" : "tira do início do Windows; religar desfaz",
        "reverter" => "desfaz o que o Factory fez",
        "abrir" => "abre a tela onde se liga a proteção (nada muda sozinho)",
        "porque" => "o que é isto, se é comum ou suspeito, e o que eu faria",
        _ => "",
    };
    public bool TemPrimaria => PrimariaAcao.Length > 0;
    // painel de detalhe: as acoes que NAO sao a principal (a principal ja aparece em destaque)
    public bool ExtraAprovar => PodeAprovar && PrimariaAcao != "confiar";
    public bool ExtraCorte => PodeCortar && PrimariaAcao != "cortar";
    public bool ExtraReverte => PodeReverter && PrimariaAcao != "reverter";
    public bool ExtraAbrir => PodeAbrir && PrimariaAcao != "abrir";
    /// <summary>O botao "O QUE E ISSO?" existe em TODA linha acionavel (e a principal quando nao ha recomendacao).</summary>
    public bool MostrarOQueE => PodePorQue && PrimariaAcao != "porque";
    // perigo nao pinta o botao (28 linhas = coluna de magenta que vira ruido): a confirmacao e o dialogo
    public bool PrimariaPerigo => false;
    public bool PrimariaNeutra => TemPrimaria;
    public string CorteMenu => Corte.Length > 0 ? char.ToUpper(Corte[0]) + Corte[1..].ToLowerInvariant() : "";
    public string ReverteMenu => Reverte.Length > 0 ? char.ToUpper(Reverte[0]) + Reverte[1..].ToLowerInvariant() : "";
    public string Corte => Kind == "porta" ? "BLOQUEAR" : Estado == Orfao ? "LIMPAR" : "DESLIGAR";
    public string Reverte => Estado == Bloqueado ? "LIBERAR" : "RELIGAR";
    public string CorteDica => Kind == "porta"
        ? "regra no Firewall do Windows bloqueando conexoes de ENTRADA pra este programa (pede admin). LIBERAR desfaz."
        : Tipo switch
        {
            "tarefa" => "desativa a tarefa agendada (fica la, so nao roda). RELIGAR desfaz.",
            "run" => "tira do registro Run (o valor fica guardado em .factory\\factory; antes disso o Factory salva um backup da lista). RELIGAR poe de volta.",
            _ => "move o atalho da pasta Inicializar pra .factory\\factory\\autostart-desligados. RELIGAR devolve.",
        };
    /// <summary>Tooltip da linha: o detalhe inteiro (argumentos decidem) + o executavel.</summary>
    public string Dica => string.IsNullOrEmpty(Caminho) || Detalhe.Contains(Caminho) ? Detalhe : $"{Detalhe}\n{Caminho}";
    public bool PodeInspecionar => Acionavel && !string.IsNullOrEmpty(Caminho) && Caminho.Contains('\\');
    // Cor so pra EXCECAO: magenta = risco/ameaca, ambar = protecao desligada. Tudo o mais e cinza (revisar, ignorado, windows, defender ja bloqueado).
    public string Accent => AccentFixo ?? (Risco && Estado is Revisar or Orfao ? "magenta" : Estado switch { Novo => "magenta", _ => "dim" });
    /// <summary>Ordem na lista: o que pede atencao primeiro.</summary>
    public int Peso => PesoFixo ?? (Risco && Estado is Revisar or Orfao ? -1 : Estado switch { Novo => 0, Revisar => 1, Orfao => 2, Desligado or Bloqueado => 3, Conhecido => 4, _ => 5 });
}

/// <summary>
/// O que o Factory desligou/bloqueou (pra RELIGAR/LIBERAR). User data: &lt;data&gt;\factory\seguranca-acoes.json.
/// tipo: tarefa | run | inicializar | firewall.
/// </summary>
public sealed class SecAcao
{
    [JsonPropertyName("versao")] public string Versao { get; set; } = "seguranca-acao/v1";
    [JsonPropertyName("tipo")] public string Tipo { get; set; } = "";
    [JsonPropertyName("nome")] public string Nome { get; set; } = "";
    [JsonPropertyName("onde")] public string Onde { get; set; } = "";
    [JsonPropertyName("valor")] public string Valor { get; set; } = "";
    [JsonPropertyName("quando")] public DateTimeOffset Quando { get; set; }
}

/// <summary>Uma linha do feed do Defender.</summary>
public sealed class SecEvent
{
    public string Quando { get; init; } = "";
    public string Titulo { get; init; } = "";
    public string Detalhe { get; init; } = "";
    public string Accent { get; init; } = "amber";
}

/// <summary>Um selo da POSTURA: rotulo + valor + cor (verde ok, ambar fraco, cinza sem leitura).</summary>
public sealed class SecBadge
{
    public string Rotulo { get; init; } = "";
    public string Valor { get; init; } = "";
    public string Dica { get; init; } = "";
    public string Accent { get; init; } = "cyan";

    /// <summary>O que abre ao clicar na lampada (pagina do Windows onde se liga aquilo). Vazio = so le.</summary>
    public string Abrir { get; init; } = "";
    public string Nota { get; init; } = "";

    // Alarm panel lamp (grey everywhere was confusing): ok = ACESA VERDE,
    // problema = ACESA AMBAR, sem leitura = violeta. O valor escrito sempre diz o estado (cor nunca sozinha).
    private bool Problema => Accent == "amber";
    public Microsoft.UI.Xaml.Media.SolidColorBrush Fundo => new(Problema ? Windows.UI.Color.FromArgb(0x2E, 0xE8, 0xB0, 0x4A) : Windows.UI.Color.FromArgb(0x00, 0, 0, 0));
    public Microsoft.UI.Xaml.Media.SolidColorBrush Borda => new(Problema ? Windows.UI.Color.FromArgb(0xA0, 0xE8, 0xB0, 0x4A) : Windows.UI.Color.FromArgb(0xFF, 0x2A, 0x35, 0x47));
    public Microsoft.UI.Xaml.Media.SolidColorBrush Tinta => new(Problema ? Factory.Helpers.SegColors.Amber : Factory.Helpers.SegColors.Dim);
    // LED do kit: verde = ligado/ok, ambar = problema, anel vazio = sem leitura ou desligado de proposito (o valor escreve o estado)
    public Microsoft.UI.Xaml.Media.SolidColorBrush Led => new(Accent switch { "green" => Factory.Helpers.SegColors.Green, "amber" => Factory.Helpers.SegColors.Amber, _ => Windows.UI.Color.FromArgb(0x00, 0, 0, 0) });
    public Microsoft.UI.Xaml.Media.SolidColorBrush LedAro => new(Accent is "green" or "amber" ? Windows.UI.Color.FromArgb(0x00, 0, 0, 0) : Factory.Helpers.SegColors.Dim);
}

/// <summary>Uma linha do HardeningKitty (falha de severidade alta).</summary>
public sealed class SecHardeningRow
{
    public string Titulo { get; init; } = "";
    public string Detalhe { get; init; } = "";
}
