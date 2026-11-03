using Windows.Foundation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Factory.Helpers;

/// <summary>
/// Painel que quebra linha (o WinUI nao traz um; o do CommunityToolkit nao esta no projeto).
/// Each child at its natural size, espacamento fixo. Usado nos chips da BUSCA.
/// </summary>
public sealed partial class WrapPanel : Panel
{
    public double HorizontalSpacing { get; set; } = 8;
    public double VerticalSpacing { get; set; } = 8;

    protected override Size MeasureOverride(Size available)
    {
        double x = 0, y = 0, lineH = 0, maxW = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(available.Width, double.PositiveInfinity));
            var s = child.DesiredSize;
            if (x > 0 && x + s.Width > available.Width) { y += lineH + VerticalSpacing; x = 0; lineH = 0; }
            x += s.Width + HorizontalSpacing;
            lineH = Math.Max(lineH, s.Height);
            maxW = Math.Max(maxW, x - HorizontalSpacing);
        }
        return new Size(double.IsInfinity(available.Width) ? maxW : Math.Min(maxW, available.Width), y + lineH);
    }

    protected override Size ArrangeOverride(Size final)
    {
        double x = 0, y = 0, lineH = 0;
        foreach (var child in Children)
        {
            var s = child.DesiredSize;
            if (x > 0 && x + s.Width > final.Width) { y += lineH + VerticalSpacing; x = 0; lineH = 0; }
            child.Arrange(new Rect(x, y, s.Width, s.Height));
            x += s.Width + HorizontalSpacing;
            lineH = Math.Max(lineH, s.Height);
        }
        return final;
    }
}
