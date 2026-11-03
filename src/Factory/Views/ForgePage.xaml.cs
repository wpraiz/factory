using Factory.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace Factory.Views;

public sealed partial class ForgePage : Page
{
    public ForgeViewModel ViewModel { get; }

    public ForgePage()
    {
        ViewModel = App.Services.GetRequiredService<ForgeViewModel>();
        InitializeComponent();
    }
}
