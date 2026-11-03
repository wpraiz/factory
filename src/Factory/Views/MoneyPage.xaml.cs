using Factory.Models;
using Factory.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Factory.Views;

public sealed partial class MoneyPage : Page
{
    public MoneyViewModel ViewModel { get; }

    public MoneyPage()
    {
        ViewModel = App.Services.GetRequiredService<MoneyViewModel>();
        InitializeComponent();
        ViewModel.DotsChanged += OnDotsChanged;
        Unloaded += (_, _) => ViewModel.DotsChanged -= OnDotsChanged;
        ViewModel.Rebuild();
    }

    private void OnDotsChanged(object? sender, EventArgs e) => DrawDots();

    /// <summary>
    /// Marca = um circulo de uma cor so (ciano), tamanho pela receita; identidade vem do
    /// rotulo em cor de texto ao lado, nunca da cor. Estagio vai no tooltip e no rotulo.
    /// </summary>
    private void DrawDots()
    {
        DotsLayer.Children.Clear();
        PlotEmpty.Visibility = ViewModel.Dots.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var mark = (SolidColorBrush)Application.Current.Resources["NeonCyan"];
        var ring = (SolidColorBrush)Application.Current.Resources["CyberBg"];
        var soon = (SolidColorBrush)Application.Current.Resources["NeonGreen"];
        var ink = (SolidColorBrush)Application.Current.Resources["CyberText"];
        var mono = (FontFamily)Application.Current.Resources["MonoFont"];
        // pontos contam como ocupados pros rotulos nao cairem em cima de outro ponto
        var ocupados = ViewModel.Dots.Select(p => new Windows.Foundation.Rect(p.X - p.Size / 2, p.Y - p.Size / 2, p.Size, p.Size)).ToList();
        // textos dos quadrantes (MoneyPage.xaml: FAZER JA / APOSTA GRANDE / RAPIDO MAS PEQUENO / EVITAR)
        ocupados.Add(new Windows.Foundation.Rect(58, 54, 300, 16));
        ocupados.Add(new Windows.Foundation.Rect(418, 54, 340, 16));
        ocupados.Add(new Windows.Foundation.Rect(58, 488, 150, 16));
        ocupados.Add(new Windows.Foundation.Rect(418, 488, 300, 16));

        foreach (var d in ViewModel.Dots)
        {
            if (d.Highlight)
            {
                // anel: dinheiro rapido (<= 14 d). Segundo canal alem do tamanho, com rotulo junto.
                var halo = new Ellipse { Width = d.Size + 10, Height = d.Size + 10, Stroke = soon, StrokeThickness = 2, Fill = null };
                Canvas.SetLeft(halo, d.X - (d.Size + 10) / 2);
                Canvas.SetTop(halo, d.Y - (d.Size + 10) / 2);
                DotsLayer.Children.Add(halo);
            }
            var dot = new Ellipse
            {
                Width = d.Size, Height = d.Size,
                Fill = mark, Stroke = ring, StrokeThickness = 2, Opacity = 0.85,
            };
            Canvas.SetLeft(dot, d.X - d.Size / 2);
            Canvas.SetTop(dot, d.Y - d.Size / 2);
            ToolTipService.SetToolTip(dot, d.Tooltip);
            var cmd = d.GoCommand;
            dot.Tapped += (_, _) => cmd.Execute(null);
            dot.PointerEntered += (s, _) => ((Ellipse)s).Opacity = 1;
            dot.PointerExited += (s, _) => ((Ellipse)s).Opacity = 0.85;
            DotsLayer.Children.Add(dot);

            var stage = string.IsNullOrWhiteSpace(d.Project.Manifest.Stage) ? "" : $" · {d.Project.Manifest.Stage}";
            if (d.Highlight) stage += $" · ≤{d.Project.Manifest.FirstMoneyDays}d";
            var label = new TextBlock
            {
                Text = d.Project.Name + stage,
                FontFamily = mono, FontSize = 11, Foreground = ink,
            };
            var (lx, ly) = LugarLivre(d, label.Text, ocupados);
            Canvas.SetLeft(label, lx);
            Canvas.SetTop(label, ly);
            DotsLayer.Children.Add(label);
        }
    }

    /// <summary>
    /// Rotulo sem empilhar (5-6 nomes no mesmo pixel): tenta direita, esquerda, cima,
    /// baixo do ponto e desce em degraus se tudo colidir. Mono 11 ≈ 6,7 px por caractere.
    /// </summary>
    private static (double X, double Y) LugarLivre(MoneyDot d, string text, List<Windows.Foundation.Rect> ocupados)
    {
        var w = text.Length * 6.7;
        const double h = 14;
        var r = d.Size / 2;
        (double, double)[] tentativas =
        [
            (d.X + r + 4, d.Y - 8), (d.X - r - 4 - w, d.Y - 8),
            (d.X - w / 2, d.Y - r - 4 - h), (d.X - w / 2, d.Y + r + 4),
        ];
        foreach (var (x, y) in tentativas.Concat(Enumerable.Range(1, 6).Select(i => (d.X + r + 4, d.Y - 8 + i * (h + 2)))))
        {
            var rect = new Windows.Foundation.Rect(x, y, w, h);
            // dentro do retangulo do plot (48..768 × 48..508), nunca por fora da caixa desenhada
            if (x < 48 || x + w > 768 || y < 48 || y + h > 508) continue;
            if (ocupados.Any(o => { var i = o; i.Intersect(rect); return !i.IsEmpty && i.Width > 0 && i.Height > 0; })) continue;
            ocupados.Add(rect);
            return (x, y);
        }
        return (d.X + r + 4, d.Y - 8);
    }

    private void OpenUrl_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ProjectInfo p }) ViewModel.OpenUrl(p);
    }

    private void GoTo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ProjectInfo p }) ViewModel.GoTo(p);
    }
}
