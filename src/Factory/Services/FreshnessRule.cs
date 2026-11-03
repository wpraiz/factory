namespace Factory.Services;

/// <summary>
/// Quao confiavel e retomar uma sessao: fresh = nada mudou na pasta desde ela; stale = a pasta
/// andou depois; drifted = o branch da sessao nao e o atual (o contexto dela pode nem existir mais).
/// </summary>
public static class FreshnessRule
{
    public sealed record Result(string Label, bool Fresh, bool Stale, bool Drifted);

    public static Result Compute(SessionRecord s, DateTimeOffset? folderNewest, string currentBranch)
    {
        var sessionBranch = s.GitBranch.Length > 0 ? s.GitBranch : s.Branches.LastOrDefault() ?? "";
        if (sessionBranch.Length > 0 && currentBranch.Length > 0
            && !sessionBranch.Equals(currentBranch, StringComparison.OrdinalIgnoreCase))
            return new Result($"drifted · {sessionBranch} ≠ {currentBranch}", false, false, true);

        if (folderNewest is null || s.LastWrite >= folderNewest.Value.AddMinutes(-5))
            return new Result("fresh", true, false, false);

        var gap = folderNewest.Value - s.LastWrite;
        var gapText = gap.TotalDays >= 1 ? $"{(int)gap.TotalDays} d" : $"{(int)gap.TotalHours} h";
        return new Result($"stale · pasta mudou {gapText} depois", false, true, false);
    }
}
