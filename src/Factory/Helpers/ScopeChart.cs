using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace Factory.Helpers;

/// <summary>
/// "Scope" do design kit C-opus-cockpit (osciloscopio): fundo Abyss, grade vertical a cada 15 s, linha de 2 px com
/// area a 8%, cabeca viva de 8 px no ultimo ponto. Um eixo so (regra do dataviz: nunca dois y). Modo barras pra
/// contagens (2 px de vao entre barras). Hover = cursor vertical + leitura do instante.
/// </summary>
public sealed partial class ScopeChart : Grid
{
    public sealed record Serie(string Nome, Color Cor, IReadOnlyList<double> Dados, IReadOnlyList<string>? Rotulos = null);

    // tokens do kit: Abyss #05070A, Edge #1C2433, grade rgba(42,53,71,.35), Dim #5E6B80, Text #C8D3E0
    private static readonly Color Abyss = Color.FromArgb(0xFF, 0x05, 0x07, 0x0A);
    private static readonly Color Edge = Color.FromArgb(0xFF, 0x1C, 0x24, 0x33);
    private static readonly Color GridC = Color.FromArgb(0x59, 0x2A, 0x35, 0x47);
    private static readonly Color Dim = Factory.Helpers.SegColors.Dim;   // = CyberDim #94A3B8
    private static readonly Color Txt = Color.FromArgb(0xFF, 0xC8, 0xD3, 0xE0);

