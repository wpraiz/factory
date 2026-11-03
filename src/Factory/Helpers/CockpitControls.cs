using System.Windows.Input;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace Factory.Helpers;

/// <summary>
/// Chave com tampa (guarded switch) de painel de A-10. A acao fica embaixo de uma tampa listrada:
/// 1o clique levanta a tampa (arma), 2o clique dispara. Sozinha a tampa fecha de novo em 6 s. Serve pra acao que pede
/// admin ou demora (AUDITAR): o "perigoso pede confirmacao" da Forja Silenciosa, so que com a mao.
/// </summary>
public sealed partial class GuardedSwitch : Grid
{
    private readonly Button _botao = new();
    private readonly Grid _tampa = new();
    private readonly ScaleTransform _escala = new() { ScaleY = 1 };
    private readonly TextBlock _rotuloTampa = new();
    private readonly DispatcherTimer _fecha = new() { Interval = TimeSpan.FromSeconds(6) };
    private bool _armado;

    public ICommand? Command { get => _botao.Command; set => _botao.Command = value; }
    private string _texto = "";
    public string Texto
    {
        get => _texto;
        set { _texto = value; _botao.Content = value; _rotuloTampa.Text = value + "  ▲"; }
    }
    public string Dica { set { ToolTipService.SetToolTip(this, value); } }

    public GuardedSwitch()
    {
        Height = 36;
        MinWidth = 120;
        _botao.Style = (Style)Application.Current.Resources["ArcadeButtonDanger"];
        _botao.HorizontalAlignment = HorizontalAlignment.Stretch;
        _botao.VerticalAlignment = VerticalAlignment.Stretch;
        _botao.IsEnabled = false;
        _botao.Click += (_, _) => Fechar();
        Children.Add(_botao);

        // tampa: listras ambar/preto a 45 graus (o padrao de "perigo" de painel), presa no topo, levanta pra cima
        var amber = SegColors.Amber;
        var listras = new Canvas { IsHitTestVisible = false };
        _tampa.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x16, 0x1D, 0x29)),
            BorderBrush = new SolidColorBrush(amber), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
        });
        _tampa.Children.Add(listras);
        _rotuloTampa.Text = "▲";
        _rotuloTampa.FontSize = 10;
        _rotuloTampa.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
        _rotuloTampa.CharacterSpacing = 150;
        _rotuloTampa.Foreground = new SolidColorBrush(amber);
        _rotuloTampa.HorizontalAlignment = HorizontalAlignment.Center;
        _rotuloTampa.VerticalAlignment = VerticalAlignment.Center;
        _tampa.Children.Add(new Border
        {
            Child = _rotuloTampa, Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x05, 0x07, 0x0A)),
            Padding = new Thickness(6, 1, 6, 1), CornerRadius = new CornerRadius(2),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        });
        _tampa.RenderTransform = _escala;
        _tampa.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0);
        _tampa.SizeChanged += (_, e) =>
        {
            listras.Children.Clear();
            listras.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(1, 1, Math.Max(0, e.NewSize.Width - 2), Math.Max(0, e.NewSize.Height - 2)) };
            var stroke = new SolidColorBrush(Color.FromArgb(0x55, amber.R, amber.G, amber.B));
            for (var x = -e.NewSize.Height; x < e.NewSize.Width; x += 12)
                listras.Children.Add(new Line { X1 = x, Y1 = e.NewSize.Height, X2 = x + e.NewSize.Height, Y2 = 0, Stroke = stroke, StrokeThickness = 4 });
        };
        _tampa.Tapped += (_, _) => Armar();
        Children.Add(_tampa);
        _fecha.Tick += (_, _) => Fechar();
    }

    private void Armar()
    {
        if (_armado) return;
        _armado = true;
        _botao.IsEnabled = true;
        _tampa.IsHitTestVisible = false;
        _botao.Content = "CONFIRMAR";
        Anima(0, 0);   // tampa sobe e some: o botao aparece inteiro
        _botao.Focus(FocusState.Programmatic);   // Enter dispara
        _fecha.Start();
    }

    private void Fechar()
    {
        _fecha.Stop();
        if (!_armado) return;
        _armado = false;
        _botao.IsEnabled = false;
        _botao.Content = _texto;
        _tampa.IsHitTestVisible = true;
        Anima(1, 1);
    }

    private void Anima(double scaleY, double opacidade)
    {
        var sb = new Storyboard();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var a = new DoubleAnimation { To = scaleY, Duration = TimeSpan.FromMilliseconds(220), EasingFunction = ease };
        Storyboard.SetTarget(a, _escala); Storyboard.SetTargetProperty(a, "ScaleY");
        var o = new DoubleAnimation { To = opacidade, Duration = TimeSpan.FromMilliseconds(220), EasingFunction = ease };
        Storyboard.SetTarget(o, _tampa); Storyboard.SetTargetProperty(o, "Opacity");
        sb.Children.Add(a); sb.Children.Add(o);
        sb.Begin();
    }
}

/// <summary>
/// MASTER CAUTION do painel: acende ambar e pisca (1 s, o pulso do LED magenta do kit) quando ha algo NOVO ou uma
/// protecao desligada que ele ainda nao reconheceu. Clicar = "vi" (apaga ate aparecer coisa nova).
/// </summary>
public sealed partial class MasterCaution : Button
{
    private readonly Border _face = new();
    private readonly TextBlock _l1 = new() { Text = "MASTER" }, _l2 = new() { Text = "CAUTION" };
    private Storyboard? _pulso;
    private bool _aceso;

    public MasterCaution()
    {
        Padding = new Thickness(0);
        BorderThickness = new Thickness(0);
        Background = new SolidColorBrush(Colors.Transparent);
        foreach (var t in new[] { _l1, _l2 })
        {
            t.FontSize = 12; t.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; t.CharacterSpacing = 40;
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        }
        _face.Width = 96; _face.Height = 44; _face.CornerRadius = new CornerRadius(3); _face.BorderThickness = new Thickness(1);
        _face.Child = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2, Children = { _l1, _l2 } };
        Content = _face;
        Aplicar();
    }

    public bool Aceso
    {
        get => _aceso;
        set { if (_aceso == value) return; _aceso = value; Aplicar(); }
    }

    private void Aplicar()
    {
        var amber = SegColors.Amber;
        // apagado = lampada fria (Mute #3A4558 sobre Abyss); aceso = ambar com fundo a 18%
        _face.Background = new SolidColorBrush(_aceso ? Color.FromArgb(0x2E, amber.R, amber.G, amber.B) : Color.FromArgb(0xFF, 0x05, 0x07, 0x0A));
        _face.BorderBrush = new SolidColorBrush(_aceso ? amber : Color.FromArgb(0xFF, 0x2A, 0x35, 0x47));
        var fg = new SolidColorBrush(_aceso ? amber : Color.FromArgb(0xFF, 0x5E, 0x6B, 0x80));   // apagada: cinza de controle (lampada fria)
        _l1.Foreground = _l2.Foreground = fg;
        _pulso?.Stop();
        _face.Opacity = 1;
        if (!_aceso) return;
        // pisca 10 s (5 ciclos do pulso de 1 s do kit) e fica aceso fixo: alerta, nao loop eterno
        _pulso = new Storyboard { RepeatBehavior = new RepeatBehavior(5) };
        var a = new DoubleAnimation { From = 1, To = 0.45, Duration = TimeSpan.FromMilliseconds(500), AutoReverse = true, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
        Storyboard.SetTarget(a, _face); Storyboard.SetTargetProperty(a, "Opacity");
        _pulso.Children.Add(a);
        _pulso.Begin();
    }
}
