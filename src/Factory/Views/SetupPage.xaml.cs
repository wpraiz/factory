using Factory.Models;
using Factory.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Factory.Views;

public sealed partial class SetupPage : Page
{
    public SetupViewModel ViewModel { get; }

    public SetupPage()
    {
        ViewModel = App.Services.GetRequiredService<SetupViewModel>();
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        // busca e o 1o gesto ("onde esta X?"): foco ja no campo (ux-review)
        if (ViewModel.MostraSetup) BuscaBox.Focus(FocusState.Programmatic);
        try { await ViewModel.AbrirAsync(); }
        catch (Exception ex) { App.LogCrash("SetupPage", ex); }
    }

    private void Filtro_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SetupFiltro f }) return;
        if (ViewModel.Tipos.Contains(f)) ViewModel.EscolherTipo(f);
        else ViewModel.EscolherSelo(f);
    }

    private void Joia_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SetupItem item }) ViewModel.AlternarJoia(item);
    }

    private void Pasta_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string caminho }) ViewModel.AbrirPasta(caminho);
    }
}
