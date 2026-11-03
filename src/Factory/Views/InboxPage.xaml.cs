using Factory.Models;
using Factory.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Factory.Views;

public sealed partial class InboxPage : Page
{
    public InboxViewModel ViewModel { get; }

    public InboxPage()
    {
        ViewModel = App.Services.GetRequiredService<InboxViewModel>();
        InitializeComponent();
        ViewModel.Rebuild();
    }

    private void DestinoBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput) ViewModel.FiltrarDestinos(sender.Text);
    }

    private void DestinoBox_GotFocus(object sender, RoutedEventArgs e)
    {
        ViewModel.FiltrarDestinos(DestinoBox.Text);
        DestinoBox.IsSuggestionListOpen = true;
    }

    // digitou o nome e deu Tab: resolve igual ao Enter (senao o destino fica vazio sem aviso)
    private void DestinoBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var t = DestinoBox.Text.Trim();
        if (t.Length > 0 && (ViewModel.Destino is null || ViewModel.Destino.Nome != t)) ViewModel.DestinoPorTexto(t);
    }

    private void DestinoBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is Destino d) ViewModel.EscolherDestino(d);
    }

    private void DestinoBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (args.ChosenSuggestion is Destino d) ViewModel.EscolherDestino(d);
        else ViewModel.DestinoPorTexto(args.QueryText);
    }

    private void Rodar_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: InboxItem i }) ViewModel.Rodar(i);
    }

    private void Abrir_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: InboxItem i }) ViewModel.Abrir(i);
    }

    private void Pasta_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: InboxItem i }) ViewModel.Pasta(i);
    }
}
