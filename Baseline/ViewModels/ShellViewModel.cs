using System.Collections.ObjectModel;
using BaseLine.Infrastructure;
using BaseLine.Services;

namespace BaseLine.ViewModels;

public sealed class ShellViewModel : ObservableObject
{
    private readonly WorkspaceState _workspaceState;
    private PageViewModelBase _currentPage;

    public ShellViewModel(
        WorkspaceState workspaceState,
        CapturePageViewModel capturePage,
        ProfilesPageViewModel profilesPage,
        ComparePageViewModel comparePage,
        ApplyPageViewModel applyPage,
        RollbackPageViewModel rollbackPage)
    {
        _workspaceState = workspaceState;

        NavigationItems = new ObservableCollection<NavigationItemViewModel>
        {
            new("Capture", "Build a profile from the current machine", capturePage, SelectPage),
            new("Snapshots", "Load and review saved profiles", profilesPage, SelectPage),
            new("Compare", "Inspect differences before execution", comparePage, SelectPage),
            new("Replicate", "Apply selected changes with rollback", applyPage, SelectPage),
            new("Rollback", "Restore app-managed changes", rollbackPage, SelectPage)
        };

        _currentPage = capturePage;
        NavigationItems[0].IsSelected = true;
        _workspaceState.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WorkspaceState.StatusText))
            {
                OnPropertyChanged(nameof(StatusText));
            }
        };
    }

    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; }

    public PageViewModelBase CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

    public string StatusText => _workspaceState.StatusText;

    public async Task InitializeAsync()
    {
        foreach (var item in NavigationItems)
        {
            await item.Page.InitializeAsync();
        }
    }

    private void SelectPage(NavigationItemViewModel item)
    {
        foreach (var navigationItem in NavigationItems)
        {
            navigationItem.IsSelected = navigationItem == item;
        }

        CurrentPage = item.Page;
    }
}
