using Factory.Models;
using Factory.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Factory.Views;

public sealed partial class ContextsPage : Page
{
    public ContextsViewModel ViewModel { get; }

    public ContextsPage()
    {
        ViewModel = App.Services.GetRequiredService<ContextsViewModel>();
        InitializeComponent();
        ViewModel.Rebuild();
    }

    private void OpenClaude_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ProjectInfo p }) ViewModel.OpenClaude(p);
    }
}
