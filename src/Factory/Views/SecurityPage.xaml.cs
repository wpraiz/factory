using Factory.Helpers;
using Factory.Models;
using Factory.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.UI;

namespace Factory.Views;

public sealed partial class SecurityPage : Page
{
    public SecurityViewModel ViewModel { get; }

    // serie dupla da REDE: par validado no dataviz (dark, superficie #11161F): ΔE CVD 10,9 · normal 21,6
    private static Color CorEntrada => SegColors.Cyan;
    private static Color CorSaida => Color.FromArgb(0xFF, 0xC8, 0xD3, 0xE0);   // = ChartB do SecurityTheme

    public SecurityPage()
    {
        ViewModel = App.Services.GetRequiredService<SecurityViewModel>();
        InitializeComponent();
        Auditar.Command = ViewModel.AuditarCommand;
        ViewModel.GruposMudou += (_, _) => DispatcherQueue.TryEnqueue(AtualizarGrupos);
        AtualizarGrupos();
        Loaded += (_, _) =>
        {
            ViewModel.PropertyChanged += Vm_PropertyChanged;
            ViewModel.Live.Tick += Live_Tick;
                if (ViewModel.AoVivo) ViewModel.Live.Start(DispatcherQueue);
            Desenhar();
        };
        Unloaded += (_, _) =>
        {
            ViewModel.Live.Stop();   // telemetria so roda com a tela aberta
            ViewModel.Live.Tick -= Live_Tick;
            ViewModel.PropertyChanged -= Vm_PropertyChanged;
        };
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        try { await ViewModel.EnsureLoadedAsync(); }
        catch (Exception ex) { App.LogCrash("SecurityPage", ex); }
    }

