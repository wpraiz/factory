using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Factory.Helpers;

/// <summary>
/// Paleta SO da secao Seguranca: acentos dessaturados + cinza de leitura. O resto do Factory segue no
/// CyberColors/Cyber.xaml originais — mexer aqui nao muda nenhuma outra tela.
/// </summary>
public static class SegColors
{
    public static readonly Color Cyan = Color.FromArgb(0xFF, 0x4D, 0xC9, 0xE6);
    public static readonly Color Green = Color.FromArgb(0xFF, 0x4F, 0xD1, 0x8B);
    public static readonly Color Magenta = Color.FromArgb(0xFF, 0xF2, 0x72, 0x8F);
    public static readonly Color Amber = Color.FromArgb(0xFF, 0xE8, 0xB0, 0x4A);
    public static readonly Color Violet = Color.FromArgb(0xFF, 0xB8, 0xA4, 0xF5);
    public static readonly Color Dim = Color.FromArgb(0xFF, 0x94, 0xA3, 0xB8);

    public static Color Accent(string? name) => name?.ToLowerInvariant() switch
    {
        "green" => Green,
        "magenta" => Magenta,
        "amber" => Amber,
        "violet" => Violet,
        "dim" => Dim,
        _ => Cyan,
    };
}

/// <summary>AccentBrush da Seguranca (mesma chave, sobrescrita no Page.Resources dela).</summary>
public sealed partial class SegAccentBrushConverter : IValueConverter
{
    private static readonly Dictionary<string, SolidColorBrush> Cache = new();
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var key = (value as string ?? "cyan").ToLowerInvariant();
        lock (Cache) return Cache.TryGetValue(key, out var b) ? b : Cache[key] = new SolidColorBrush(SegColors.Accent(key));
    }
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

/// <summary>AccentGlow da Seguranca: a mesma cor a 16%.</summary>
public sealed partial class SegAccentGlowConverter : IValueConverter
{
    private static readonly Dictionary<string, SolidColorBrush> Cache = new();
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var key = (value as string ?? "cyan").ToLowerInvariant();
        lock (Cache) return Cache.TryGetValue(key, out var b) ? b : Cache[key] = new SolidColorBrush(SegColors.Accent(key)) { Opacity = 0.16 };
    }
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
