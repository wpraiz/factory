using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Factory.Services;

public sealed record VoiceInfo(string Id, string Name, string Category)
{
    public string Label => Category is "cloned" or "professional" ? $"★ {Name}" : Name;
}

public interface IAudioStudio
{
    bool Configured { get; }
    string OutputDir { get; }
    Task<IReadOnlyList<VoiceInfo>> VoicesAsync(CancellationToken ct = default);
    /// <summary>Gera MP3 e devolve o caminho. Gasta creditos da ElevenLabs — so por clique.</summary>
    Task<string> SpeakAsync(string text, string voiceId, string fileStem, CancellationToken ct = default);
    IReadOnlyList<string> ListOutputs();
    /// <summary>A voz de FACTORY_VOICE, senao a primeira clonada, senao a primeira. null = sem chave/sem voz.</summary>
    Task<VoiceInfo?> DefaultVoiceAsync(CancellationToken ct = default);
    /// <summary>Ultimo MP3 gerado cujo nome comeca com o prefixo (cache barato: nao regera o que ja existe).</summary>
    string? FindOutput(string stemPrefix);
}

/// <summary>
/// Texto → voz pela API da ElevenLabs (chave em ELEVENLABS_API_KEY, escopo TTS/voices).
/// Saida em .factory\deliverables\audio — espaco de operacao do usuario, nao do app.
/// </summary>
public sealed class AudioStudio : IAudioStudio
{
    public const int MaxChars = 4500;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };
    private readonly string? _key = Environment.GetEnvironmentVariable("ELEVENLABS_API_KEY");

    public bool Configured => !string.IsNullOrWhiteSpace(_key);
    public string OutputDir => Path.Combine(AppPaths.Dados, "deliverables", "audio");

    private sealed class VoicesResponse
    {
        [JsonPropertyName("voices")] public List<VoiceJson> Voices { get; set; } = [];
    }

    private sealed class VoiceJson
    {
        [JsonPropertyName("voice_id")] public string Id { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("category")] public string Category { get; set; } = "";
    }

    public async Task<IReadOnlyList<VoiceInfo>> VoicesAsync(CancellationToken ct = default)
    {
        if (!Configured) return [];
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.elevenlabs.io/v1/voices");
        req.Headers.Add("xi-api-key", _key);
        using var res = await Http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        var data = await res.Content.ReadFromJsonAsync<VoicesResponse>(cancellationToken: ct) ?? new VoicesResponse();
        // as do usuario (clonadas/profissionais) primeiro, depois o catalogo
        return data.Voices
            .Select(v => new VoiceInfo(v.Id, v.Name, v.Category))
            .OrderBy(v => v.Category is "cloned" or "professional" ? 0 : 1)
            .ThenBy(v => v.Name)
            .ToList();
    }

    public async Task<string> SpeakAsync(string text, string voiceId, string fileStem, CancellationToken ct = default)
    {
        if (!Configured) throw new InvalidOperationException("ELEVENLABS_API_KEY nao definida neste processo (reinicie o app depois de definir)");
        text = text.Trim();
        if (text.Length == 0) throw new ArgumentException("texto vazio");
        if (text.Length > MaxChars) text = text[..MaxChars];

        using var req = new HttpRequestMessage(HttpMethod.Post,
            $"https://api.elevenlabs.io/v1/text-to-speech/{voiceId}?output_format=mp3_44100_128");
        req.Headers.Add("xi-api-key", _key);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("audio/mpeg"));
        req.Content = JsonContent.Create(new
        {
            text,
            model_id = "eleven_multilingual_v2",
            voice_settings = new { stability = 0.5, similarity_boost = 0.8, style = 0.2, use_speaker_boost = true },
        });

        using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"ElevenLabs {(int)res.StatusCode}: {Clip(body, 300)}");
        }

        Directory.CreateDirectory(OutputDir);
        var safe = string.Join("-", fileStem.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
        var file = Path.Combine(OutputDir, $"{safe}-{DateTime.Now:yyyyMMdd-HHmm}.mp3");
        await using (var fs = File.Create(file))
            await res.Content.CopyToAsync(fs, ct);
        return file;
    }

    public IReadOnlyList<string> ListOutputs()
    {
        if (!Directory.Exists(OutputDir)) return [];
        return Directory.EnumerateFiles(OutputDir, "*.mp3").OrderByDescending(File.GetLastWriteTime).Take(20).ToList();
    }

    private IReadOnlyList<VoiceInfo>? _voices;

    public async Task<VoiceInfo?> DefaultVoiceAsync(CancellationToken ct = default)
    {
        if (!Configured) return null;
        _voices ??= await VoicesAsync(ct);
        return _voices.FirstOrDefault(v => AppPaths.VozPreferida.Length > 0 && v.Name.Contains(AppPaths.VozPreferida, StringComparison.OrdinalIgnoreCase))
               ?? _voices.FirstOrDefault(v => v.Category is "cloned" or "professional")
               ?? _voices.FirstOrDefault();
    }

    public string? FindOutput(string stemPrefix)
    {
        if (!Directory.Exists(OutputDir)) return null;
        var safe = string.Join("-", stemPrefix.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
        return Directory.EnumerateFiles(OutputDir, safe + "*.mp3").OrderByDescending(File.GetLastWriteTime).FirstOrDefault();
    }

    private static string Clip(string s, int n) => s.Length > n ? s[..n] + "…" : s;
}
