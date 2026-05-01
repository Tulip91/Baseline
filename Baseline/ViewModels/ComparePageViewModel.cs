using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using BaseLine.Core;
using BaseLine.Infrastructure;
using BaseLine.Services;

namespace BaseLine.ViewModels;

public sealed class ComparePageViewModel : PageViewModelBase
{
    private readonly BaselineWorkflowService _workflowService;
    private readonly WorkspaceState _workspaceState;
    private readonly IMessageDialogService _messageDialogService;
    private string _selectedCategoryFilter = "All";
    private bool _showOnlyMismatches;

    public ComparePageViewModel(BaselineWorkflowService workflowService, WorkspaceState workspaceState, IMessageDialogService messageDialogService)
        : base("Compare", "Compare the loaded profile against the current machine before applying anything.")
    {
        _workflowService = workflowService;
        _workspaceState = workspaceState;
        _messageDialogService = messageDialogService;

        Rows = new ObservableCollection<CompareRowViewModel>();
        CategoryFilters = new ObservableCollection<string>(new[] { "All" });
        Summary = new CompareSummaryViewModel();
        RowsView = CollectionViewSource.GetDefaultView(Rows);
        RowsView.Filter = FilterRow;

        RunCompareCommand = new AsyncRelayCommand(RunCompareAsync, () => !IsBusy && _workspaceState.LoadedProfile is not null);
        _workspaceState.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WorkspaceState.CurrentCompareReport))
            {
                RebuildRows();
            }
            else if (e.PropertyName == nameof(WorkspaceState.LoadedProfile))
            {
                RunCompareCommand.NotifyCanExecuteChanged();
            }
        };
    }

    public ObservableCollection<CompareRowViewModel> Rows { get; }
    public ObservableCollection<string> CategoryFilters { get; }
    public ICollectionView RowsView { get; }
    public CompareSummaryViewModel Summary { get; }
    public AsyncRelayCommand RunCompareCommand { get; }

    public string SelectedCategoryFilter
    {
        get => _selectedCategoryFilter;
        set
        {
            if (SetProperty(ref _selectedCategoryFilter, value))
            {
                RowsView.Refresh();
            }
        }
    }

    public bool ShowOnlyMismatches
    {
        get => _showOnlyMismatches;
        set
        {
            if (SetProperty(ref _showOnlyMismatches, value))
            {
                RowsView.Refresh();
            }
        }
    }

    private async Task RunCompareAsync()
    {
        if (_workspaceState.LoadedProfile is null)
        {
            _messageDialogService.ShowError("Load or capture a profile first.", "Compare");
            return;
        }

        IsBusy = true;
        StatusMessage = "Comparing profile against current machine...";
        try
        {
            var report = await _workflowService.CompareAsync(_workspaceState.LoadedProfile);
            _workspaceState.CurrentCompareReport = report;

            var actionableCount = report.Items.Count(item => item.Status is BaselineStatus.Ready or BaselineStatus.Warning or BaselineStatus.MissingDependency or BaselineStatus.Failed);
            _workspaceState.StatusText = $"Compared {_workspaceState.LoadedProfile.Metadata.ProfileName} against the current machine.";
            StatusMessage = report.Messages.LastOrDefault() ?? $"Comparison ready. {actionableCount} items need attention.";
        }
        catch (Exception ex)
        {
            StatusMessage = "Compare failed.";
            _workspaceState.StatusText = "Compare failed.";
            _messageDialogService.ShowError(ex.Message, "Compare failed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RebuildRows()
    {
        Rows.Clear();
        CategoryFilters.Clear();
        CategoryFilters.Add("All");

        var report = _workspaceState.CurrentCompareReport;
        if (report is null)
        {
            return;
        }

        foreach (var item in report.Items.OrderBy(item => item.Category).ThenBy(item => item.DisplayName))
        {
            Rows.Add(new CompareRowViewModel(item));
        }

        foreach (var category in Rows.Select(row => row.Item.Category.ToString()).Distinct())
        {
            CategoryFilters.Add(category);
        }

        Summary.ReadyCount = Rows.Count(row => row.Item.Status == BaselineStatus.Ready);
        Summary.MatchedCount = Rows.Count(row => row.Item.Status == BaselineStatus.AlreadyMatches);
        Summary.WarningCount = Rows.Count(row => row.Item.Status is BaselineStatus.Warning or BaselineStatus.MissingDependency);
        Summary.UnsupportedCount = Rows.Count(row => row.Item.Status == BaselineStatus.Unsupported);

        RowsView.Refresh();
    }

    private bool FilterRow(object obj)
    {
        if (obj is not CompareRowViewModel row)
        {
            return false;
        }

        if (SelectedCategoryFilter != "All" && row.Item.Category.ToString() != SelectedCategoryFilter)
        {
            return false;
        }

        if (ShowOnlyMismatches && row.Item.Status == BaselineStatus.AlreadyMatches)
        {
            return false;
        }

        return true;
    }
}
