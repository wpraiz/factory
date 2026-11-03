using System.Text;
using Factory.Models;

namespace Factory.Services;

public interface IChatService
{
    bool Available { get; }
    /// <summary>Tem conversa em andamento (vai continuar com --resume).</summary>
    bool HasSession { get; }
    /// <summary>Manda uma mensagem. A primeira da conversa leva o catalogo inteiro como contexto.</summary>
    Task<ClaudeReply> AskAsync(string message, IReadOnlyList<ProjectInfo> catalog, CancellationToken ct);
    /// <summary>Zera a conversa: a proxima mensagem abre sessao nova com catalogo fresco.</summary>
    void Reset();
    string HistoryDir { get; }
}

/// <summary>
/// Papo com o Claude sobre os projetos: `claude -p` (sonnet, assinatura, sem tools) com
/// `--resume` pra manter memoria entre mensagens. Contexto = digest do catalogo (nome, estagio,
/// proxima acao, bloqueios, ultima sessao) mandado so na primeira mensagem da conversa.
/// Historico em OwnerProfile.ChatDir\&lt;data&gt;.md (vida do usuario: perfil, fora do git) e o id da conversa em
/// chat\session.txt (reabrir o app continua o papo).
/// </summary>
public sealed class ChatService : IChatService
{
    private const string SystemPrompt =
        "Voce e o assistente do cockpit pessoal de quem toca varios projetos de software. " +
        "Na primeira mensagem vem um CATALOGO com o estado real de cada projeto (lido de STATUS.md e factory.json); " +
        "responda SEMPRE a partir dele e diga 'nao sei pelo catalogo' quando o dado nao estiver la. Nunca invente estado, valor ou prazo. " +
        "Responda em pt-BR, direto, curto (ate 6 linhas salvo pedido), sem markdown pesado (sem tabela, sem cabecalho; lista simples com '-' pode). " +
        "Voce nao consegue abrir nada: se pedirem pra abrir/ir/retomar um projeto, diga que o comando e `ir <projeto>` na mesma caixa. " +
        "Quando ajudar a amadurecer uma ideia, faca no maximo 2 perguntas por vez, as de maior alavancagem.";

    public string HistoryDir { get; } = OwnerProfile.ChatDir;   // conversas = dado de vida (perfil, fora do git)

    private readonly SemaphoreSlim _gate = new(1);
    private string? _sessionId;

    public ChatService()
    {
        try
        {
            var f = Path.Combine(HistoryDir, "session.txt");
            if (File.Exists(f))
            {
                var id = File.ReadAllText(f).Trim();
                // conversa de hoje continua; de ontem comeca do zero (catalogo velho)
                if (id.Length > 0 && File.GetLastWriteTime(f).Date == DateTime.Today) _sessionId = id;
            }
        }
        catch (Exception) { }
    }

    public bool Available => ClaudeCli.Available;
    public bool HasSession => _sessionId is not null;

    public async Task<ClaudeReply> AskAsync(string message, IReadOnlyList<ProjectInfo> catalog, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var input = _sessionId is null ? BuildCatalog(catalog) + "\n\nMENSAGEM: " + message : message;
            var reply = await ClaudeCli.RunAsync(SystemPrompt, input, _sessionId, "sonnet", ct);
            if (reply.SessionId is not null && reply.SessionId != _sessionId)
            {
                _sessionId = reply.SessionId;
                Persist("session.txt", _sessionId, append: false);
            }
            Persist(DateTime.Today.ToString("yyyy-MM-dd") + ".md",
                $"\n## {DateTime.Now:HH:mm} voce\n{message}\n\n## {DateTime.Now:HH:mm} claude ({reply.DurationMs / 1000.0:0.0}s)\n{reply.Text}\n", append: true);
            return reply;
        }
        finally { _gate.Release(); }
    }

    public void Reset()
    {
        _sessionId = null;
        try { File.Delete(Path.Combine(HistoryDir, "session.txt")); } catch (Exception) { }
    }

    private void Persist(string name, string text, bool append)
    {
        try
        {
            Directory.CreateDirectory(HistoryDir);
            var f = Path.Combine(HistoryDir, name);
            if (append) File.AppendAllText(f, text, new UTF8Encoding(false));
            else File.WriteAllText(f, text, new UTF8Encoding(false));
        }
        catch (Exception) { /* historico e conveniencia */ }
    }

    /// <summary>Uma linha por projeto, mais recente primeiro. So o que foi lido de verdade.</summary>
    public static string BuildCatalog(IReadOnlyList<ProjectInfo> catalog)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"CATALOGO ({catalog.Count} projetos em {AppPaths.Root}, {DateTime.Now:yyyy-MM-dd HH:mm}):");
        foreach (var p in catalog.OrderByDescending(p => p.LastTouch))
        {
            sb.Append("- ").Append(p.Name).Append(" · ").Append(p.Path);
            sb.Append(" · tocado ").Append(p.LastTouchText);
            if (p.StageText != "—") sb.Append(" · ").Append(p.StageText);
            if (p.MoneyText != "—") sb.Append(" · grana: ").Append(p.MoneyText);
            if (p.HasPotential) sb.Append(" · ").Append(p.PotentialText);
            if (p.SessionCount > 0) sb.Append(" · ").Append(p.SessionCount).Append(" sessoes claude");
            if (p.GitBranch.Length > 0) sb.Append(" · git ").Append(p.GitBranch).Append(p.GitDirty > 0 ? $" ({p.GitDirty} sujos)" : "");
            if (p.HasTags) sb.Append(" · ").Append(p.TagsText);
            sb.AppendLine();
            if (p.HasStatus)
            {
                sb.Append("  STATUS.md ").Append(p.StatusUpdated.Length > 0 ? p.StatusUpdated : "(sem data)").AppendLine();
                if (p.HasNextAction) sb.Append("  proxima acao: ").AppendLine(Clip(p.NextAction, 400));
                if (p.HasBlockers) sb.Append("  bloqueios: ").AppendLine(Clip(p.Blockers, 300));
            }
            else sb.AppendLine("  (sem STATUS.md)");
        }
        return sb.ToString();
    }

    private static string Clip(string s, int n)
    {
        s = s.Replace("\r", "").Replace('\n', ' ').Trim();
        return s.Length > n ? s[..n] + "…" : s;
    }
}
