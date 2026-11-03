using System.Numerics;
using Factory.Models;
using Factory.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Windows.Storage.Pickers;
using Windows.System;

namespace Factory.Views;

/// <summary>
/// Code-behind so pra movimento (hover/press/entrada), atalhos e o que exige janela (file picker,
/// dialogo). Toda decisao esta no AppsViewModel.
/// </summary>
public sealed partial class AppsPage : Page
{
    public AppsViewModel ViewModel { get; }

    private readonly DispatcherTimer _pulse = new() { Interval = TimeSpan.FromSeconds(6) };
    private DateTime _openedAt;

    public AppsPage()
    {
        ViewModel = App.Services.GetRequiredService<AppsViewModel>();
        InitializeComponent();
        _pulse.Tick += async (_, _) => await ViewModel.RefreshRunningAsync();
        Loaded += (_, _) => { _openedAt = DateTime.Now; _pulse.Start(); _ = ViewModel.RefreshRunningAsync(); };
        Unloaded += (_, _) => _pulse.Stop();
        // entrar em ESCOLHER: leva o painel pra tela e poe o cursor no filtro (sem clique extra)
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(AppsViewModel.IsEditing) || !ViewModel.IsEditing) return;
            DispatcherQueue.TryEnqueue(() => { EditCard.StartBringIntoView(); FilterBox.Focus(FocusState.Programmatic); });
        };
    }

    // ---------- clique / teclado ----------

    private static AppTile? TileOf(object sender) => (sender as FrameworkElement)?.Tag as AppTile;

    private void Tile_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (ViewModel.IsEditing) return; // em ESCOLHER, so os controles do tile agem
        ViewModel.LaunchCommand.Execute(TileOf(sender));
    }

    private void Tile_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is not (VirtualKey.Enter or VirtualKey.Space)) return;
        if (!ViewModel.IsEditing) ViewModel.LaunchCommand.Execute(TileOf(sender));
        e.Handled = true;
    }

    private void Hotkey_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        var n = sender.Key - VirtualKey.Number0;
        if (n is >= 1 and <= 9) { ViewModel.LaunchIndex(n); args.Handled = true; }
    }

    // ---------- botao direito ----------

    private void MenuOpen_Click(object sender, RoutedEventArgs e) { if (TileOf(sender) is { } t) ViewModel.LaunchCommand.Execute(t); }
    private void MenuFolder_Click(object sender, RoutedEventArgs e) { if (TileOf(sender) is { } t) ViewModel.OpenFolder(t); }
    private void MenuTerminal_Click(object sender, RoutedEventArgs e) { if (TileOf(sender) is { } t) ViewModel.OpenTerminal(t); }
    private void MenuClaude_Click(object sender, RoutedEventArgs e) { if (TileOf(sender) is { } t) ViewModel.OpenClaude(t); }
    private void MenuCopy_Click(object sender, RoutedEventArgs e) { if (TileOf(sender) is { } t) ViewModel.CopyPath(t); }

    // ---------- escolher ----------

    private void MoveUp_Click(object sender, RoutedEventArgs e) => ViewModel.MoveUpCommand.Execute(TileOf(sender));
    private void MoveDown_Click(object sender, RoutedEventArgs e) => ViewModel.MoveDownCommand.Execute(TileOf(sender));
    private void Remove_Click(object sender, RoutedEventArgs e) => ViewModel.RemoveCommand.Execute(TileOf(sender));

    private void Group_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: string name } cb && TileOf(cb) is { } t) ViewModel.Regroup(t, name);
    }

    private void Group_TextSubmitted(ComboBox sender, ComboBoxTextSubmittedEventArgs args)
    {
        if (TileOf(sender) is { } t) ViewModel.Regroup(t, args.Text);
        args.Handled = true;
    }

    private void Candidate_Click(object sender, RoutedEventArgs e)
    {
        if (TileOf(sender) is { } t) ViewModel.ToggleCandidate(t);
    }

    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Voltar ao grid padrao?",
            Content = $"Substitui os {ViewModel.Tiles.Count} apps que voce escolheu pelo conjunto inicial (projetos que existem no disco). O apps.json atual vira apps.json.bak.",
            PrimaryButtonText = "SUBSTITUIR",
            CloseButtonText = "CANCELAR",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dlg.ShowAsync() == ContentDialogResult.Primary) ViewModel.ResetToSeedCommand.Execute(null);
    }

    private async void AddFile_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.Desktop };
        foreach (var ext in new[] { ".exe", ".lnk", ".cmd", ".bat", ".ps1", ".py", ".url" }) picker.FileTypeFilter.Add(ext);
        if (App.Window is null) return;
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.Window));
        var file = await picker.PickSingleFileAsync();
        if (file is not null) ViewModel.AddFromFile(file.Path);
    }

    // ---------- movimento: 150–350 ms, so o que ajuda a entender o toque ----------

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

    // hover: escala 1.03 + borda na cor da faixa (reforca o grupo sem elemento novo)
    private void Tile_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (ViewModel.IsEditing) return;
        Spring((UIElement)sender, 1.03f);
        if (sender is Border b && TileOf(b) is { } t)
            b.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)((Microsoft.UI.Xaml.Data.IValueConverter)Application.Current.Resources["AccentBrush"]).Convert(t.Accent, typeof(object), null!, "");
    }

    private void Tile_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        Spring((UIElement)sender, 1f);
        if (sender is Border b) b.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CyberPanelEdge"];
    }
    private void Tile_PointerPressed(object sender, PointerRoutedEventArgs e) { if (!ViewModel.IsEditing) Spring((UIElement)sender, 0.97f); }
    private void Tile_PointerReleased(object sender, PointerRoutedEventArgs e) { if (!ViewModel.IsEditing) Spring((UIElement)sender, 1.03f); }

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
