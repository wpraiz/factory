using System.Collections.Specialized;
using System.ComponentModel;
using System.Numerics;
using Factory.Helpers;
using Factory.Models;
using Factory.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace Factory.Views;

/// <summary>
/// Mostrador do radar. Escala logaritmica: 1 m, 3 m, 10 m, 30 m. Cada alvo e um ponto no seu
/// angulo fixo; a distancia move o ponto pro centro ou pra borda com animacao implicita de 400 ms.
/// A varredura gira so enquanto esta varrendo (e o indicador de "ao vivo", nao enfeite).
/// </summary>
public sealed partial class RadarPage : Page
{
    public RadarViewModel ViewModel { get; }

    private const double Size = 560, Center = 280, Radius = 262;
    private static readonly double[] Rings = [1, 3, 10, 30];
    private readonly Dictionary<RadarTarget, (Ellipse Dot, TextBlock Label)> _dots = new();
    private readonly Dictionary<RadarTarget, PropertyChangedEventHandler> _handlers = new();
    private Line? _sweep;
    private Polygon? _wedge;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };

    public RadarPage()
    {
        ViewModel = App.Services.GetRequiredService<RadarViewModel>();
        InitializeComponent();
        DrawScope();
        ViewModel.Targets.CollectionChanged += Targets_Changed;
        foreach (var t in ViewModel.Targets) AddDot(t);
        ViewModel.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(RadarViewModel.IsScanning)) UpdateSweep(); };
        _clock.Tick += (_, _) => { foreach (var t in ViewModel.Targets) t.Touch(); };
        Loaded += (_, _) => { UpdateSweep(); _clock.Start(); };
        Unloaded += (_, _) => _clock.Stop();
    }

    // ---------- fundo: aneis + varredura ----------

    private static double R(double meters) =>
        Radius * Math.Clamp((Math.Log10(Math.Max(meters, 0.3)) + 0.6) / (Math.Log10(50) + 0.6), 0.04, 1.0);

    private void DrawScope()
    {
        var edge = (SolidColorBrush)Application.Current.Resources["CyberPanelEdge"];
        var dim = (SolidColorBrush)Application.Current.Resources["CyberDim"];
        var cyan = new SolidColorBrush(CyberColors.NeonCyan);

        foreach (var m in Rings)
        {
            var r = R(m);
            var ring = new Ellipse { Width = r * 2, Height = r * 2, Stroke = edge, StrokeThickness = 1 };
            Canvas.SetLeft(ring, Center - r); Canvas.SetTop(ring, Center - r);
            Scope.Children.Add(ring);
            var lbl = new TextBlock { Text = m >= 10 ? $"{m:0} m" : $"{m} m", FontFamily = (FontFamily)Application.Current.Resources["MonoFont"], FontSize = 10, Foreground = dim };
            Canvas.SetLeft(lbl, Center + 4); Canvas.SetTop(lbl, Center - r + 2);
            Scope.Children.Add(lbl);
        }
        Scope.Children.Add(new Line { X1 = Center, Y1 = 0, X2 = Center, Y2 = Size, Stroke = edge, StrokeThickness = 1, Opacity = 0.6 });
        Scope.Children.Add(new Line { X1 = 0, Y1 = Center, X2 = Size, Y2 = Center, Stroke = edge, StrokeThickness = 1, Opacity = 0.6 });

        // varredura: setor translucido + linha, girando em torno do centro
        _wedge = new Polygon { Fill = new SolidColorBrush(Color.FromArgb(0x30, 0x00, 0xE5, 0xFF)), Opacity = 0 };
        _wedge.Points.Add(new Windows.Foundation.Point(Center, Center));
        for (var a = -28; a <= 0; a += 4)
        {
            var rad = a * Math.PI / 180;
            _wedge.Points.Add(new Windows.Foundation.Point(Center + Radius * Math.Cos(rad), Center + Radius * Math.Sin(rad)));
        }
        Scope.Children.Add(_wedge);
        _sweep = new Line { X1 = Center, Y1 = Center, X2 = Center + Radius, Y2 = Center, Stroke = cyan, StrokeThickness = 1.5, Opacity = 0 };
        Scope.Children.Add(_sweep);

        var me = new Ellipse { Width = 10, Height = 10, Fill = cyan };
        Canvas.SetLeft(me, Center - 5); Canvas.SetTop(me, Center - 5);
        Scope.Children.Add(me);
    }

    private void UpdateSweep()
    {
        if (_sweep is null || _wedge is null) return;
        foreach (var el in new UIElement[] { _sweep, _wedge })
        {
            var v = ElementCompositionPreview.GetElementVisual(el);
            v.CenterPoint = new Vector3((float)Center, (float)Center, 0);
            if (ViewModel.IsScanning)
            {
                el.Opacity = el == _sweep ? 0.9 : 1;
                var spin = v.Compositor.CreateScalarKeyFrameAnimation();
                spin.InsertKeyFrame(0f, 0f, v.Compositor.CreateLinearEasingFunction());
                spin.InsertKeyFrame(1f, 360f, v.Compositor.CreateLinearEasingFunction());
                spin.Duration = TimeSpan.FromSeconds(4);
                spin.IterationBehavior = AnimationIterationBehavior.Forever;
                v.StartAnimation("RotationAngleInDegrees", spin);
            }
            else
            {
                v.StopAnimation("RotationAngleInDegrees");
                el.Opacity = 0;
            }
        }
    }

    // ---------- pontos ----------

    private void Targets_Changed(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var t in _dots.Keys.ToList()) RemoveDot(t);
            return;
        }
        if (e.OldItems is not null) foreach (RadarTarget t in e.OldItems) RemoveDot(t);
        if (e.NewItems is not null) foreach (RadarTarget t in e.NewItems) AddDot(t);
    }

    private void AddDot(RadarTarget t)
    {
        if (_dots.ContainsKey(t)) return;
        var dot = new Ellipse { Width = 12, Height = 12 };
        var label = new TextBlock
        {
            FontFamily = (FontFamily)Application.Current.Resources["MonoFont"], FontSize = 10,
            Foreground = (SolidColorBrush)Application.Current.Resources["CyberText"], IsHitTestVisible = false,
        };
        ToolTipService.SetToolTip(dot, $"{t.Title}\n{t.KindLabel} · {t.Detail}");
        Scope.Children.Add(dot);
        Scope.Children.Add(label);
        _dots[t] = (dot, label);

        // movimento suave quando Canvas.Left/Top mudam
        foreach (var el in new UIElement[] { dot, label })
        {
            var v = ElementCompositionPreview.GetElementVisual(el);
            var c = v.Compositor;
            var move = c.CreateVector3KeyFrameAnimation();
            move.Target = "Offset";
            move.InsertExpressionKeyFrame(1f, "this.FinalValue");
            move.Duration = TimeSpan.FromMilliseconds(400);
            var set = c.CreateImplicitAnimationCollection();
            set["Offset"] = move;
            v.ImplicitAnimations = set;
        }

        PropertyChangedEventHandler h = (_, e) =>
        {
            if (e.PropertyName is nameof(RadarTarget.Distance) or nameof(RadarTarget.State) or nameof(RadarTarget.Present) or nameof(RadarTarget.Name) or nameof(RadarTarget.Rssi))
                Place(t);
        };
        _handlers[t] = h;
        t.PropertyChanged += h;
        Place(t);
    }

    private void RemoveDot(RadarTarget t)
    {
        if (!_dots.Remove(t, out var pair)) return;
        Scope.Children.Remove(pair.Dot);
        Scope.Children.Remove(pair.Label);
        if (_handlers.Remove(t, out var h)) t.PropertyChanged -= h;
    }

    private void Place(RadarTarget t)
    {
        if (!_dots.TryGetValue(t, out var pair)) return;
        var (dot, label) = pair;
        var r = R(t.Distance);
        var rad = t.Angle * Math.PI / 180;
        var x = Center + r * Math.Cos(rad);
        var y = Center + r * Math.Sin(rad);

        var size = t.Kind == RadarTarget.KindLan ? 10 : Math.Clamp(18 + (t.Rssi + 40) / 4.0, 8, 18);
        dot.Width = dot.Height = size;
        Canvas.SetLeft(dot, x - size / 2); Canvas.SetTop(dot, y - size / 2);

        var color = t.IsNew ? CyberColors.NeonMagenta : t.IsMoving ? CyberColors.NeonAmber : CyberColors.Accent(t.Accent);
        dot.Fill = new SolidColorBrush(color);
        dot.Opacity = t.Present ? 1 : 0.25;
        dot.Stroke = t.IsMine ? new SolidColorBrush(CyberColors.NeonGreen) : null;
        dot.StrokeThickness = t.IsMine ? 2 : 0;

        // rotulo so pra quem esta perto (ate 10 m) ou e NOVO: senao vira sopa de letras
        var show = t.Present && (t.Distance <= 10 || t.IsNew) && t.Kind != RadarTarget.KindLan;
        label.Text = show ? t.Title : "";
        label.Foreground = new SolidColorBrush(t.IsNew ? CyberColors.NeonMagenta : Color.FromArgb(0xFF, 0xC8, 0xD3, 0xE0));
        Canvas.SetLeft(label, x + size / 2 + 4); Canvas.SetTop(label, y - 7);
        ToolTipService.SetToolTip(dot, $"{t.Title}\n{t.KindLabel} · {t.Detail}\n{t.DistanceText} · {t.RssiText} · {t.StateText}");

        Pulse(dot, t.IsNew);
    }

    private static void Pulse(UIElement el, bool on)
    {
        var v = ElementCompositionPreview.GetElementVisual(el);
        v.CenterPoint = new Vector3((float)el.ActualSize.X / 2, (float)el.ActualSize.Y / 2, 0);
        if (!on) { v.StopAnimation("Scale"); v.Scale = Vector3.One; return; }
        var a = v.Compositor.CreateVector3KeyFrameAnimation();
        a.InsertKeyFrame(0f, Vector3.One);
        a.InsertKeyFrame(0.5f, new Vector3(1.6f, 1.6f, 1f));
        a.InsertKeyFrame(1f, Vector3.One);
        a.Duration = TimeSpan.FromMilliseconds(900);
        a.IterationBehavior = AnimationIterationBehavior.Forever;
        v.StartAnimation("Scale", a);
    }
}
