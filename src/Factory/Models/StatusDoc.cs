namespace Factory.Models;

/// <summary>Campos extraidos de um STATUS.md (formato da skill /status).</summary>
public sealed record StatusDoc(string Raw, string Updated, string NextAction, string Blockers)
{
    public static readonly StatusDoc Empty = new("", "", "", "");
}
