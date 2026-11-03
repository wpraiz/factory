using System.Diagnostics;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Factory.Services;

/// <summary>
/// Mao `obs` do Odin: fala direto com o obs-websocket v5 (embutido no OBS 28+, porta 4455), sem Claude Code e
/// sem clicar na tela. o `fazer` no OBS por windows-mcp morreu 5 vezes em 324-480 s; por aqui cada
/// comando leva ~5 ms. Senha e porta vem do proprio global.ini do OBS (nada de segredo duplicado).
/// Nunca transmite ao vivo: transmitir e publicar (portao irreversivel) e fica com ele.
/// </summary>
public static class ObsHands
{
    private static readonly string Ini = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "obs-studio", "global.ini");
    private const string Exe = @"C:\Program Files\obs-studio\bin\64bit\obs64.exe";
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static ClientWebSocket? _ws;
    private static int _reqId;

    public static async Task<string> RunAsync(string acao, string nome, CancellationToken ct)
    {
        var a = CommandLine.Fold((acao ?? "").Trim());
        if (a is "transmitir" or "live" or "stream" or "iniciar transmissao")
            return "Nao transmito ao vivo: isso publica pra fora. Diga a ele pra clicar em Iniciar transmissao no OBS.";
        await Gate.WaitAsync(ct);
        try
        {
            var aberto = await GarantirAsync(ct);
            if (aberto is not null) return aberto;
            _arquivo = null;
            var res = await Acao(a, acao ?? "", nome, ct);
            // proof contract: so em sucesso, estado RELIDO do OBS (e do disco) vira "[prova] {json}" -> evidencia confiavel
            return res.StartsWith("Feito") || res.StartsWith("Cena no ar") || res.StartsWith("OBS:") || res.StartsWith("Perfil atual") || res.StartsWith("OBS aberto")
                ? res + "\n" + await Prova(a, ct)
                : res;
        }
        catch (ObsError ex) { return $"O OBS recusou: {ex.Message}"; }
        catch (Exception ex) { Fechar(); return $"A mao falhou: nao consegui falar com o OBS ({ex.Message})."; }
        finally { Gate.Release(); }
    }

    private static string? _arquivo;

    private static async Task<string> Prova(string acao, CancellationToken ct)
    {
        var cena = (await Req("GetCurrentProgramScene", null, ct)).GetProperty("currentProgramSceneName").GetString();
        var perfil = (await Req("GetProfileList", null, ct)).GetProperty("currentProfileName").GetString();
        var rec = await Req("GetRecordStatus", null, ct);
        var fi = _arquivo is null ? null : new FileInfo(_arquivo);
        return "[prova] " + JsonSerializer.Serialize(new
        {
            mao = "obs", acao, cena, perfil, gravando = rec.GetProperty("outputActive").GetBoolean(), pausada = rec.GetProperty("outputPaused").GetBoolean(),
            arquivo = _arquivo, arquivo_bytes = fi is { Exists: true } ? fi.Length : (long?)null, lido_em = DateTimeOffset.Now.ToString("o"),
        });
    }

    private static async Task<string> Acao(string a, string acao, string nome, CancellationToken ct)
    {
        {
            switch (a)
            {
                case "cenas" or "listar" or "":
                    {
                        var r = await Req("GetSceneList", null, ct);
                        var atual = r.GetProperty("currentProgramSceneName").GetString();
                        var nomes = r.GetProperty("scenes").EnumerateArray().Select(s => s.GetProperty("sceneName").GetString()).Reverse();
                        return $"Cena no ar: {atual}. Cenas: {string.Join(", ", nomes)}.";
                    }
                case "cena" or "trocar" or "trocar cena":
                    {
                        var r = await Req("GetSceneList", null, ct);
                        var cenas = r.GetProperty("scenes").EnumerateArray().Select(s => s.GetProperty("sceneName").GetString() ?? "").ToList();
                        var alvo = Melhor(nome, cenas);
                        if (alvo is null) return $"Nao achei cena parecida com '{nome}'. Cenas: {string.Join(", ", cenas)}.";
                        await Req("SetCurrentProgramScene", new { sceneName = alvo }, ct);
                        return $"Feito: cena '{alvo}' no ar.";
                    }
                case "gravar" or "iniciar gravacao" or "rec":
                    {
                        // Prova, nao declaracao: o OBS tem que dizer que a saida esta ativa depois do Start.
                        await Req("StartRecord", null, ct);
                        await Task.Delay(700, ct);
                        var st = await Req("GetRecordStatus", null, ct);
                        var dir = (await Req("GetRecordDirectory", null, ct)).GetProperty("recordDirectory").GetString();
                        return st.GetProperty("outputActive").GetBoolean()
                            ? $"Feito: gravando so local (nada ao vivo), em {dir}. Bytes ate agora: {st.GetProperty("outputBytes").GetInt64()}."
                            : "O OBS aceitou o comando mas a gravacao NAO ficou ativa. Nao diga que esta gravando.";
                    }
                case "parar" or "parar gravacao" or "stop":
                    {
                        var r = await Req("StopRecord", null, ct);
                        var arq = r.TryGetProperty("outputPath", out var p) ? p.GetString() : null;
                        if (arq is null) return "Gravacao parada, mas o OBS nao disse o arquivo.";
                        // o OBS fecha o arquivo depois de responder: espera ate ~6 s pelo tamanho no disco
                        var fi = new FileInfo(arq);
                        for (var i = 0; i < 20 && !(fi.Exists && fi.Length > 0); i++) { await Task.Delay(300, ct); fi.Refresh(); }
                        _arquivo = arq;
                        return fi.Exists && fi.Length > 0
                            ? $"Feito: gravacao parada. Arquivo {arq}, {fi.Length / 1024} KB (conferido no disco)."
                            : $"Gravacao parada, mas o arquivo {arq} NAO existe ou esta vazio no disco. Nao diga que gravou.";
                    }
                case "pausar":
                    await Req("PauseRecord", null, ct); return "Feito: gravacao pausada.";
                case "retomar" or "continuar":
                    await Req("ResumeRecord", null, ct); return "Feito: gravacao retomada.";
                case "status" or "estado":
                    {
                        var rec = await Req("GetRecordStatus", null, ct);
                        var cena = (await Req("GetCurrentProgramScene", null, ct)).GetProperty("currentProgramSceneName").GetString();
                        var perfil = (await Req("GetProfileList", null, ct)).GetProperty("currentProfileName").GetString();
                        var grav = rec.GetProperty("outputActive").GetBoolean()
                            ? $"gravando ha {rec.GetProperty("outputTimecode").GetString()}{(rec.GetProperty("outputPaused").GetBoolean() ? " (pausada)" : "")}"
                            : "sem gravar";
                        return $"OBS: cena '{cena}', perfil '{perfil}', {grav}.";
                    }
                case "perfis":
                    {
                        var r = await Req("GetProfileList", null, ct);
                        return $"Perfil atual: {r.GetProperty("currentProfileName").GetString()}. Perfis: {string.Join(", ", r.GetProperty("profiles").EnumerateArray().Select(x => x.GetString()))}.";
                    }
                case "perfil":
                    {
                        var r = await Req("GetProfileList", null, ct);
                        var alvo = Melhor(nome, r.GetProperty("profiles").EnumerateArray().Select(x => x.GetString() ?? "").ToList());
                        if (alvo is null) return $"Nao achei perfil parecido com '{nome}'.";
                        await Req("SetCurrentProfile", new { profileName = alvo }, ct);
                        return $"Feito: perfil '{alvo}'.";
                    }
                case "mostrar" or "esconder":
                    {
                        var cena = (await Req("GetCurrentProgramScene", null, ct)).GetProperty("currentProgramSceneName").GetString();
                        var itens = (await Req("GetSceneItemList", new { sceneName = cena }, ct)).GetProperty("sceneItems").EnumerateArray()
                            .Select(i => (Id: i.GetProperty("sceneItemId").GetInt32(), Nome: i.GetProperty("sourceName").GetString() ?? "")).ToList();
                        var alvo = Melhor(nome, itens.Select(i => i.Nome).ToList());
                        if (alvo is null) return $"Nao achei fonte '{nome}' na cena '{cena}'. Fontes: {string.Join(", ", itens.Select(i => i.Nome))}.";
                        await Req("SetSceneItemEnabled", new { sceneName = cena, sceneItemId = itens.First(i => i.Nome == alvo).Id, sceneItemEnabled = a == "mostrar" }, ct);
                        return $"Feito: fonte '{alvo}' {(a == "mostrar" ? "visivel" : "escondida")} na cena '{cena}'.";
                    }
                case "abrir":
                    return "OBS aberto e conectado.";
                case "montar" or "layout" or "montar cena":
                    return await MontarLayout(string.IsNullOrWhiteSpace(nome) ? "Odin + Terminais" : nome.Trim(), ct);
                default:
                    return $"Nao conheco a acao '{acao}'. Aceito: cenas, cena, gravar, parar, pausar, retomar, status, perfis, perfil, mostrar, esconder, abrir.";
            }
        }
    }

    // Layout scene (Odin on the left, up to 4 terminals on the right): cria/refaz a cena com
    // captura de janela (WGC, pega WinUI e terminal), Odin na metade esquerda e ate 4 terminais em grade 2x2 a direita.
    // NUNCA troca a cena no ar: ele pode estar gravando. Quem poe no ar e ele (ou `cena`).
    private static async Task<string> MontarLayout(string cena, CancellationToken ct)
    {
        var video = await Req("GetVideoSettings", null, ct);
        int W = video.GetProperty("baseWidth").GetInt32(), H = video.GetProperty("baseHeight").GetInt32();
        var cenas = (await Req("GetSceneList", null, ct)).GetProperty("scenes").EnumerateArray().Select(s => s.GetProperty("sceneName").GetString()).ToList();
        var noAr = (await Req("GetCurrentProgramScene", null, ct)).GetProperty("currentProgramSceneName").GetString();
        if (cenas.Contains(cena))
        {
            if (cena == noAr) return $"Nao refiz '{cena}': ela esta no ar agora. Troque de cena antes.";
            foreach (var it in (await Req("GetSceneItemList", new { sceneName = cena }, ct)).GetProperty("sceneItems").EnumerateArray())
                await Req("RemoveSceneItem", new { sceneName = cena, sceneItemId = it.GetProperty("sceneItemId").GetInt32() }, ct);
        }
        else await Req("CreateScene", new { sceneName = cena }, ct);

        // Captura de MONITOR, nao de janela: o titulo dos terminais muda a cada segundo (spinner ◐◑✳) e os 4 sao o
        // mesmo WindowsTerminal.exe, entao captura por janela se perde ou repete o mesmo terminal. Default two-monitor
        // layout: Odin on monitor 2, terminals side by side on monitor 1: esquerda = monitor 2, direita = monitor 1.
        var postos = new List<string>();
        var lados = new[] { (Rotulo: "Odin (monitor 2)", Monitor: "2", X: 0), (Rotulo: "Terminais (monitor 1)", Monitor: "1", X: W / 2) };
        foreach (var (rotulo, monitor, x) in lados)
        {
            var r = WindowsHands.MonitorRect(monitor);
            if (r is null) { postos.Add($"{rotulo}: monitor nao existe"); continue; }
            var fonte = $"{cena} · {rotulo}";
            await RemoverFonteSeExiste(fonte, ct);
            var item = (await Req("CreateInput", new { sceneName = cena, inputName = fonte, inputKind = "monitor_capture", inputSettings = new { capture_cursor = false } }, ct)).GetProperty("sceneItemId").GetInt32();
            // o OBS nomeia "BenQ...: 1920x1080 @ 0,0"; casa pela posicao, que e a numeracao do Factory
            var alvo = (await Req("GetInputPropertiesListPropertyItems", new { inputName = fonte, propertyName = "monitor_id" }, ct)).GetProperty("propertyItems").EnumerateArray()
                .Select(o => (Nome: o.GetProperty("itemName").GetString() ?? "", Valor: o.GetProperty("itemValue").GetString() ?? ""))
                .FirstOrDefault(o => o.Nome.Contains($"@ {r.Value.L},{r.Value.T}"));
            if (string.IsNullOrEmpty(alvo.Valor)) { postos.Add($"{rotulo}: OBS nao listou o monitor em {r.Value.L},{r.Value.T}"); continue; }
            await Req("SetInputSettings", new { inputName = fonte, inputSettings = new { monitor_id = alvo.Valor, capture_cursor = false } }, ct);
            await Req("SetSceneItemTransform", new { sceneName = cena, sceneItemId = item, sceneItemTransform = new { positionX = x, positionY = 0, boundsType = "OBS_BOUNDS_SCALE_INNER", boundsWidth = W / 2, boundsHeight = H, boundsAlignment = 0 } }, ct);
            postos.Add(rotulo);
        }
        var itens = (await Req("GetSceneItemList", new { sceneName = cena }, ct)).GetProperty("sceneItems").GetArrayLength();
        return $"Feito: cena '{cena}' montada com {itens} capturas ({string.Join("; ", postos)}). NAO pus no ar (no ar segue '{noAr}'); pra trocar: obs cena '{cena}'.";
    }

    private static async Task RemoverFonteSeExiste(string fonte, CancellationToken ct)
    {
        var existe = (await Req("GetInputList", null, ct)).GetProperty("inputs").EnumerateArray().Any(i => i.GetProperty("inputName").GetString() == fonte);
        if (existe) await Req("RemoveInput", new { inputName = fonte }, ct);
    }

    // Abre o OBS (minimizado na bandeja) se nao estiver rodando e conecta. Devolve texto de erro ou null se ok.
    private static async Task<string?> GarantirAsync(CancellationToken ct)
    {
        if (_ws is { State: WebSocketState.Open }) return null;
        Fechar();
        var (enabled, port, pw) = LerIni();
        if (!enabled) return "O servidor WebSocket do OBS esta desligado (Ferramentas > Configuracoes do servidor WebSocket). Peca pra ele ligar.";
        if (!Process.GetProcessesByName("obs64").Any())
        {
            if (!File.Exists(Exe)) return "OBS nao esta instalado em Program Files.";
            Process.Start(new ProcessStartInfo(Exe, "--minimize-to-tray --disable-shutdown-check") { WorkingDirectory = Path.GetDirectoryName(Exe)!, UseShellExecute = true });
        }
        // o OBS leva ~2-6 s pra abrir a porta depois de iniciar
        for (var i = 0; i < 30; i++)
        {
            try { await Conectar(port, pw, ct); return null; }
            catch (Exception) when (i < 29) { Fechar(); await Task.Delay(500, ct); }
        }
        return "Nao consegui conectar no OBS.";
    }

    private static (bool Enabled, int Port, string? Pw) LerIni()
    {
        if (!File.Exists(Ini)) return (false, 4455, null);
        var sec = Regex.Match(File.ReadAllText(Ini), @"\[OBSWebSocket\]([^\[]*)").Groups[1].Value;
        string? V(string k) => Regex.Match(sec, $@"^{k}=(.*)$", RegexOptions.Multiline).Groups[1].Value.Trim() is { Length: > 0 } s ? s : null;
        return (V("ServerEnabled") == "true", int.TryParse(V("ServerPort"), out var p) ? p : 4455, V("ServerPassword"));
    }

    private static async Task Conectar(int port, string? pw, CancellationToken ct)
    {
        var ws = new ClientWebSocket();
        ws.Options.AddSubProtocol("obswebsocket.json");
        using var cto = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cto.CancelAfter(TimeSpan.FromSeconds(3));
        await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}"), cto.Token);
        using var hello = await Receber(ws, cto.Token);
        string? auth = null;
        if (hello.RootElement.GetProperty("d").TryGetProperty("authentication", out var a))
        {
            if (pw is null) throw new InvalidOperationException("OBS pede senha e o global.ini nao tem");
            auth = Sha(Sha(pw + a.GetProperty("salt").GetString()) + a.GetProperty("challenge").GetString());
        }
        await Enviar(ws, new { op = 1, d = new { rpcVersion = 1, authentication = auth, eventSubscriptions = 0 } }, cto.Token);
        using var ident = await Receber(ws, cto.Token);
        if (ident.RootElement.GetProperty("op").GetInt32() != 2) throw new InvalidOperationException("OBS recusou a identificacao");
        _ws = ws;
    }

    private static async Task<JsonElement> Req(string type, object? data, CancellationToken ct)
    {
        var ws = _ws ?? throw new InvalidOperationException("sem conexao");
        var id = Interlocked.Increment(ref _reqId).ToString();
        using var cto = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cto.CancelAfter(TimeSpan.FromSeconds(8));
        await Enviar(ws, new { op = 6, d = new { requestType = type, requestId = id, requestData = data } }, cto.Token);
        while (true)
        {
            using var doc = await Receber(ws, cto.Token);
            var d = doc.RootElement.GetProperty("d");
            if (doc.RootElement.GetProperty("op").GetInt32() != 7 || d.GetProperty("requestId").GetString() != id) continue;
            var st = d.GetProperty("requestStatus");
            if (!st.GetProperty("result").GetBoolean())
                throw new ObsError(st.TryGetProperty("comment", out var c) ? c.GetString() ?? type : $"{type} codigo {st.GetProperty("code").GetInt32()}");
            return d.TryGetProperty("responseData", out var rd) ? rd.Clone() : default;
        }
    }

    private static Task Enviar(ClientWebSocket ws, object msg, CancellationToken ct) =>
        ws.SendAsync(JsonSerializer.SerializeToUtf8Bytes(msg), WebSocketMessageType.Text, true, ct);

    private static async Task<JsonDocument> Receber(ClientWebSocket ws, CancellationToken ct)
    {
        var buf = new byte[16384];
        using var ms = new MemoryStream();
        WebSocketReceiveResult r;
        do
        {
            r = await ws.ReceiveAsync(buf, ct);
            if (r.MessageType == WebSocketMessageType.Close) throw new InvalidOperationException("OBS fechou a conexao");
            ms.Write(buf, 0, r.Count);
        } while (!r.EndOfMessage);
        return JsonDocument.Parse(ms.ToArray());
    }

    private static string Sha(string s) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(s)));

    private static void Fechar()
    {
        try { _ws?.Abort(); _ws?.Dispose(); } catch (Exception) { }
        _ws = null;
    }

    // Casa o nome falado com a lista (ASR erra acento e caixa): igual > contem > contido.
    private static string? Melhor(string falado, List<string> opcoes)
    {
        var q = CommandLine.Fold((falado ?? "").Trim());
        if (q.Length == 0) return null;
        return opcoes.FirstOrDefault(o => CommandLine.Fold(o) == q)
            ?? opcoes.FirstOrDefault(o => CommandLine.Fold(o).Contains(q))
            ?? opcoes.FirstOrDefault(o => q.Contains(CommandLine.Fold(o)));
    }

    private sealed class ObsError(string msg) : Exception(msg);
}
