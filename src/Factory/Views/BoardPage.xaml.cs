using Factory.Models;
using Factory.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace Factory.Views;

public sealed partial class BoardPage : Page
{
    public BoardViewModel ViewModel { get; }

    public BoardPage()
    {
        ViewModel = App.Services.GetRequiredService<BoardViewModel>();
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        try { await ViewModel.AbrirAsync(); }
        catch (Exception ex) { App.LogCrash("BoardPage", ex); }
    }

    private void Filtro_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SetupFiltro f }) ViewModel.EscolherFonte(f);
    }

    private void Bom_Click(object sender, RoutedEventArgs e) => Decidir(sender, "bom");
    private void Ruim_Click(object sender, RoutedEventArgs e) => Decidir(sender, "ruim");
    private void Depois_Click(object sender, RoutedEventArgs e) => Decidir(sender, "depois");

    private void Decidir(object sender, string decisao)
    {
        if (sender is FrameworkElement { Tag: BoardCard card }) ViewModel.Decidir(card, decisao);
    }

    private void Fonte_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: BoardCard card }) ViewModel.AbrirFonte(card);
    }

    private void NotaToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: BoardCard card }) card.NotaAberta = !card.NotaAberta;
    }

    // hover dos tiles: mola 1.02 + aresta ciano (mesma regua do APPS/VIDA: ui-guide "Movimento")
    private void Tile_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        Mola((UIElement)sender, 1.02f);
        if (sender is Border b) b.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["NeonCyanGlow"];
    }

    private void Tile_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        Mola((UIElement)sender, 1f);
        if (sender is Border b) b.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CyberPanelEdge"];
    }

    private static void Mola(UIElement el, float s)
    {
        var v = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(el);
        v.CenterPoint = new System.Numerics.Vector3(el.ActualSize.X / 2f, el.ActualSize.Y / 2f, 0);
        var mola = v.Compositor.CreateSpringVector3Animation();
        mola.FinalValue = new System.Numerics.Vector3(s, s, 1f);
        mola.DampingRatio = 0.75f;
        mola.Period = TimeSpan.FromMilliseconds(45);
        v.StartAnimation("Scale", mola);
    }

    /// <summary>Ctrl+Enter na nota = BOM (acao mais comum sem tirar a mao do teclado).</summary>
    private void Nota_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (!ctrl) return;
        e.Handled = true;
        Decidir(sender, "bom");
    }
}
