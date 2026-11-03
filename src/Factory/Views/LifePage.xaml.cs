using System.Numerics;
using Factory.Models;
using Factory.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace Factory.Views;

/// <summary>
/// Code-behind so pra movimento (hover/press/entrada), teclado e o drag (precisa do DataPackage do
/// evento). Toda decisao esta no LifeViewModel.
/// </summary>
public sealed partial class LifePage : Page
{
    public LifeViewModel ViewModel { get; }

    private DateTime _openedAt;

    public LifePage()
    {
        ViewModel = App.Services.GetRequiredService<LifeViewModel>();
        InitializeComponent();
        // relê o vida.json a cada entrada na secao: editou o JSON, voltou, esta atualizado
        Loaded += (_, _) => { _openedAt = DateTime.Now; ViewModel.Reload(); };
        KeyDown += Page_KeyDown;
    }

    private static LifeDoc? DocOf(object sender) => (sender as FrameworkElement)?.Tag as LifeDoc;

    // ---------- clique / teclado ----------

    private void Tile_Tapped(object sender, TappedRoutedEventArgs e) => ViewModel.OpenCommand.Execute(DocOf(sender));

    private void Tile_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var d = DocOf(sender);
        if (d is null) return;
        var ctrl = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        if (e.Key is VirtualKey.Enter or VirtualKey.Space) { ViewModel.OpenCommand.Execute(d); e.Handled = true; }
        else if (ctrl && e.Key == VirtualKey.C) { ViewModel.CopyFileCommand.Execute(d); e.Handled = true; }
    }

    private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.F5) { ViewModel.Reload(); e.Handled = true; }
    }

    // ---------- botao direito / mini-botoes ----------

    private void MenuOpen_Click(object sender, RoutedEventArgs e) { if (DocOf(sender) is { } d) ViewModel.OpenCommand.Execute(d); }
    private void MenuCopyFile_Click(object sender, RoutedEventArgs e) { if (DocOf(sender) is { } d) ViewModel.CopyFileCommand.Execute(d); }
    private void MenuCopyPath_Click(object sender, RoutedEventArgs e) { if (DocOf(sender) is { } d) ViewModel.CopyPath(d); }
    private void MenuFolder_Click(object sender, RoutedEventArgs e) { if (DocOf(sender) is { } d) ViewModel.OpenFolder(d); }

    // ---------- arrastar pra fora: o arquivo vai junto (Gmail, WhatsApp, campo de anexo) ----------

    private async void Tile_DragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if (DocOf(sender) is not { } d) { args.Cancel = true; return; }
        var deferral = args.GetDeferral();
        try
        {
            var file = await ViewModel.StorageFileAsync(d);
            if (file is null) { args.Cancel = true; ViewModel.DragStarted(d, false); return; }
            args.Data.SetStorageItems([file], readOnly: true);
            args.Data.SetText(d.FullPath);
            args.Data.RequestedOperation = DataPackageOperation.Copy;
            args.AllowedOperations = DataPackageOperation.Copy;
            args.DragUI.SetContentFromDataPackage();
            ViewModel.DragStarted(d, true);
        }
        catch (Exception ex) { args.Cancel = true; ViewModel.Status = $"arrastar: {ex.Message}"; }
        finally { deferral.Complete(); }
    }

    /// <summary>Soltou fora do app: o PointerExited nao dispara, entao o hover/escala voltam aqui.</summary>
    private void Tile_DropCompleted(UIElement sender, DropCompletedEventArgs args)
    {
        Spring(sender, 1f);
        if (sender is Border b)
        {
            if (DocOf(b) is { } d)
            {
                d.Hovered = false;
                ViewModel.Status = args.DropResult == DataPackageOperation.None ? $"{d.FileName}: soltou fora de um alvo (nada anexado)" : $"{d.FileName} entregue";
            }
            b.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CyberPanelEdge"];
        }
    }

    // ---------- movimento: 150–350 ms, so o que ajuda a entender o toque (mesmo padrao de APPS) ----------

    private void Tiles_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        // entrada em cascata so na primeira pintura da pagina (no scroll/reciclagem, nada de re-animar)
        if ((DateTime.Now - _openedAt).TotalMilliseconds > 900 && _openedAt != default) return;
        var el = args.Element;
        ElementCompositionPreview.SetIsTranslationEnabled(el, true);
        var v = ElementCompositionPreview.GetElementVisual(el);
        var c = v.Compositor;
        var delay = TimeSpan.FromMilliseconds(Math.Min(args.Index, 24) * 35);

        v.Opacity = 0f;
        var fade = c.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1f, 1f, c.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f)));
        fade.Duration = TimeSpan.FromMilliseconds(260);
        fade.DelayTime = delay;
        v.StartAnimation("Opacity", fade);

        v.Properties.InsertVector3("Translation", new Vector3(0, 14, 0));
        var rise = c.CreateVector3KeyFrameAnimation();
        rise.InsertKeyFrame(1f, Vector3.Zero, c.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f)));
        rise.Duration = TimeSpan.FromMilliseconds(320);
        rise.DelayTime = delay;
        v.StartAnimation("Translation", rise);
    }

    // hover: escala 1.03 + borda na cor da faixa + mini-botoes aparecem
    private void Tile_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        Spring((UIElement)sender, 1.03f);
        if (sender is Border b && DocOf(b) is { } d)
        {
            d.Hovered = true;
            b.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)((Microsoft.UI.Xaml.Data.IValueConverter)Application.Current.Resources["AccentBrush"]).Convert(d.Accent, typeof(object), null!, "");
        }
    }

    private void Tile_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        Spring((UIElement)sender, 1f);
        if (sender is Border b)
        {
            if (DocOf(b) is { } d) d.Hovered = false;
            b.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CyberPanelEdge"];
        }
    }

    private void Tile_PointerPressed(object sender, PointerRoutedEventArgs e) => Spring((UIElement)sender, 0.97f);
    private void Tile_PointerReleased(object sender, PointerRoutedEventArgs e) => Spring((UIElement)sender, 1.03f);

    private static void Spring(UIElement el, float s)
    {
        var v = ElementCompositionPreview.GetElementVisual(el);
        v.CenterPoint = new Vector3(el.ActualSize.X / 2f, el.ActualSize.Y / 2f, 0);
        var spring = v.Compositor.CreateSpringVector3Animation();
        spring.FinalValue = new Vector3(s, s, 1f);
        spring.DampingRatio = 0.75f;
        spring.Period = TimeSpan.FromMilliseconds(45);
        v.StartAnimation("Scale", spring);
    }
}
