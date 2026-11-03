using Factory.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace Factory.Views;

public sealed partial class PaletteView : UserControl
{
    public PaletteViewModel ViewModel { get; }

    public PaletteView()
    {
        ViewModel = App.Services.GetRequiredService<PaletteViewModel>();
        InitializeComponent();
    }

    /// <summary>Chamar depois do Popup abrir: foco antes de estar na arvore nao pega.</summary>
    public void FocusQuery()
    {
        QueryBox.Focus(FocusState.Programmatic);
        QueryBox.SelectAll();
    }

    private void QueryBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(CoreVirtualKeyStates.Down);
        switch (e.Key)
        {
            case VirtualKey.Down:
                ViewModel.Move(+1);
                ResultList.ScrollIntoView(ResultList.SelectedItem);
                e.Handled = true;
                break;
            case VirtualKey.Up:
                ViewModel.Move(-1);
                ResultList.ScrollIntoView(ResultList.SelectedItem);
                e.Handled = true;
                break;
            case VirtualKey.Enter:
                ViewModel.Execute();
                e.Handled = true;
                break;
            case VirtualKey.Escape:
                ViewModel.Close();
                e.Handled = true;
                break;
            case VirtualKey.K when ctrl:
                ViewModel.Close();
                e.Handled = true;
                break;
        }
    }

    private void ResultList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PaletteItem item) ViewModel.Execute(item);
    }
}
