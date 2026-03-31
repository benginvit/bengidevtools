using BengiDevTools.ViewModels;

namespace BengiDevTools.Views;

public partial class BuildPage : ContentPage
{
    private readonly BuildViewModel _vm;

    public BuildPage(BuildViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;

        vm.PropertyChanged += async (_, e) =>
        {
            if (e.PropertyName == nameof(BuildViewModel.BuildLog))
                await LogScrollView.ScrollToAsync(0, double.MaxValue, false);
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (!_vm.BuildTargets.Any())
            _vm.DiscoverRepos();
    }
}
