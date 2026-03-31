using BengiDevTools.ViewModels;

namespace BengiDevTools.Views;

public partial class AppsPage : ContentPage
{
    private readonly AppsViewModel _vm;

    public AppsPage(AppsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.RefreshGitStatusCommand.Execute(null);
    }
}
