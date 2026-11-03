using System.Net;
using System.Text;
using System.Text.Json;
using Factory.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Factory.Services;

/// <summary>
/// Local port for Odin's hands (online behaves exactly like offline).
/// A orb web, servida por web-orb\server.mjs atras de um tunel, manda cada ferramenta pra ca e o
/// Factory executa na maquina do usuario. So loopback (127.0.0.1) + token; quem fala com a internet e o Node.
///   GET  /vars           -> { catalogo, memoria, ultima, familia, hoje }
///   POST /hands/{nome}   -> { result }   (corpo = params JSON da ferramenta)
/// Token: %LOCALAPPDATA%\Factory\hands-token.txt (gerado uma vez; o Node le o mesmo arquivo).
/// </summary>
public sealed class HandsServer
{
    // FACTORY_HANDS_PORT: porta de teste pro homolog/debug nao brigar com a 8788 da PROD aberta
    public static readonly int Port = int.TryParse(Environment.GetEnvironmentVariable("FACTORY_HANDS_PORT"), out var p) ? p : 8788;
    private readonly HttpListener _http = new();
    private readonly string _token;

    public static string TokenFile => Path.Combine(AppPaths.Cache, "hands-token.txt");

    public HandsServer()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(TokenFile)!);
        if (!File.Exists(TokenFile)) File.WriteAllText(TokenFile, Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"));
        _token = File.ReadAllText(TokenFile).Trim();
    }

    public void Start()
    {
        try
        {
            _http.Prefixes.Add($"http://127.0.0.1:{Port}/");
            _http.Start();
            _ = Task.Run(LoopAsync);
        }
        catch (Exception) { /* porta ocupada (outra instancia): a outra atende */ }
    }

    private async Task LoopAsync()
    {
        while (_http.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await _http.GetContextAsync(); } catch (Exception) { break; }
            _ = Task.Run(() => HandleAsync(ctx));
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        var res = ctx.Response;
        try
        {
            if (ctx.Request.Headers["X-Odin-Token"] != _token) { res.StatusCode = 401; await Write(res, "{\"error\":\"token\"}"); return; }
            var path = ctx.Request.Url?.AbsolutePath ?? "/";
            var sp = App.Services;
            if (ctx.Request.HttpMethod == "GET" && path == "/vars")
            {
                var voice = sp.GetRequiredService<IVoiceAgent>();
                var catalog = sp.GetRequiredService<ProjectCatalog>();
                var vars = new
                {
                    catalogo = voice.BuildCatalog(catalog.All.ToList()),
                    memoria = voice.LoadMemory(),
                    ultima = voice.LoadLastTranscript(),
                    familia = voice.LoadFamily(),
                    hoje = DateTime.Now.ToString("dddd, dd/MM/yyyy HH:mm", new System.Globalization.CultureInfo("pt-BR")),
                };
                await Write(res, JsonSerializer.Serialize(vars));
                return;
            }
            if (ctx.Request.HttpMethod == "POST" && path.StartsWith("/hands/"))
            {
                var name = path["/hands/".Length..].Trim('/');
                using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
                var body = await reader.ReadToEndAsync();
                JsonElement args;
                try { args = body.Length > 0 ? JsonDocument.Parse(body).RootElement.Clone() : default; } catch (JsonException) { args = default; }
                var hands = sp.GetRequiredService<IOdinHands>();
                var result = await hands.RunAsync(name, args, CancellationToken.None, origem: "online");
                try
                {
                    var vm = sp.GetRequiredService<ChatViewModel>();
                    var first = result.Split('\n')[0];
                    App.Window?.DispatcherQueue.TryEnqueue(() => vm.Lines.Add(new ChatLine { Who = "FACTORY", Text = $"⚙ [online] {name}: {(first.Length > 140 ? first[..140] + "…" : first)}" }));
                }
                catch (Exception) { }
                await Write(res, JsonSerializer.Serialize(new { result }));
                return;
            }
            res.StatusCode = 404; await Write(res, "{\"error\":\"rota\"}");
        }
        catch (Exception ex)
        {
            try { res.StatusCode = 500; await Write(res, JsonSerializer.Serialize(new { error = ex.Message })); } catch (Exception) { }
        }
    }

    private static async Task Write(HttpListenerResponse res, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        res.ContentType = "application/json; charset=utf-8";
        res.ContentLength64 = bytes.Length;
        await res.OutputStream.WriteAsync(bytes);
        res.Close();
    }
}
