using System.Text.RegularExpressions;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace Factory.Helpers;

/// <summary>
/// Markdown minimo pra texto que vem de STATUS.md/Claude (proxima acao, bloqueios): **negrito**,
/// `codigo`, titulos (#) e listas (- / 1.). Uso: helpers:MarkdownLite.Text="{x:Bind X, Mode=OneWay}"
/// num TextBlock. Sem isso a tela mostrava os ** literais.
/// </summary>
public static partial class MarkdownLite
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(MarkdownLite), new PropertyMetadata("", OnTextChanged));

    public static string GetText(DependencyObject o) => (string)o.GetValue(TextProperty);
    public static void SetText(DependencyObject o, string value) => o.SetValue(TextProperty, value);

    [GeneratedRegex(@"(\*\*[^*\n]+\*\*|`[^`\n]+`)")]
    private static partial Regex InlineRx();

    [GeneratedRegex(@"^\s*(?:[-*+]\s+|(\d+)[.)]\s+)")]
    private static partial Regex ListRx();

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock tb) return;
        tb.Inlines.Clear();
        var text = (e.NewValue as string ?? "").Replace("\r", "");
        var bright = Brush("CyberTextBright");

        var first = true;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Length == 0) continue;
            if (!first) tb.Inlines.Add(new LineBreak());
            first = false;

            var heading = false;
            if (line.StartsWith('#')) { line = line.TrimStart('#').Trim(); heading = true; }
            var m = ListRx().Match(line);
            if (m.Success)
            {
                tb.Inlines.Add(new Run { Text = m.Groups[1].Success ? m.Groups[1].Value + ". " : "• " });
                line = line[m.Length..];
            }

            foreach (var part in InlineRx().Split(line))
            {
                if (part.Length == 0) continue;
                if (part.Length > 4 && part.StartsWith("**") && part.EndsWith("**"))
                    tb.Inlines.Add(new Run { Text = part[2..^2], FontWeight = FontWeights.SemiBold, Foreground = bright });
                // codigo sem cor de acento: ambar e aviso no tema (ui-review); a fonte ja e mono
                else if (part.Length > 2 && part.StartsWith('`') && part.EndsWith('`'))
                    tb.Inlines.Add(new Run { Text = part[1..^1], Foreground = bright });
                else if (heading)
                    tb.Inlines.Add(new Run { Text = part, FontWeight = FontWeights.SemiBold, Foreground = bright });
                else
                    tb.Inlines.Add(new Run { Text = part });
            }
        }
    }

    private static Brush? Brush(string key) =>
        Application.Current.Resources.TryGetValue(key, out var b) ? b as Brush : null;
}
