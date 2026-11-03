using System.Text;
using Factory.Models;

namespace Factory.Services;

public interface ISecuritySquad
{
    string? Ultimo { get; }
    DateTimeOffset? UltimoQuando { get; }
    string Pasta { get; }
    /// <summary>Convoca o squad (1 chamada `claude -p`, assinatura). So por clique.</summary>
    Task<string> ConvocarAsync(HostSnapshot s, IReadOnlyList<SecRow> portas, IReadOnlyList<SecRow> pers, CancellationToken ct = default);
    /// <summary>Parecer do squad sobre UMA linha (botao PERGUNTAR AO SQUAD do POR QUE?).</summary>
    Task<string> PerguntarAsync(SecRow row, HostSnapshot s, CancellationToken ct = default);
}

/// <summary>
/// SECURITY SQUAD HELPER: cinco papeis (Chefe, Triagem, Caca-ameaca, Hardening, Advogado do
/// diabo) em UMA chamada `claude -p` via ClaudeCli (assinatura, sem tools, MCP zerado) — um squad de 5 chamadas gastaria
/// 5x pra dizer quase o mesmo. O prompt mora em Assets\security\squad.md (editavel). Resultado vira dado do usuario:
/// <dados>\factory\seguranca-pareceres\squad-*.md. O squad so OPINA; quem aperta o botao e o usuario.
/// </summary>
public sealed class SecuritySquad : ISecuritySquad
{
    public string Pasta { get; } = Path.Combine(AppPaths.Factory, "seguranca-pareceres");
    private static readonly string PromptPath = Path.Combine(AppContext.BaseDirectory, "Assets", "security", "squad.md");

    public string? Ultimo { get; private set; }
    public DateTimeOffset? UltimoQuando { get; private set; }

    public SecuritySquad()
    {
        try
        {
            if (!Directory.Exists(Pasta)) return;
            var f = new DirectoryInfo(Pasta).GetFiles("squad-*.md").OrderByDescending(x => x.LastWriteTimeUtc).FirstOrDefault();
            if (f is null) return;
            Ultimo = File.ReadAllText(f.FullName);
            UltimoQuando = f.LastWriteTime;
        }
        catch (Exception) { /* parecer antigo e conforto */ }
    }

    public async Task<string> ConvocarAsync(HostSnapshot s, IReadOnlyList<SecRow> portas, IReadOnlyList<SecRow> pers, CancellationToken ct = default)
    {
        if (!ClaudeCli.Available) throw new InvalidOperationException("claude CLI não encontrado — o squad precisa dele");
        var reply = await ClaudeCli.RunAsync(File.ReadAllText(PromptPath), Resumo(s, portas, pers), null, "sonnet", ct);
        Directory.CreateDirectory(Pasta);
        var arq = Path.Combine(Pasta, $"squad-{DateTime.Now:yyyyMMdd-HHmm}.md");
        File.WriteAllText(arq, reply.Text, new UTF8Encoding(false));
        Ultimo = reply.Text;
        UltimoQuando = DateTimeOffset.Now;
        return reply.Text;
    }

    public async Task<string> PerguntarAsync(SecRow r, HostSnapshot s, CancellationToken ct = default)
    {
        if (!ClaudeCli.Available) throw new InvalidOperationException("claude CLI não encontrado");
        var p = SecKnowledge.Explicar(r);
        var input = new StringBuilder()
            .AppendLine("PERGUNTA: o que é este item e o que eu faço com ele? Responda em no máximo 120 palavras: o que é, quão confiante você está, e o botão exato (IGNORAR / DESLIGAR / LIMPAR / BLOQUEAR / nada).")
            .AppendLine().AppendLine($"ITEM: {r.Titulo} ({r.Kind}) · estado {r.Estado}{(r.Risco ? " · RISCO" : "")}")
            .AppendLine($"DETALHE: {r.Detalhe}").AppendLine($"ARQUIVO: {r.Caminho}")
            .AppendLine($"PARECER LOCAL DO FACTORY (opinião, pode estar errada): {p.Oque} | {p.Risco} | {p.Sugestao}")
            .AppendLine($"CONTEXTO: Defender 24h = {s.Eventos.Count} eventos; admin={s.Admin}.").ToString();
        var reply = await ClaudeCli.RunAsync(File.ReadAllText(PromptPath) + "\n\nPara esta pergunta responda NO FORMATO CURTO pedido, sem as seções acima.", input, null, "sonnet", ct);
        return reply.Text;
    }

    /// <summary>Retrato compacto: o que o squad realmente precisa (nada de 66 linhas de porta de svchost).</summary>
    private static string Resumo(HostSnapshot s, IReadOnlyList<SecRow> portas, IReadOnlyList<SecRow> pers)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"RETRATO lido {s.Quando.LocalDateTime:dd/MM HH:mm} · {(s.Admin ? "COM admin" : "SEM admin (BitLocker/Secure Boot podem estar sem leitura)")}");
        sb.AppendLine("\nPOSTURA");
        foreach (var b in HostSecurityService.Postura(s)) sb.AppendLine($"- {b.Rotulo}: {b.Valor}");
        sb.AppendLine("\nDEFENDER 24h");
        if (s.Eventos.Count == 0) sb.AppendLine("- nenhum evento");
        foreach (var e in s.Eventos.OrderByDescending(e => e.Quando).Take(12))
        {
            var v = HostSecurityService.Evento(e);
            sb.AppendLine($"- {v.Quando} {v.Titulo} | {v.Detalhe}");
        }
        sb.AppendLine("\nREGRAS ASR");
        foreach (var a in s.Postura.Asr) sb.AppendLine($"- {HostSecurityService.AsrNome(a.Id)}: {(a.Acao == 1 ? "bloqueando" : a.Acao is 2 or 6 ? "auditando" : "desligada")}");
        void Lista(string titulo, IReadOnlyList<SecRow> rows)
        {
            sb.AppendLine($"\n{titulo} (total {rows.Count}; só o que NÃO é do Windows nem já confiável)");
            foreach (var r in rows.Where(r => r.Estado is not (SecRow.Sistema or SecRow.Conhecido)).Take(40))
                sb.AppendLine($"- [{r.Estado}{(r.Risco ? ",RISCO" : "")}] {r.Titulo} · {Cortar(r.Detalhe, 200)}");
        }
        Lista("PORTAS ABERTAS PRA REDE", portas);
        Lista("INICIA SOZINHO", pers);
        if (s.Hardening is { } h)
        {
            sb.AppendLine($"\nHARDENINGKITTY: nota {h.Score:0.00}/6 · {h.Alto} altas, {h.Medio} médias, {h.Baixo} baixas");
            foreach (var a in h.Altos.Take(15)) sb.AppendLine($"- {a.Categoria}: {a.Nome} (hoje {a.Atual}, recomendado {a.Recomendado})");
        }
        else sb.AppendLine("\nHARDENINGKITTY: nunca auditado.");
        return sb.ToString();
    }

    private static string Cortar(string t, int n) => t.Length <= n ? t : t[..n] + "…";
}
