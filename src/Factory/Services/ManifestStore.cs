using System.Text.Encodings.Web;
using System.Text.Json;
using Factory.Models;

namespace Factory.Services;

public interface IManifestStore
{
    ProjectManifest Load(string projectPath);
    Task SaveAsync(string projectPath, ProjectManifest manifest);
}

public sealed class ManifestStore : IManifestStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public ProjectManifest Load(string projectPath)
    {
        var file = Path.Combine(projectPath, ProjectManifest.FileName);
        if (!File.Exists(file)) return new ProjectManifest();
        try
        {
            return JsonSerializer.Deserialize<ProjectManifest>(File.ReadAllText(file), Options) ?? new ProjectManifest();
        }
        catch (JsonException)
        {
            return new ProjectManifest { Notes = "(factory.json invalido — salvar sobrescreve)" };
        }
    }

    public Task SaveAsync(string projectPath, ProjectManifest manifest)
    {
        var file = Path.Combine(projectPath, ProjectManifest.FileName);
        return File.WriteAllTextAsync(file, JsonSerializer.Serialize(manifest, Options) + Environment.NewLine);
    }
}
