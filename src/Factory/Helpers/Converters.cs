using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Factory.Helpers;

public static class CyberColors
{
    public static readonly Color NeonCyan = Color.FromArgb(0xFF, 0x00, 0xE5, 0xFF);
    public static readonly Color NeonGreen = Color.FromArgb(0xFF, 0x39, 0xFF, 0x14);
    public static readonly Color NeonMagenta = Color.FromArgb(0xFF, 0xFF, 0x2A, 0x6D);
    public static readonly Color NeonAmber = Color.FromArgb(0xFF, 0xFF, 0xB0, 0x00);
    public static readonly Color NeonViolet = Color.FromArgb(0xFF, 0xB3, 0x88, 0xFF);

    public static Color Accent(string? name) => name?.ToLowerInvariant() switch
    {
        "green" => NeonGreen,
        "magenta" => NeonMagenta,
        "amber" => NeonAmber,
        "violet" => NeonViolet,
        _ => NeonCyan,
    };
}

/// <summary>LED de app: verde aceso rodando; cinza apagado parado (rosa parecia erro).</summary>
public sealed partial class RunningBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush On = new(CyberColors.NeonGreen);
    private static readonly SolidColorBrush Off = new(Color.FromArgb(0xFF, 0x5E, 0x6B, 0x80));

    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? On : Off;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>"cyan|green|magenta|amber" → pincel neon (icone, texto de destaque).</summary>
public sealed partial class AccentBrushConverter : IValueConverter
{
    private static readonly Dictionary<string, SolidColorBrush> Cache = new();

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var key = (value as string ?? "cyan").ToLowerInvariant();
        lock (Cache)
            return Cache.TryGetValue(key, out var b) ? b : Cache[key] = new SolidColorBrush(CyberColors.Accent(key));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>Mesma cor, translucida: fundo do icone do tile.</summary>
public sealed partial class AccentGlowConverter : IValueConverter
{
    private static readonly Dictionary<string, SolidColorBrush> Cache = new();

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var key = (value as string ?? "cyan").ToLowerInvariant();
        lock (Cache)
            return Cache.TryGetValue(key, out var b) ? b : Cache[key] = new SolidColorBrush(CyberColors.Accent(key)) { Opacity = 0.16 };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>LED: verde neon online, magenta offline.</summary>
public sealed partial class BoolToStatusBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Online = new(CyberColors.NeonGreen);
    private static readonly SolidColorBrush Offline = new(CyberColors.NeonMagenta);

    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? Online : Offline;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>Halo translúcido do LED.</summary>
public sealed partial class BoolToStatusGlowBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Online = new(CyberColors.NeonGreen) { Opacity = 0.25 };
    private static readonly SolidColorBrush Offline = new(CyberColors.NeonMagenta) { Opacity = 0.25 };

    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? Online : Offline;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed partial class BoolNegationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        value is not true;
}

/// <summary>Texto nao vazio → Visible (card de erro que so aparece quando ha mensagem).</summary>
public sealed partial class HasTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        string.IsNullOrWhiteSpace(value as string) ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>LED de sessao: aceso (1.0) quando ao vivo, apagado (0.25) quando nao.</summary>
public sealed partial class LiveOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? 1.0 : 0.25;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