    private void Vm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SecurityViewModel.Cautela):
                        break;
            case nameof(SecurityViewModel.Selecionado):
                if (ViewModel.Selecionado is { } sel && !ReferenceEquals(ListAchados.SelectedItem, sel))
                {
                    _sync = true;
                    ListAchados.SelectedItem = sel;
                    ListAchados.ScrollIntoView(sel);
                    _sync = false;
                }
                break;
            case nameof(SecurityViewModel.Aba):
                Desenhar();
                break;
            case nameof(SecurityViewModel.AoVivo):
                if (ViewModel.AoVivo) ViewModel.Live.Start(DispatcherQueue); else ViewModel.Live.Stop();
                break;
        }
    }

    private void Live_Tick(object? sender, EventArgs e) => Desenhar();

    // conexoes = verde "vivo" do scope do kit; nascimentos = ciano (ambar/magenta ficam reservados pra estado)
    private void Desenhar()
    {
        var l = ViewModel.Live;
        var rede = new ScopeChart.Serie[] { new("entrada", CorEntrada, l.RedeIn), new("saída", CorSaida, l.RedeOut) };
        var conex = new ScopeChart.Serie[] { new("conexões", SegColors.Cyan, l.Conexoes) };
        ScopeRede.Set(rede, SecurityViewModel.Kb);
        ScopeConexoes.Set(conex, v => v.ToString("0"));

        ScopeProcessos.Set([new("nasceram", SegColors.Cyan, l.Nascimentos, l.NomesPorSegundo)], v => v.ToString("0"), barras: true);
    }

    private void IrLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string t }) ViewModel.Ir(t);
    }

    private void Ir_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string t }) ViewModel.Ir(t);
    }

    private void AlertaVer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SecAlert a } && a.TemAchado) ViewModel.Ir("achados:" + a.FiltroAchados);
    }

    private void AlertaVisto_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SecAlert a }) ViewModel.AlertaVistoCommand.Execute(a);
    }

    private void AlertaSilenciar_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SecAlert a }) ViewModel.AlertaSilenciarCommand.Execute(a);
    }

    private void AbrirWin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SecRow { Abrir.Length: > 0 } row }) ViewModel.AbrirUrl(row.Abrir);
    }

    private bool _sync;
    private readonly Microsoft.UI.Xaml.Data.CollectionViewSource _achadosCvs = new() { IsSourceGrouped = true };
    private readonly Microsoft.UI.Xaml.Data.CollectionViewSource _atencaoCvs = new() { IsSourceGrouped = true };

    /// <summary>Listas agrupadas (cabecalho + linha divisoria por origem): o CollectionViewSource recebe os grupos prontos.</summary>
    private void AtualizarGrupos()
    {
        var sel = ViewModel.Selecionado;
        _sync = true;
        _achadosCvs.Source = ViewModel.AchadosGrupos;
        _atencaoCvs.Source = ViewModel.AtencaoGrupos;
        ListAchados.ItemsSource = _achadosCvs.View;
        ListAtencao.ItemsSource = _atencaoCvs.View;
        if (sel is not null) ListAchados.SelectedItem = ViewModel.AchadosGrupos.SelectMany(g => g).FirstOrDefault(r => r.Kind == sel.Kind && r.Key == sel.Key);
        _sync = false;
    }

    /// <summary>Master-detail: selecionar uma linha mostra o painel (o que e, risco, o que fazer, nota, acoes).</summary>
    private void Achados_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_sync) return;
        if (ListAchados.SelectedItem is SecRow r) ViewModel.Selecionado = r;   // lista recarregada (null) nao apaga o painel
    }

    private void Atencao_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SecRow r) ViewModel.IrAoAchado(r);
    }

    private void Nota_LostFocus(object sender, RoutedEventArgs e) => ViewModel.SalvarNota();

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Microsoft.UI.Xaml.Controls.Primitives.ToggleButton { Tag: string t } tb) return;
        ViewModel.Aba = int.Parse(t);
        tb.IsChecked = true;   // clicar na aba ativa nao pode desligar o toggle
    }

    /// <summary>POR QUE?: parecer local (instantaneo, de graca) + a acao sugerida + opcao de perguntar ao squad.</summary>
    private async void PorQue_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: SecRow row }) return;
        if (ViewModel.Aba1) ViewModel.Selecionado = row; else ViewModel.IrAoAchado(row);   // o "?" leva ao painel de detalhe
    }

    private async void PerguntarSquad_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SecRow row }) await ViewModel.PerguntarAsync(row);
    }

    /// <summary>A acao principal da linha: a que o parecer recomenda (IGNORAR, DESLIGAR, BLOQUEAR, LIMPAR, RELIGAR…).</summary>
    private async void Primario_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: SecRow row }) return;
        switch (row.PrimariaAcao)
        {
            case "confiar": ViewModel.AprovarCommand.Execute(row); break;
            case "cortar": await CortarComDialogoAsync(row); break;
            case "reverter": ViewModel.ReverterCommand.Execute(row); break;
            case "abrir": ViewModel.AbrirUrl(row.Abrir); break;
            case "porque": if (ViewModel.Aba1) ViewModel.Selecionado = row; else ViewModel.IrAoAchado(row); break;
        }
    }

    private async Task MostrarPorQueAsync(SecRow row)
    {
        var p = Factory.Services.SecKnowledge.Explicar(row);
        var t = new StackPanel { Spacing = 10, MaxWidth = 560 };
        void Bloco(string rotulo, string texto)
        {
            t.Children.Add(new TextBlock { Text = rotulo, Style = (Style)Application.Current.Resources["CyberLabel"] });
            t.Children.Add(new TextBlock { Text = texto, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        }
        Bloco("O QUE É", p.Oque);
        Bloco("RISCO", p.Risco);
        Bloco("O QUE EU FARIA", p.Sugestao);
        t.Children.Add(new TextBlock { Text = row.Detalhe, TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 11, IsTextSelectionEnabled = true });
        t.Children.Add(new TextBlock { Text = "Parecer local do Factory: é opinião, não medição. PERGUNTAR AO SQUAD consulta o Claude (gasta assinatura).", TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 11 });

        var acaoTxt = p.Acao == "confiar" && row.PodeAprovar ? "IGNORAR" : p.Acao == "cortar" && row.PodeCortar ? row.Corte : null;
        var dlg = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = $"O que é {row.Titulo}?", Content = new ScrollViewer { Content = t, MaxHeight = 460 },
            PrimaryButtonText = acaoTxt ?? "", IsPrimaryButtonEnabled = acaoTxt is not null,
            SecondaryButtonText = "PERGUNTAR AO SQUAD", CloseButtonText = "FECHAR", DefaultButton = ContentDialogButton.Close,
        };
        var r = await dlg.ShowAsync();
        if (r == ContentDialogResult.Secondary) await ViewModel.PerguntarAsync(row);
        else if (r == ContentDialogResult.Primary)
        {
            if (p.Acao == "confiar") ViewModel.AprovarCommand.Execute(row);
            else await CortarComDialogoAsync(row);
        }
    }

    private void Aprovar_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SecRow row }) ViewModel.AprovarCommand.Execute(row);
    }

    private void Selo_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SecBadge b }) ViewModel.AbrirSeloCommand.Execute(b);
    }

    private void Reverter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SecRow row }) ViewModel.ReverterCommand.Execute(row);
    }

    /// <summary>DESLIGAR/BLOQUEAR/LIMPAR sempre pergunta antes, dizendo exatamente o que acontece e como desfazer.</summary>
    private async void Cortar_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SecRow row }) await CortarComDialogoAsync(row);
    }

    private async Task CortarComDialogoAsync(SecRow row)
    {
        var dlg = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"{row.Corte} {row.Titulo}?",
            Content = new TextBlock { Text = row.CorteDica + "\n\n" + row.Detalhe, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 },
            PrimaryButtonText = row.Corte,
            CloseButtonText = "CANCELAR",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dlg.ShowAsync() == ContentDialogResult.Primary) await ViewModel.CortarAsync(row);
    }

    private void Inspecionar_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SecRow row }) ViewModel.InspecionarCommand.Execute(row);
    }
}
