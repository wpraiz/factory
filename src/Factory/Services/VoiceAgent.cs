using System.Net.Http;
using System.Text.Json;
using Factory.Models;

namespace Factory.Services;

public interface IVoiceAgent
{
    /// <summary>Chave ElevenLabs no ambiente + agent.json com agent_id.</summary>
    bool Configured { get; }
    string? AgentId { get; }
    /// <summary>"ODIN-lab (opus 5.5) · claude-opus-5-5": nome e cerebro da agente em uso, pro painel.</summary>
    string AgentLabel { get; }
    /// <summary>Le na API qual LLM a agente em uso esta rodando (o json local pode estar velho).</summary>
    Task<string?> GetLlmAsync(CancellationToken ct);
    /// <summary>Troca o LLM da agente em uso (PATCH; vale na proxima conversa). Devolve "" ou a mensagem de erro.</summary>
    Task<string> SetLlmAsync(string llm, CancellationToken ct);
    /// <summary>Pasta com index.html, orb-gl.js e factory-orb.js (servida ao WebView2 por host virtual).</summary>
    string OrbDir { get; }
    /// <summary>Por que nao esta configurado (pra UI dizer o que fazer), ou "".</summary>
    string Missing { get; }
    /// <summary>URL assinada pra abrir a sessao. A chave fica aqui; o JS so recebe a URL. Nao gasta credito.</summary>
    Task<string> GetSignedUrlAsync(CancellationToken ct);
    /// <summary>Catalogo pra variavel dinamica {{catalogo}} da agente, cortado pra caber.</summary>
    string BuildCatalog(IReadOnlyList<ProjectInfo> catalog);
    /// <summary>Memoria do Odin (ultimas linhas), pra variavel dinamica {{memoria}}.</summary>
    string LoadMemory();
    /// <summary>Uma linha datada na memoria (ferramenta `lembrar` e resumo de fim de conversa).</summary>
    void AppendMemory(string text);
    /// <summary>Resume a conversa (VOCE/ODIN) em ate 5 linhas via claude -p e grava na memoria. Assinatura, sem credito.</summary>
    Task SummarizeAsync(string transcript, CancellationToken ct);
    string MemoryFile { get; }
    /// <summary>Transcricao crua da ultima conversa (VOCE/ODIN), pra variavel {{ultima_conversa}}.</summary>
    string LoadLastTranscript();
    void SaveLastTranscript(string transcript);
    /// <summary>Comeca uma conversa nova no arquivo (com marca de resumo pendente).</summary>
    void BeginLive();
    /// <summary>Uma linha da conversa em andamento, gravada na hora: fechar o app nao perde nada.</summary>
    void AppendLive(string line);
    /// <summary>Conversa terminou e foi resumida: tira a marca de pendente.</summary>
    void EndLive();
    /// <summary>Se a conversa anterior caiu sem resumo, resume agora (assincrono) e devolve true.</summary>
    Task<bool> SummarizePendingAsync(CancellationToken ct);
    /// <summary>Primeira frase de retomada, ou null pra usar a padrao da agente.</summary>
    string? ResumeGreeting();
    /// <summary>familia.md (escrito por ele, sem nomes), pra variavel {{familia}}. Nunca vai pro git.</summary>
    string LoadFamily();
}

