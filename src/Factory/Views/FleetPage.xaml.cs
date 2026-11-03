using Factory.Models;
using Factory.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Factory.Views;

public sealed partial class FleetPage : Page
{
    public FleetViewModel ViewModel { get; }

    public FleetPage()
    {
        ViewModel = App.Services.GetRequiredService<FleetViewModel>();
        InitializeComponent();
        ViewModel.Rebuild();
        Loaded += async (_, _) => await IniciarSquadAsync();
        Unloaded += (_, _) => _squadClock.Stop();
    }

    // ---------- squad do Odin (WebView2 com squad.html, mesmo host virtual da orb) ----------
    private readonly DispatcherTimer _squadClock = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _squadPronto;

    private async Task IniciarSquadAsync()
    {
        try
        {
            if (!_squadPronto)
            {
                await SquadView.EnsureCoreWebView2Async();
                var core = SquadView.CoreWebView2;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.SetVirtualHostNameToFolderMapping("factory.orb", App.Services.GetRequiredService<Services.IVoiceAgent>().OrbDir,
                    Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
                core.Navigate("https://factory.orb/squad.html");
                _squadClock.Tick += (_, _) => EnviarSquad();
                _squadPronto = true;
            }
            _squadClock.Start();
        }
        catch (Exception) { /* sem WebView2: a FROTA continua com a lista */ }
    }

    /// <summary>"Trabalhe SO dentro da pasta ... (projeto X) ... Tarefa: Y" vira "X · Y" (o nome do projeto e o que importa no card).</summary>
    private static string Rotulo(string t)
    {
        var m = System.Text.RegularExpressions.Regex.Match(t, @"\(projeto ([^)]+)\).*?Tarefa:\s*(.*)$");
        var r = m.Success ? $"{m.Groups[1].Value} · {m.Groups[2].Value}" : t;
        return r.Length > 70 ? r[..70] + "…" : r;
    }

    private void EnviarSquad()
    {
        if (SquadView.CoreWebView2 is null) return;
        var watch = App.Services.GetRequiredService<Services.ISecurityWatch>();
        var agentes = Services.OdinHands.TrabalhandoAgora().Select(Rotulo).ToArray();
        var json = System.Text.Json.JsonSerializer.Serialize(new { agentes, vigia = watch.Estado, vigiaAtivo = watch.Ligado });
        SquadView.CoreWebView2.PostWebMessageAsJson(json);
    }

    private void Resume_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: FleetRow row }) ViewModel.Resume(row);
    }

    private void Launch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: FleetRow row }) ViewModel.Launch(row);
    }

    private void Terminal_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: FleetRow row }) ViewModel.Terminal(row);
    }

    private void Folder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: FleetRow row }) ViewModel.Folder(row);
    }
}
