using System.Text;
using System.Text.RegularExpressions;
using Factory.Models;

namespace Factory.Services;

public interface IStatusParser
{
    StatusDoc Parse(string raw);
}

/// <summary>
/// Extrai "Atualizado", "Proxima acao" e "Bloqueios" de um STATUS.md. Tolerante:
/// os STATUS reais variam (heading, **negrito:**, com/sem acento) — o parser tenta
/// o formato canonico da skill e cai pra heuristicas.
/// </summary>
public sealed partial class StatusParser : IStatusParser
{
    [GeneratedRegex(@"^\**\s*(?:atualizado|[úu]ltima\s+sess[ãa]o)\s*[:*]+\s*(.+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex UpdatedRx();

    [GeneratedRegex(@"pr[oó]xima\s+a[cç][aã]o", RegexOptions.IgnoreCase)]
    private static partial Regex NextActionRx();

    [GeneratedRegex(@"bloqueios?", RegexOptions.IgnoreCase)]
    private static partial Regex BlockersRx();

    public StatusDoc Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return StatusDoc.Empty;

        var updated = UpdatedRx().Match(raw) is { Success: true } m ? m.Groups[1].Value.Trim('*', ' ') : "";
        var lines = raw.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();

        return new StatusDoc(raw, updated, Section(lines, NextActionRx()), Section(lines, BlockersRx()));
    }

    /// <summary>
    /// Acha a linha que "abre" a secao (heading `## ...` ou `**...:**`) e devolve o
    /// texto ate o proximo heading, limitado. Se a abertura ja traz o conteudo na mesma
    /// linha (`**Proxima acao do usuario:** fazer X`), devolve so isso.
    /// </summary>
    private static string Section(string[] lines, Regex opener)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var isHeading = line.StartsWith('#');
            var isBold = line.StartsWith("**");
            if (!(isHeading || isBold) || !opener.IsMatch(line)) continue;

            if (isBold)
            {
                var close = line.IndexOf(":**", StringComparison.Ordinal);
                if (close < 0) close = line.IndexOf("**", 2, StringComparison.Ordinal);
                var inline = close >= 0 ? line[(close + (line[close] == ':' ? 3 : 2))..].Trim() : "";
                if (inline.Length > 0) return Clip(inline);
            }

            var sb = new StringBuilder();
            var count = 0;
            for (var j = i + 1; j < lines.Length && count < 6; j++)
            {
                var l = lines[j];
                if (l.StartsWith('#')) break;
                if (l.StartsWith("---")) break;
                if (string.IsNullOrWhiteSpace(l)) { if (sb.Length > 0 && count >= 2) break; continue; }
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(l.TrimStart());
                count++;
            }
            if (sb.Length > 0) return Clip(sb.ToString());
        }
        return "";
    }

    private static string Clip(string s) => s.Length > 420 ? s[..420].TrimEnd() + " …" : s;
}
