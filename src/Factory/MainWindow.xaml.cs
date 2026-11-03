using System.ComponentModel;
using Factory.Navigation;
using Factory.Services;
using Factory.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace Factory;

public sealed partial class MainWindow : Window
{
    private readonly ProjectCatalog _catalog;
    private readonly NavigationService _nav;
    private readonly PaletteViewModel _palette;

    /// <summary>Player unico do app (barra do rodape). x:Bind no XAML.</summary>
    public AudioPlayer Player { get; }

    public MainWindow()
    {
        _catalog = App.Services.GetRequiredService<ProjectCatalog>();
        _nav = App.Services.GetRequiredService<NavigationService>();
        _palette = App.Services.GetRequiredService<PaletteViewModel>();
        Player = App.Services.GetRequiredService<AudioPlayer>();

        InitializeComponent();
        Closed += (_, _) => Player.Dispose();
        Title = App.IsHomolog ? "ODIN · HOMOLOG" : "Factory";
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1440, 860));
        // Icone da barra de tarefas / alt-tab (o do .exe vem do ApplicationIcon no csproj).
        var ico = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        if (File.Exists(ico)) AppWindow.SetIcon(ico);

        // Sem Mica (Windows 10 / sessao remota): volta o fundo chapado do tema.
        if (!MicaController.IsSupported())
        {
            SystemBackdrop = null;
            Root.Background = (SolidColorBrush)Application.Current.Resources["CyberBg"];
        }

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarArea);

        var mono = (FontFamily)Application.Current.Resources["MonoFont"];
        var dim = (Brush)Application.Current.Resources["CyberDim"];
        string? grupoAtual = null;
        foreach (var (grupo, section) in SectionRegistry.Grouped())
        {
            // cabecalho de grupo: rotulo de canto, espacado e apagado (guia: CyberLabel 10, CyberDim)
            if (grupo != grupoAtual)
            {
                grupoAtual = grupo;
                Nav.MenuItems.Add(new NavigationViewItemHeader
                {
                    Content = new TextBlock
                    {
                        Text = grupo,
                        Style = (Style)Application.Current.Resources["CyberLabel"],
                        Foreground = dim,
                    },
                });
            }
            Nav.MenuItems.Add(new NavigationViewItem
            {
                Content = new TextBlock
                {
                    Text = section.Title.ToUpperInvariant(),
                    FontFamily = mono,
                    FontSize = 13,
                    CharacterSpacing = 100,
                },
                Tag = section.Tag,
                Icon = new FontIcon { Glyph = section.Glyph },
            });
        }

        Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().First();

        // bolinha de alertas do VIGIA (Seguranca) na barra lateral: so aparece com alerta nao visto
        var watch = App.Services.GetRequiredService<ISecurityWatch>();
        void Badge()
        {
            if (Nav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => (string?)i.Tag == "seguranca") is not { } it) return;
            var n = watch.NaoVistos;
            it.InfoBadge = n > 0 ? new InfoBadge { Value = n } : null;
        }
        watch.Mudou += (_, _) => DispatcherQueue.TryEnqueue(Badge);
        Badge();

        // "ir pro projeto X" vindo de qualquer pagina: troca pra PROJETOS; o ViewModel seleciona.
        _nav.ProjectRequested += (_, _) => SelectSection("projects");
        _nav.SectionRequested += (_, tag) => SelectSection(tag);

        // Palette: o ViewModel manda no IsOpen; o Popup segue.
        _palette.PropertyChanged += Palette_PropertyChanged;

        _catalog.Changed += (_, _) => UpdateTitle();
        _catalog.IndexProgress += (_, p) =>
            TitleIndex.Text = p.Length == 0 ? "" : $"· {p.ToUpperInvariant()}";
        UpdateTitle();
        _ = _catalog.ScanAsync();
    }

    private void SelectSection(string tag)
    {
        var item = Nav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => (string?)i.Tag == tag);
        if (item is not null) Nav.SelectedItem = item;
    }

    // ---------- player ----------

    private void PlayerToggle_Click(object sender, RoutedEventArgs e) => Player.Toggle();
    private void PlayerStop_Click(object sender, RoutedEventArgs e) => Player.Stop();

    private void PlayerToggle_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!Player.HasTrack) return;
        Player.Toggle();
        args.Handled = true;
    }

    private void PlayerBar_Tapped(object sender, TappedRoutedEventArgs e)
    {
        var bar = (FrameworkElement)sender;
        if (bar.ActualWidth <= 0) return;
        Player.Seek(e.GetPosition(bar).X / bar.ActualWidth);
    }

    // ---------- palette ----------

    private void Palette_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        _palette.Toggle();
        args.Handled = true;
    }

    private void Entrada_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        SelectSection("entrada");
        args.Handled = true;
    }

    private void Palette_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PaletteViewModel.IsOpen)) return;
        if (_palette.IsOpen)
        {
            PalettePopup.HorizontalOffset = Math.Max(0, (Root.ActualWidth - Palette.Width) / 2);
            PalettePopup.IsOpen = true;
        }
        else
        {
            PalettePopup.IsOpen = false;
        }
    }

    private void PalettePopup_Opened(object? sender, object e) => Palette.FocusQuery();

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    /// <summary>Atalho global: janela pra frente, secao ODIN, voz ligada (se a orb ja subiu; senao fica pendente).</summary>
    public void SummonOdin()
    {
        var h = WinRT.Interop.WindowNative.GetWindowHandle(this);
        ShowWindow(h, 9);
        keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero);   // toque de ALT libera o foreground
        SetForegroundWindow(h);
        SelectSection("chat");
        App.Services.GetRequiredService<ChatViewModel>().SummonVoice();
    }

    private void PalettePopup_Closed(object? sender, object e)
    {
        // light-dismiss fechou o popup: sincroniza o ViewModel
        if (_palette.IsOpen) _palette.Close();
    }

    // ---------- titulo / navegacao ----------

    private void UpdateTitle()
    {
        if (_catalog.IsScanning)
        {
            TitleStatus.Text = "▮ SCANNING";
            TitleStatus.Foreground = (SolidColorBrush)Application.Current.Resources["CyberDim"];
            return;
        }
        var n = _catalog.All.Count;
        var s = _catalog.All.Count(p => p.HasStatus);
        TitleStatus.Text = $"▮ {n} PROJETOS · {s} COM STATUS · {ProjectScanner.Root}";
        TitleStatus.Foreground = (SolidColorBrush)Application.Current.Resources["NeonGreen"];
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: string tag })
        {
            var section = SectionRegistry.Sections.First(s => s.Tag == tag);
            if (ContentFrame.CurrentSourcePageType == section.PageType) return;
            ContentFrame.Navigate(section.PageType, null, new DrillInNavigationTransitionInfo());
        }
    }
}