/// <summary>
/// A orb que fala: agente de voz da ElevenLabs , hospedada
/// num WebView2 na tela CHAT. Este servico so faz o que exige a chave: URL assinada. Abrir a
/// sessao (que gasta minutos de conversa) e sempre um clique na tela, nunca automatico.
/// Agente criada/atualizada por `node tools\create-orb-agent.mjs`.
/// </summary>
public sealed class VoiceAgent : IVoiceAgent
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly string? _key = Environment.GetEnvironmentVariable("ELEVENLABS_API_KEY");

    public string OrbDir { get; } = Path.Combine(AppContext.BaseDirectory, "Assets", "orb");
    private static readonly string RepoOrbDir = Path.Combine(AppPaths.Repo, "src", "Factory", "Assets", "orb");
    public string? AgentId { get; }
    // memoria/familia/ultima conversa = dado de vida: mora no PERFIL (fora do git), pela porta unica
    public string MemoryFile { get; } = Path.Combine(OwnerProfile.ChatDir, "odin-memoria.md");

    private const string SummaryPrompt =
        "Voce recebe a transcricao de uma conversa por voz entre uma pessoa (VOCE) e Odin, o assistente do cockpit dela. " +
        "Escreva de 1 a 5 linhas curtas, em pt-BR, comecando cada uma com '- ', com o que vale lembrar na PROXIMA conversa: " +
        "como a pessoa estava, decisoes, pendencias, o que Odin prometeu, preferencias que ela declarou. Fatos, sem adjetivo, sem preambulo. " +
        "NUNCA registre limites ou capacidades do Odin ('Odin nao consegue X'): as maos mudam entre conversas e isso envenena a memoria. Se nao houver nada, responda so '-'.";

    public VoiceAgent()
    {
        try
        {
            // Homolog fala com a agente de teste; prod com a agente principal. Mesma memoria (e a mesma pessoa).
            // Branch odin-opus-5-5: homolog fala com a agente LAB (Opus 5.5) se ela existir, senao com a de teste.
            var lab = Path.Combine(OrbDir, "agent-lab.json");
            var f = App.IsHomolog && File.Exists(lab) ? lab : Path.Combine(OrbDir, App.IsHomolog ? "agent-test.json" : "agent.json");
            if (File.Exists(f))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(f));
                if (doc.RootElement.TryGetProperty("agent_id", out var id) && id.ValueKind == JsonValueKind.String)
                    AgentId = id.GetString();
                var nome = doc.RootElement.TryGetProperty("nome", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                var llm = doc.RootElement.TryGetProperty("llm", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString() : null;
                AgentLabel = string.Join(" · ", new[] { nome, llm }.Where(x => !string.IsNullOrWhiteSpace(x))!);
            }
        }
        catch (Exception) { AgentId = null; }
    }

    public string AgentLabel { get; private set; } = "";

    public bool Configured => !string.IsNullOrWhiteSpace(_key) && !string.IsNullOrWhiteSpace(AgentId) && File.Exists(Path.Combine(OrbDir, "index.html"));

    public string Missing =>
        string.IsNullOrWhiteSpace(_key) ? "ELEVENLABS_API_KEY nao definida neste processo (defina e reabra o app)"
        : string.IsNullOrWhiteSpace(AgentId) ? (App.IsHomolog ? "Assets\\orb\\agent-test.json sem agent_id (rode node tools\\create-orb-agent.mjs --teste)" : "Assets\\orb\\agent.json sem agent_id (rode node tools\\create-orb-agent.mjs)")
        : !File.Exists(Path.Combine(OrbDir, "index.html")) ? "Assets\\orb\\index.html nao foi copiado no publish"
        : "";

    public async Task<string> GetSignedUrlAsync(CancellationToken ct)
    {
        if (!Configured) throw new InvalidOperationException(Missing);
        using var req = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.elevenlabs.io/v1/convai/conversation/get-signed-url?agent_id={AgentId}");
        req.Headers.Add("xi-api-key", _key);
        using var res = await Http.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"ElevenLabs {(int)res.StatusCode}: {(body.Length > 200 ? body[..200] : body)}");
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("signed_url").GetString() ?? throw new InvalidOperationException("resposta sem signed_url");
    }

    public async Task<string?> GetLlmAsync(CancellationToken ct)
    {
        if (!Configured) return null;
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.elevenlabs.io/v1/convai/agents/{AgentId}");
        req.Headers.Add("xi-api-key", _key);
        using var res = await Http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var llm = doc.RootElement.GetProperty("conversation_config").GetProperty("agent").GetProperty("prompt").GetProperty("llm").GetString();
        if (llm is not null) UpdateLabelLlm(llm);
        return llm;
    }

    public async Task<string> SetLlmAsync(string llm, CancellationToken ct)
    {
        if (!Configured) return Missing;
        llm = llm.Trim();
        if (llm.Length == 0) return "nome vazio";
        using var req = new HttpRequestMessage(HttpMethod.Patch, $"https://api.elevenlabs.io/v1/convai/agents/{AgentId}");
        req.Headers.Add("xi-api-key", _key);
        req.Content = new StringContent(JsonSerializer.Serialize(new { conversation_config = new { agent = new { prompt = new { llm } } } }), System.Text.Encoding.UTF8, "application/json");
        using var res = await Http.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            // a API lista os nomes validos na mensagem; devolve so o comeco
            var msg = body.Contains("Input should be") ? "nome invalido pra ElevenLabs" : body;
            return $"ElevenLabs {(int)res.StatusCode}: {(msg.Length > 160 ? msg[..160] : msg)}";
        }
        UpdateLabelLlm(llm);
        // Copia local do llm na pasta do publish E no repo: so a do publish ficava certa, o repo seguia velho e o
        // fiscal certificou o cerebro errado (it happened). A verdade continua sendo a API (tools\odin-now.mjs).
        var nome = App.IsHomolog && File.Exists(Path.Combine(OrbDir, "agent-lab.json")) ? "agent-lab.json" : App.IsHomolog ? "agent-test.json" : "agent.json";
        foreach (var dir in new[] { OrbDir, RepoOrbDir })
        {
            try
            {
                var f = Path.Combine(dir, nome);
                if (!File.Exists(f)) continue;
                var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(f)) as System.Text.Json.Nodes.JsonObject;
                if (node is not null) { node["llm"] = llm; node["llm_trocado_em"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm"); File.WriteAllText(f, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n"); }
            }
            catch (Exception) { /* sem permissao de escrita: a agente ja mudou, so a copia fica velha */ }
        }
        return "";
    }

    private void UpdateLabelLlm(string llm)
    {
        var nome = AgentLabel.Split(" · ")[0];
        AgentLabel = nome.Length > 0 ? nome + " · " + llm : llm;
    }

    private string LastFile => Path.Combine(Path.GetDirectoryName(MemoryFile)!, "odin-ultima-conversa.md");

    public string LoadLastTranscript()
    {
        try
        {
            if (!File.Exists(LastFile)) return "(nenhuma)";
            var t = File.ReadAllText(LastFile).Trim();
            return t.Length > 6000 ? "(…)\n" + t[^6000..] : t;
        }
        catch (Exception) { return "(nenhuma)"; }
    }

    private const string PendingMark = "· pendente-resumo";

    public void BeginLive()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LastFile)!);
            File.WriteAllText(LastFile, $"[conversa de {DateTime.Now:dd/MM/yyyy HH:mm} {PendingMark}]\n", new System.Text.UTF8Encoding(false));
        }
        catch (Exception) { }
    }

    public void AppendLive(string line)
    {
        try { File.AppendAllText(LastFile, line.Replace("\r", " ").Replace('\n', ' ').Trim() + "\n", new System.Text.UTF8Encoding(false)); }
        catch (Exception) { }
    }

    public void EndLive()
    {
        try
        {
            if (!File.Exists(LastFile)) return;
            var t = File.ReadAllText(LastFile);
            File.WriteAllText(LastFile, t.Replace(" " + PendingMark, ""), new System.Text.UTF8Encoding(false));
        }
        catch (Exception) { }
    }

    public async Task<bool> SummarizePendingAsync(CancellationToken ct)
    {
        try
        {
            if (!File.Exists(LastFile)) return false;
            var t = File.ReadAllText(LastFile);
            if (!t.Contains(PendingMark)) return false;
            var body = string.Join("\n", t.Split('\n').Skip(1));
            EndLive();   // marca sai antes, pra nao resumir duas vezes se abrir de novo
            if (body.Trim().Length > 0) await SummarizeAsync(body, ct);
            return true;
        }
        catch (Exception) { return false; }
    }

    /// <summary>Se a ultima conversa e recente (≤ 6 h) e tem fala do usuario, abre citando a ultima fala.</summary>
    public string? ResumeGreeting()
    {
        try
        {
            // Never the same opening line twice.
            // Abertura curta e variada; se houve conversa nas ultimas 6 h, retoma a ultima frase do usuario.
            var aberturas = new[] { "Continua.", "Fala.", "Seguimos.", "Diz.", "Tô ouvindo.", "Onde a gente estava?", "Vai.", "Pode falar.", "Retomando." };
            var abre = aberturas[Random.Shared.Next(aberturas.Length)];
            if (!File.Exists(LastFile) || DateTime.Now - File.GetLastWriteTime(LastFile) > TimeSpan.FromHours(6)) return abre;
            var lines = File.ReadAllLines(LastFile);
            var lastUser = lines.LastOrDefault(l => l.StartsWith("VOCE: "))?[6..].Trim();
            if (string.IsNullOrWhiteSpace(lastUser) || lastUser.Length < 4) return abre;
            var words = lastUser.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var quote = string.Join(' ', words.Take(12)) + (words.Length > 12 ? "…" : "");
            var formas = new[] { $"{abre} Tu dizia: \"{quote}\".", $"Ficou no ar: \"{quote}\". Segue.", $"\"{quote}\": era isso. Continua.", $"{abre} Última coisa tua: \"{quote}\"." };
            return formas[Random.Shared.Next(formas.Length)];
        }
        catch (Exception) { return "Continua."; }
    }

    public void SaveLastTranscript(string transcript)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LastFile)!);
            File.WriteAllText(LastFile, $"[conversa de {DateTime.Now:dd/MM/yyyy HH:mm}]\n{transcript.Trim()}\n", new System.Text.UTF8Encoding(false));
        }
        catch (Exception) { }
    }

    public string LoadFamily()
    {
        try
        {
            var f = Path.Combine(Path.GetDirectoryName(MemoryFile)!, "familia.md");
            if (!File.Exists(f)) return "(nada ainda)";
            var t = File.ReadAllText(f).Trim();
            return t.Length > 8000 ? t[..8000] : t;
        }
        catch (Exception) { return "(nada ainda)"; }
    }

    public string LoadMemory()
    {
        try
        {
            if (!File.Exists(MemoryFile)) return "(sem memoria ainda)";
            var lines = File.ReadAllLines(MemoryFile).Where(l => l.Trim().Length > 0).ToList();
            var tail = lines.Skip(Math.Max(0, lines.Count - 80));
            var text = string.Join("\n", tail);
            return text.Length > 5000 ? text[^5000..] : text;
        }
        catch (Exception) { return "(memoria ilegivel)"; }
    }

    public void AppendMemory(string text)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MemoryFile)!);
            var line = text.Replace("\r", " ").Replace('\n', ' ').Trim();
            File.AppendAllText(MemoryFile, $"- {DateTime.Now:yyyy-MM-dd HH:mm} · {line}\n", new System.Text.UTF8Encoding(false));
        }
        catch (Exception) { /* memoria e conveniencia */ }
    }

    public async Task SummarizeAsync(string transcript, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(transcript) || !ClaudeCli.Available) return;
        try
        {
            var r = await ClaudeCli.RunAsync(SummaryPrompt, transcript, null, "sonnet", ct);
            foreach (var l in r.Text.Split('\n').Select(x => x.Trim().TrimStart('-').Trim()).Where(x => x.Length > 1).Take(5))
                AppendMemory(l);
        }
        catch (Exception) { /* sem resumo desta vez */ }
    }

    public string BuildCatalog(IReadOnlyList<ProjectInfo> catalog)
    {
        // Mesmo digest do chat de texto; a variavel dinamica tem que caber no prompt da agente.
        var text = ChatService.BuildCatalog(catalog);
        const int max = 9000;
        return text.Length <= max ? text : text[..max] + "\n(catalogo cortado aqui)";
    }
}
