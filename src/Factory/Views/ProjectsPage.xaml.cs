using System.ComponentModel;
using Factory.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;

namespace Factory.Views;

public sealed partial class ProjectsPage : Page
{
    public ProjectsViewModel ViewModel { get; }

    private bool _panning;
    private Point _panStart;
    private double _hStart, _vStart;
    private bool _fitted;
    private Border[] _cards = [];

    public ProjectsPage()
    {
        ViewModel = App.Services.GetRequiredService<ProjectsViewModel>();
        InitializeComponent();
        ProjectsCvs.Source = ViewModel.Groups;

        _cards = [CardCenter, CardSystem, CardFolder, CardOwn, CardDb, CardMentions, CardManifest, CardStatus, CardDocs, CardBriefing];
        var edge = (SolidColorBrush)Application.Current.Resources["CyberPanelEdge"];
        var hot = (SolidColorBrush)Application.Current.Resources["NeonCyan"];
        foreach (var c in _cards)
        {
            // Hover: a borda acende; o centro ja e acentuado e nao muda.
            if (c == CardCenter) continue;
            c.PointerEntered += (s, _) => ((Border)s).BorderBrush = hot;
            c.PointerExited += (s, _) => ((Border)s).BorderBrush = edge;
        }

        // fios: (linha, de, para). Recalculados quando qualquer card muda de tamanho (conteudo via x:Bind).
        _wires =
        [
            (WireFolder, CardCenter, CardFolder), (WireDb, CardCenter, CardDb), (WireManifest, CardCenter, CardManifest),
            (WireOwn, CardCenter, CardOwn), (WireMentions, CardCenter, CardMentions), (WireDocs, CardCenter, CardDocs),
            (WireStatus, CardCenter, CardStatus), (WireSystem, CardCenter, CardSystem), (WireBriefing, CardStatus, CardBriefing),
        ];
        foreach (var c in _cards) c.SizeChanged += (_, _) => Rewire();

        Loaded += (_, _) => { Rewire(); if (!_fitted) Fit(); if (ViewModel.Selected is not null) Ignite(); };
        Viewport.SizeChanged += (_, _) => { if (!_fitted) Fit(); };
        ViewModel.PropertyChanged += OnVmChanged;
        Unloaded += (_, _) => ViewModel.PropertyChanged -= OnVmChanged;
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ProjectsViewModel.Selected) || ViewModel.Selected is null) return;
        Ignite();
        if (!_fitted) Fit();
    }

    private (Microsoft.UI.Xaml.Shapes.Line Wire, Border From, Border To)[] _wires = [];

    /// <summary>Liga cada fio ao centro real dos dois cards (Canvas.Left/Top + tamanho medido). Fios ficam atras dos cards, entao o trecho interno some.</summary>
    private void Rewire()
    {
        foreach (var (wire, from, to) in _wires)
        {
            var a = CenterOf(from);
            var b = CenterOf(to);
            wire.X1 = a.X; wire.Y1 = a.Y; wire.X2 = b.X; wire.Y2 = b.Y;
        }
    }

    private static Point CenterOf(Border card) =>
        new(Canvas.GetLeft(card) + card.ActualWidth / 2, Canvas.GetTop(card) + card.ActualHeight / 2);

    /// <summary>Troca de projeto: os cards sobem e acendem em cascata, a partir do centro; os fios so acendem
    /// depois que o ultimo card pousou (enquanto sobem, o fio apontaria pro lugar errado).</summary>
    private void Ignite()
    {
        var sb = new Storyboard();
        var settle = TimeSpan.FromMilliseconds(_cards.Length * 45 + 200);
        foreach (var (wire, _, _) in _wires)
        {
            var glow = new DoubleAnimation { From = 0, To = 1, BeginTime = settle, Duration = new Duration(TimeSpan.FromMilliseconds(320)), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(glow, wire);
            Storyboard.SetTargetProperty(glow, "Opacity");
            sb.Children.Add(glow);
        }
        for (var i = 0; i < _cards.Length; i++)
        {
            var card = _cards[i];
            var delay = TimeSpan.FromMilliseconds(i * 45);
            var dur = new Duration(TimeSpan.FromMilliseconds(320));
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            var fade = new DoubleAnimation { From = 0, To = 1, BeginTime = delay, Duration = dur, EasingFunction = ease };
            Storyboard.SetTarget(fade, card);
            Storyboard.SetTargetProperty(fade, "Opacity");
            sb.Children.Add(fade);

            var rise = new DoubleAnimation { From = 28, To = 0, BeginTime = delay, Duration = dur, EasingFunction = ease };
            Storyboard.SetTarget(rise, card);
            Storyboard.SetTargetProperty(rise, "(UIElement.RenderTransform).(TranslateTransform.Y)");
            sb.Children.Add(rise);
        }
        sb.Begin();
    }

    // ---------- sistema ----------

    private async void Kill_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Factory.Models.LiveProcess proc }) return;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Encerrar processo?",
            Content = $"{proc.Name}  PID {proc.Pid}\n\n{proc.CommandLine}",
            PrimaryButtonText = "ENCERRAR",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            ViewModel.KillProcess(proc);
    }

    // ---------- briefing → STATUS.md ----------

    private async void SaveStatus_Click(object sender, RoutedEventArgs e)
    {
        var preview = ViewModel.BuildStatusPreview();
        if (preview.Length == 0) return;
        var exists = ViewModel.StatusFileExists(out var updated);

        var box = new TextBox
        {
            Text = preview, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            FontFamily = (FontFamily)Application.Current.Resources["MonoFont"], FontSize = 12,
            MinWidth = 640, MinHeight = 320, MaxHeight = 520,
        };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = exists ? $"Sobrescrever STATUS.md? (atual de {updated})" : "Criar STATUS.md",
            Content = new ScrollViewer { Content = box },
            PrimaryButtonText = exists ? "SOBRESCREVER (faz .bak)" : "GRAVAR",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            ViewModel.SaveStatus(box.Text);
    }

    // ---------- zoom ----------

    private void Fit()
    {
        if (Viewport.ViewportWidth <= 0 || Viewport.ViewportHeight <= 0) return;
        var z = Math.Min(Viewport.ViewportWidth / Board.Width, Viewport.ViewportHeight / Board.Height);
        z = Math.Clamp(z, Viewport.MinZoomFactor, Viewport.MaxZoomFactor);
        _fitted = Viewport.ChangeView(0, 0, (float)z, true);
    }

    private void Fit_Click(object sender, RoutedEventArgs e) => Fit();

    private void Reset_Click(object sender, RoutedEventArgs e) =>
        Viewport.ChangeView(760 - 40, 400 - 40, 1f);

    private void Pan_Wheel(object sender, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(PanSurface).Properties.MouseWheelDelta;
        if (delta == 0) return;
        var factor = delta > 0 ? 1.12f : 1 / 1.12f;
        var z = Math.Clamp(Viewport.ZoomFactor * factor, Viewport.MinZoomFactor, Viewport.MaxZoomFactor);

        // Zoom em torno do ponteiro: mantem o ponto do canvas sob o mouse no lugar.
        var p = e.GetCurrentPoint(Viewport).Position;
        var ratio = z / Viewport.ZoomFactor;
        var h = (Viewport.HorizontalOffset + p.X) * ratio - p.X;
        var v = (Viewport.VerticalOffset + p.Y) * ratio - p.Y;
        Viewport.ChangeView(h, v, z);
        e.Handled = true;
    }

    // ---------- pan ----------

    private void Pan_Pressed(object sender, PointerRoutedEventArgs e)
    {
        _panning = true;
        _panStart = e.GetCurrentPoint(Viewport).Position;
        _hStart = Viewport.HorizontalOffset;
        _vStart = Viewport.VerticalOffset;
        PanSurface.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void Pan_Moved(object sender, PointerRoutedEventArgs e)
    {
        if (!_panning) return;
        var p = e.GetCurrentPoint(Viewport).Position;
        Viewport.ChangeView(_hStart - (p.X - _panStart.X), _vStart - (p.Y - _panStart.Y), null, true);
        e.Handled = true;
    }

    private void Pan_Released(object sender, PointerRoutedEventArgs e)
    {
        if (!_panning) return;
        _panning = false;
        PanSurface.ReleasePointerCaptures();
    }
}
