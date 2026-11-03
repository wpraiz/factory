using System.Windows.Input;
using Factory.Services;

namespace Factory.Models;

/// <summary>Um arquivo (memoria, wiki, .factory, outro projeto) que cita o termo.</summary>
public sealed class MentionHit
{
    public required string Source { get; init; }
    public required string Path { get; init; }
    public required int Hits { get; init; }
    public required string Snippet { get; init; }
    public required DateTimeOffset Modified { get; init; }
    public ICommand? OpenCommand { get; set; }
    /// <summary>Quando o arquivo mora em outro projeto de <raiz>: pula pra ele.</summary>
    public ICommand? GoCommand { get; set; }

    public bool HasGo => GoCommand is not null;
    public string Name => System.IO.Path.GetFileName(Path);
    public string Meta => $"{Source} · {Hits}x · {ProjectInfo.Humanize(Modified)}";
}

public sealed record DbTable(string Name, long Rows);

public sealed record DbInfo(string Path, long SizeBytes, IReadOnlyList<DbTable> Tables, string? Error);

public sealed record RecentFile(string RelPath, DateTimeOffset Modified, long Size);

public sealed record FolderProfile(
    int Files, int Dirs, long Bytes,
    IReadOnlyList<(string Ext, int Count)> Exts,
    IReadOnlyList<RecentFile> Recent,
    IReadOnlyList<string> EntryPoints,
    string GitRemote, IReadOnlyList<string> GitLog,
    DateTimeOffset? Oldest, DateTimeOffset? Newest,
    bool Truncated)
{
    public StackInfo Stack { get; init; } = StackInfo.Empty;
}

public sealed class TrailReport
{
    public FolderProfile? Folder { get; set; }
    public List<DbInfo> Databases { get; } = [];
    public List<SessionRecord> OwnSessions { get; set; } = [];
    public List<(SessionRecord Session, int Hits)> MentionSessions { get; set; } = [];
    public List<MentionHit> DocMentions { get; } = [];
    public SystemReport? System { get; set; }
    public bool IndexWasReady { get; set; }
}

/// <summary>Processo vivo cuja linha de comando ou executavel aponta pra pasta do projeto.</summary>
public sealed record LiveProcess(int Pid, string Name, string CommandLine)
{
    public string Text => $"{Name}  PID {Pid}";
    public string Detail => CommandLine.Length > 160 ? CommandLine[..160] + "…" : CommandLine;
}

/// <summary>Atalho .lnk (Desktop / Menu Iniciar) que aponta pra dentro do projeto.</summary>
public sealed record ShortcutHit(string LnkPath, string Target, string Arguments)
{
    public string Name => System.IO.Path.GetFileNameWithoutExtension(LnkPath);
    public string Detail => (Target + " " + Arguments).Trim();
}

/// <summary>Task agendada cuja acao cita a pasta do projeto.</summary>
public sealed record TaskHit(string Path, string State, DateTime? NextRun, string Action)
{
    public string Text => $"{Path}  ·  {State}" + (NextRun is { } n ? $"  ·  proxima {n:MM-dd HH:mm}" : "");
}

public sealed class SystemReport
{
    public List<LiveProcess> Processes { get; } = [];
    public List<ShortcutHit> Shortcuts { get; } = [];
    public List<TaskHit> Tasks { get; } = [];
    public List<string> Errors { get; } = [];
}
