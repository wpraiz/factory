using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using Factory.Services;

namespace Factory.Models;

/// <summary>Contexto do projeto usado pra enriquecer a linha (o que mudou, fresh/stale/drifted).</summary>
public sealed record SessionContext(string ProjectPath, DateTimeOffset? FolderNewest, string CurrentBranch, Func<string, ICommand> OpenFile);

/// <summary>Linha de sessao pronta pra UI (montada a partir de um <see cref="SessionRecord"/>).</summary>
public sealed partial class SessionInfo : ObservableObject
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Meta { get; init; }
    public required string FirstPrompt { get; init; }
    public required SessionRecord Record { get; init; }
    /// <summary>Abre `claude --resume Id` na pasta certa (a do projeto ou o cwd original da sessao).</summary>
    public required ICommand ResumeCommand { get; init; }
    /// <summary>Pula pro projeto onde a sessao foi aberta (quando o cwd e um projeto de <raiz>).</summary>
    public ICommand? GoCommand { get; init; }
    /// <summary>Gera o briefing de retomada (claude -p). Atribuido pelo ViewModel.</summary>
    public ICommand? BriefCommand { get; set; }

    /// <summary>Arquivos que a sessao criou/editou dentro do projeto (Write/Edit), clicaveis.</summary>
    public IReadOnlyList<Chip> Edited { get; init; } = [];
    public bool HasEdited => Edited.Count > 0;
    public string EditedText => $"▾ {Edited.Count} arquivos mudados";

    public string Freshness { get; init; } = "";
    public bool IsFresh { get; init; }
    public bool IsStale { get; init; }
    public bool IsDrifted { get; init; }

    /// <summary>Arquivo mexido ha < 2 min: sessao provavelmente aberta agora. Reavaliado por timer.</summary>
    [ObservableProperty] private bool _isLive;

    /// <summary>Expander do "o que mudou".</summary>
    [ObservableProperty] private bool _showEdited;

    public bool HasGo => GoCommand is not null;

    public void RefreshLive() => IsLive = Record.IsLive;

    public static SessionInfo From(SessionRecord r, int hits, ICommand resume, ICommand? go = null, SessionContext? ctx = null)
    {
        var parts = new List<string>(6)
        {
            ProjectInfo.Humanize(r.LastWrite),
            $"{r.SizeBytes / 1024.0 / 1024.0:F1} MB",
        };
        if (r.Duration is { } d && d.TotalMinutes >= 1) parts.Add(d.TotalHours >= 1 ? $"{(int)d.TotalHours}h{d.Minutes:00}" : $"{(int)d.TotalMinutes} min");
        if (r.Turns > 0) parts.Add($"{r.Turns} turnos");
        if (hits > 0) parts.Add($"{hits} menções");
        if (r.IsSubagent) parts.Add("sub-agente");
        parts.Add(r.Cwd.Length > 0 ? r.Cwd : r.Slug);

        var edited = new List<Chip>();
        var fresh = new FreshnessRule.Result("", false, false, false);
        if (ctx is not null)
        {
            var root = ctx.ProjectPath.TrimEnd('\\') + "\\";
            foreach (var t in r.Touched.Where(t => t.IsEdit && t.Path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                         .DistinctBy(t => t.Path.ToLowerInvariant()).Take(20))
            {
                var full = t.Path;
                edited.Add(new Chip
                {
                    Label = $"{t.Tool.ToLowerInvariant()}  {Path.GetRelativePath(ctx.ProjectPath, full)}",
                    Command = ctx.OpenFile(full), Tooltip = full,
                });
            }
            fresh = FreshnessRule.Compute(r, ctx.FolderNewest, ctx.CurrentBranch);
        }

        return new SessionInfo
        {
            Id = r.Id,
            Title = r.Title,
            Meta = string.Join(" · ", parts),
            FirstPrompt = r.FirstPrompt == r.Title ? "" : r.FirstPrompt,
            Record = r,
            ResumeCommand = resume,
            GoCommand = go,
            IsLive = r.IsLive,
            Edited = edited,
            Freshness = fresh.Label,
            IsFresh = fresh.Fresh,
            IsStale = fresh.Stale,
            IsDrifted = fresh.Drifted,
        };
    }
}