    private readonly Canvas _plot = new();
    private readonly Line _cursor = new() { StrokeThickness = 1, Visibility = Visibility.Collapsed };
    private readonly Border _tip = new() { Visibility = Visibility.Collapsed, CornerRadius = new CornerRadius(3), Padding = new Thickness(8, 4, 8, 4), BorderThickness = new Thickness(1) };
    private readonly TextBlock _tipText = new() { FontSize = 12 };
    private readonly TextBlock _maxLabel = new() { FontSize = 12, Margin = new Thickness(8, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
    private IReadOnlyList<Serie> _series = [];
    private bool _barras;
    private Func<double, string> _fmt = v => v.ToString("0");
    private int _janela = 90;
    private double _max = 1;
    private Point? _mouse;

    public ScopeChart()
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Abyss), BorderBrush = new SolidColorBrush(Edge),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Child = _plot,
        };
        _plot.Background = new SolidColorBrush(Colors.Transparent);   // pega o hover no vazio tambem
        Children.Add(border);
        Children.Add(_maxLabel);
        _tip.Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x11, 0x16, 0x1F));
        _tip.BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x2A, 0x35, 0x47));
        _tip.Child = _tipText;
        _tipText.Foreground = new SolidColorBrush(Txt);
        _tipText.FontFamily = (FontFamily)Application.Current.Resources["MonoFont"];
        _maxLabel.FontFamily = (FontFamily)Application.Current.Resources["MonoFont"];
        _maxLabel.Foreground = new SolidColorBrush(Dim);
        _cursor.Stroke = new SolidColorBrush(Color.FromArgb(0x99, 0xC8, 0xD3, 0xE0));
        SizeChanged += (_, _) => Render();
        _plot.PointerMoved += (_, e) => { _mouse = e.GetCurrentPoint(_plot).Position; Hover(_mouse.Value); };
        _plot.PointerExited += (_, _) => { _mouse = null; _cursor.Visibility = _tip.Visibility = Visibility.Collapsed; };
    }

    public void Set(IReadOnlyList<Serie> series, Func<double, string> fmt, bool barras = false, int janela = 90)
    {
        _series = series; _fmt = fmt; _barras = barras; _janela = janela;
        Render();
    }

    private double W => Math.Max(1, _plot.ActualWidth);
    private double H => Math.Max(1, _plot.ActualHeight);
    private double Step => W / Math.Max(1, _janela - 1);

    /// <summary>x do i-esimo ponto: o ultimo encosta na borda direita, o passado escorre pra esquerda.</summary>
    private double X(int i, int count) => W - (count - 1 - i) * Step;
    private double Y(double v) => H - 4 - (H - 14) * Math.Clamp(v / _max, 0, 1);

    private void Render()
    {
        _plot.Children.Clear();
        if (_plot.ActualWidth < 2) return;

        var pico = _series.SelectMany(s => s.Dados).DefaultIfEmpty(0).Max();
        _max = Nice(Math.Max(pico * 1.15, 1));
        _maxLabel.Text = "máx " + _fmt(_max);

        var grade = new SolidColorBrush(GridC);
        for (var s = 15; s < _janela; s += 15)   // uma linha a cada 15 s, contada a partir de "agora"
        {
            var x = W - s * Step;
            _plot.Children.Add(new Line { X1 = x, X2 = x, Y1 = 0, Y2 = H, Stroke = grade, StrokeThickness = 1 });
        }
        _plot.Children.Add(new Line { X1 = 0, X2 = W, Y1 = Y(_max / 2), Y2 = Y(_max / 2), Stroke = grade, StrokeThickness = 1 });

        foreach (var serie in _series)
        {
            var d = serie.Dados;
            if (d.Count == 0) continue;
            if (_barras)
            {
                var bw = Math.Max(1, Step - 2);
                var brush = new SolidColorBrush(serie.Cor);
                for (var i = 0; i < d.Count; i++)
                {
                    if (d[i] <= 0) continue;
                    var y = Y(d[i]);
                    var r = new Rectangle { Width = bw, Height = Math.Max(2, H - 4 - y), Fill = brush, RadiusX = 1, RadiusY = 1 };
                    Canvas.SetLeft(r, X(i, d.Count) - bw / 2); Canvas.SetTop(r, y);
                    _plot.Children.Add(r);
                }
                continue;
            }
            var line = new Polyline { Stroke = new SolidColorBrush(serie.Cor), StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
            var area = new Polygon { Fill = new SolidColorBrush(Color.FromArgb(0x14, serie.Cor.R, serie.Cor.G, serie.Cor.B)) };
            area.Points.Add(new Point(X(0, d.Count), H));
            for (var i = 0; i < d.Count; i++)
            {
                var p = new Point(X(i, d.Count), Y(d[i]));
                line.Points.Add(p); area.Points.Add(p);
            }
            area.Points.Add(new Point(W, H));
            _plot.Children.Add(area);
            _plot.Children.Add(line);
            var head = new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(serie.Cor), Stroke = new SolidColorBrush(Abyss), StrokeThickness = 2 };
            Canvas.SetLeft(head, W - 4 - 1); Canvas.SetTop(head, Y(d[^1]) - 4);
            _plot.Children.Add(head);
        }
        _plot.Children.Add(_cursor);
        _plot.Children.Add(_tip);
        if (_mouse is { } m) Hover(m);   // o dado rolou 1 s: a leitura do cursor acompanha o instante certo
    }

    private void Hover(Point p)
    {
        var n = _series.Count == 0 ? 0 : _series.Max(s => s.Dados.Count);
        if (n == 0) return;
        var atras = (int)Math.Round((W - p.X) / Step);
        if (atras < 0 || atras >= n) { _cursor.Visibility = _tip.Visibility = Visibility.Collapsed; return; }
        var x = W - atras * Step;
        _cursor.X1 = _cursor.X2 = x; _cursor.Y1 = 0; _cursor.Y2 = H;
        _cursor.Visibility = Visibility.Visible;
        var partes = _series.Where(s => s.Dados.Count > atras).Select(s =>
        {
            var i = s.Dados.Count - 1 - atras;
            var quem = s.Rotulos is { } r && i < r.Count && i >= 0 && r[i].Length > 0 ? $": {r[i]}" : "";
            return $"{s.Nome} {_fmt(s.Dados[i])}{quem}";
        });
        _tipText.Text = (atras == 0 ? "agora" : $"há {atras} s") + " · " + string.Join(" · ", partes);
        _tip.Visibility = Visibility.Visible;
        _tip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var tw = _tip.DesiredSize.Width;
        Canvas.SetLeft(_tip, Math.Max(0, x + 8 + tw > W ? x - 8 - tw : x + 8));   // vira pro lado de dentro perto da borda
        Canvas.SetTop(_tip, 4);
    }

    /// <summary>Teto "redondo" 1-2-5 × 10^n: a escala nao fica pulando a cada segundo.</summary>
    private static double Nice(double v)
    {
        var e = Math.Pow(10, Math.Floor(Math.Log10(v)));
        var f = v / e;
        return (f <= 1 ? 1 : f <= 2 ? 2 : f <= 5 ? 5 : 10) * e;
    }
}
