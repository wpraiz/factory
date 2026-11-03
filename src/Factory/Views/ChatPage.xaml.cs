using System.Collections.Specialized;
using System.Numerics;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Factory.Services;
using Factory.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Factory.Views;

public sealed partial class ChatPage : Page
{
    public ChatViewModel ViewModel { get; }

    public ChatPage()
    {
        ViewModel = App.Services.GetRequiredService<ChatViewModel>();
        InitializeComponent();
        Loaded += (_, _) => InputBox.Focus(FocusState.Programmatic);
        ViewModel.Lines.CollectionChanged += Lines_CollectionChanged;
        ViewModel.VoiceToggleRequested += Voice_ToggleRequested;
        ViewModel.OrbCommand += (_, c) => DispatcherQueue.TryEnqueue(async () =>
        {
            if (OrbView.CoreWebView2 is null || !_orbReady) return;
            try
            {
                var r = await OrbView.CoreWebView2.ExecuteScriptAsync($"window.factory && window.factory.{c.Name}({c.Arg})");
                if (c.Name == "mic") ViewModel.OnMicResult(r.Trim('"'));   // "ok" | "sem-sessao" | "sem-metodo"
            }
            catch (Exception) { /* orb fora do ar: o valor fica no ViewModel */ }
        });
        _voiceClock.Tick += (_, _) => ViewModel.TickVoice();
        App.Services.GetRequiredService<IOdinHands>().TaskFinished += (_, r) =>
            DispatcherQueue.TryEnqueue(async () =>
            {
                var ctx = ViewModel.OnTaskFinished(r.Task, r.Result, r.Ok);
                if (OrbView.CoreWebView2 is not null && _orbReady)
                    await OrbView.CoreWebView2.ExecuteScriptAsync($"window.factory && window.factory.contexto({JsonSerializer.Serialize(ctx)})");
            });
        App.Services.GetRequiredService<IOdinHands>().TaskProgress += (_, r) =>
            DispatcherQueue.TryEnqueue(async () =>
            {
                var ctx = ViewModel.OnTaskProgress(r.Task, r.Msg);
                if (ctx is not null && OrbView.CoreWebView2 is not null && _orbReady)
                    await OrbView.CoreWebView2.ExecuteScriptAsync($"window.factory && window.factory.contexto({JsonSerializer.Serialize(ctx)})");
            });
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(ChatViewModel.IsVoiceOn)) return;
            if (ViewModel.IsVoiceOn) _voiceClock.Start(); else _voiceClock.Stop();
            LightEdge(ViewModel.IsVoiceOn);
        };
        _ = InitOrbAsync();
        ViewModel.PropertyChanged += (_, e) =>
        {
            // quando o claude termina, a caixa volta a habilitar: foco de volta nela
            if (e.PropertyName == nameof(ChatViewModel.IsBusy) && !ViewModel.IsBusy)
                DispatcherQueue.TryEnqueue(() => InputBox.Focus(FocusState.Programmatic));
        };
    }

    /// <summary>Toda linha nova do papo (texto, voz, ferramenta) vira uma fala no ceu do orb.</summary>
    private void Lines_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || e.NewItems is null) return;
        foreach (var it in e.NewItems) if (it is ChatLine l) _ = PushLineAsync(l.Who, l.Text, false);
    }

    private async Task PushLineAsync(string who, string text, bool update)
    {
        if (OrbView.CoreWebView2 is null || !_orbReady) return;
        var args = JsonSerializer.Serialize(new object[] { who, text, update });
        try { await OrbView.CoreWebView2.ExecuteScriptAsync($"window.factory && window.factory.linha(...{args})"); }
        catch (Exception) { /* ceu indisponivel: a linha continua no ViewModel */ }
    }

    // ---------- movimento: a aresta do ceu acende/apaga em 260 ms (so o que muda de estado se move) ----------

    private void LightEdge(bool on)
    {
        var v = ElementCompositionPreview.GetElementVisual(VoiceEdge);
        var c = v.Compositor;
        var a = c.CreateScalarKeyFrameAnimation();
        a.InsertKeyFrame(1f, on ? 0.55f : 0f, c.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f)));
        a.Duration = TimeSpan.FromMilliseconds(260);
        v.StartAnimation("Opacity", a);
    }

    // ---------- orb (WebView2) ----------

    private const string OrbHost = "factory.orb";
    private bool _orbReady;
    private string? _orbFailed;   // mensagem real do boot, se o WebView2 nao subiu
    private readonly DispatcherTimer _voiceClock = new() { Interval = TimeSpan.FromSeconds(1) };

    /// <summary>Sobe o WebView2 com a pasta Assets\orb num host virtual (file:// nao libera microfone nem modulos ES).</summary>
    private async Task InitOrbAsync()
    {
        try
        {
            var voice = App.Services.GetRequiredService<IVoiceAgent>();
            await OrbView.EnsureCoreWebView2Async();
            var core = OrbView.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.SetVirtualHostNameToFolderMapping(OrbHost, voice.OrbDir, CoreWebView2HostResourceAccessKind.Allow);
            core.PermissionRequested += (_, e) =>
            {
                // So o microfone, so pra nossa pagina. O resto continua negado.
                // Camera so pro protocolo de acordar (movimento, local, nenhum frame sai do PC).
                e.State = (e.PermissionKind == CoreWebView2PermissionKind.Microphone || e.PermissionKind == CoreWebView2PermissionKind.Camera) && e.Uri.StartsWith($"https://{OrbHost}/", StringComparison.OrdinalIgnoreCase)
                    ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
            };
            core.WebMessageReceived += Orb_MessageReceived;
            core.Navigate($"https://{OrbHost}/index.html");
        }
        catch (Exception ex)
        {
            _orbFailed = "WebView2 nao subiu (reabra o app; se repetir, instale o WebView2 Runtime): " + ex.Message;
            ViewModel.OnVoiceStatus("error", _orbFailed);
            ViewModel.OrbReady = false;
        }
    }

    private async void Orb_MessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.TryGetWebMessageAsString());
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString();
            switch (type)
            {
                case "ready":
                    _orbReady = true;
                    ViewModel.OrbReady = true;
                    if (!App.Services.GetRequiredService<IVoiceAgent>().Configured)
                        await sender.ExecuteScriptAsync("window.factory && window.factory.disable()");
                    foreach (var l in ViewModel.Lines.TakeLast(6)) await PushLineAsync(l.Who, l.Text, false);
                    break;
                case "tool":
                {
                    var id = root.GetProperty("id").GetInt32();
                    var name = root.GetProperty("name").GetString() ?? "";
                    var args = root.TryGetProperty("params", out var pa) ? pa.Clone() : default;
                    var conv = root.TryGetProperty("conv", out var cv) && cv.ValueKind == JsonValueKind.String ? cv.GetString() : null;
                    var quando = root.TryGetProperty("t", out var tv) && tv.ValueKind == JsonValueKind.Number ? tv.GetInt64() : 0;
                    var episode = conv is null ? null : $"{conv}#{name}@{quando}";
                    var result = await ViewModel.RunHandAsync(name, args, conv, episode);
                    await sender.ExecuteScriptAsync($"window.factory && window.factory.toolResult({id}, {JsonSerializer.Serialize(result)})");
                    break;
                }
                case "want-start":
                    await StartVoiceAsync();
                    break;
                case "metric":
                    ViewModel.OnMetric(root.GetProperty("kind").GetString() ?? "", root.GetProperty("ms").GetInt32());
                    break;
                case "status":
                    ViewModel.OnVoiceStatus(root.GetProperty("status").GetString() ?? "idle", root.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "");
                    break;
                case "message":
                {
                    var text = root.GetProperty("text").GetString() ?? "";
                    var updated = ViewModel.OnVoiceMessage(root.GetProperty("who").GetString() ?? "agente", text,
                        root.TryGetProperty("update", out var u) && u.GetBoolean());
                    if (updated) await PushLineAsync("ODIN", text, true);   // mesma fala, versao nova: nao cria estrela
                    break;
                }
            }
        }
        catch (Exception) { /* mensagem fora do contrato: ignora */ }
    }

    // Segunda trava, do lado do C# (defesa em profundidade da mesma falha: duas conversas
    // reais abertas ao mesmo tempo na mesma agente, uma orfa sem controle). O `starting` do JS ja deveria
    // bastar; isto cobre o caso de dois gatilhos C# (clique + Ctrl+Alt+O) chegarem quase juntos.
    private bool _startingVoice;

    /// <summary>Pede URL assinada + catalogo ao ViewModel e abre a sessao no JS. Daqui em diante gasta minutos.</summary>
    private async Task StartVoiceAsync()
    {
        if (_startingVoice || ViewModel.IsVoiceOn) return;
        _startingVoice = true;
        (string SignedUrl, string Catalogo, string Memoria, string Hoje, string Ultima, string? Saudacao, string Familia)? s;
        try { s = await ViewModel.StartVoiceAsync(); }
        finally { _startingVoice = false; }
        if (s is null || OrbView.CoreWebView2 is null) return;
        var payload = JsonSerializer.Serialize(new { signedUrl = s.Value.SignedUrl, catalogo = s.Value.Catalogo, memoria = s.Value.Memoria, hoje = s.Value.Hoje, ultima = s.Value.Ultima, saudacao = s.Value.Saudacao, familia = s.Value.Familia });
        await OrbView.CoreWebView2.ExecuteScriptAsync($"window.factory && window.factory.start({payload})");
    }

    private async void Voice_ToggleRequested(object? sender, bool on)
    {
        if (_orbFailed is not null) { ViewModel.OnVoiceStatus("error", _orbFailed); return; }
        if (OrbView.CoreWebView2 is null || !_orbReady) { ViewModel.OnVoiceStatus("error", "orb ainda carregando (2-3 s apos abrir a tela)"); return; }
        if (on) await StartVoiceAsync();
        else await OrbView.CoreWebView2.ExecuteScriptAsync("window.factory && window.factory.stop()");
    }

    private void InputBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Down:
                ViewModel.Move(+1);
                SuggestionList.ScrollIntoView(SuggestionList.SelectedItem);
                e.Handled = true;
                break;
            case VirtualKey.Up:
                ViewModel.Move(-1);
                SuggestionList.ScrollIntoView(SuggestionList.SelectedItem);
                e.Handled = true;
                break;
            case VirtualKey.Enter:
                // Shift+Enter quebra linha; Enter envia
                if (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
                        .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)) break;
                _ = ViewModel.SubmitCommand.ExecuteAsync(null);
                e.Handled = true;
                break;
            case VirtualKey.Escape:
                ViewModel.Escape();
                e.Handled = true;
                break;
        }
    }

    private void Brain_TextSubmitted(ComboBox sender, ComboBoxTextSubmittedEventArgs args)
    {
        ViewModel.SubmitBrain(args.Text);
        args.Handled = true;
    }

    private void Suggestion_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is CommandHit hit)
        {
            ViewModel.Run(hit);
            InputBox.Focus(FocusState.Programmatic);
        }
    }
}
