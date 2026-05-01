using System.Collections.ObjectModel;
using System.ComponentModel;
using BaseLine.Core;
using BaseLine.Infrastructure;
using BaseLine.Services;

namespace BaseLine.ViewModels;

public sealed class ApplyPageViewModel : PageViewModelBase
{
    private readonly BaselineWorkflowService _workflowService;
    private readonly WorkspaceState _workspaceState;
    private readonly IMessageDialogService _messageDialogService;

    public ApplyPageViewModel(BaselineWorkflowService workflowService, WorkspaceState workspaceState, IMessageDialogService messageDialogService)
        : base("Apply", "Select mismatches, create rollback state, and apply changes in a controlled pass.")
    {
        _workflowService = workflowService;
        _workspaceState = workspaceState;
        _messageDialogService = messageDialogService;

        Rows = new ObservableCollection<CompareRowViewModel>();
        Results = new ObservableCollection<ApplyResultItem>();

        SelectAllCommand = new RelayCommand(SelectAll, CanSelectRows);
        SelectMismatchesCommand = new RelayCommand(SelectMismatches, CanSelectRows);
        ClearSelectionCommand = new RelayCommand(ClearSelection, CanSelectRows);
        ApplySelectedCommand = new AsyncRelayCommand(ApplySelectedAsync, CanApplySelected);

        _workspaceState.PropertyChanged += WorkspaceStateOnPropertyChanged;
    }

    public ObservableCollection<CompareRowViewModel> Rows { get; }
    public ObservableCollection<ApplyResultItem> Results { get; }

    public RelayCommand SelectAllCommand { get; }
    public RelayCommand SelectMismatchesCommand { get; }
    public RelayCommand ClearSelectionCommand { get; }
    public AsyncRelayCommand ApplySelectedCommand { get; }

    public override Task InitializeAsync()
    {
        RebuildRows();
        return Task.CompletedTask;
    }

    private async Task ApplySelectedAsync()
    {
        if (_workspaceState.LoadedProfile is null)
        {
            _messageDialogService.ShowError("Load or capture a profile first.", "Apply");
            return;
        }

        if (_workspaceState.CurrentCompareReport is null)
        {
            _messageDialogService.ShowError("Run compare before applying changes.", "Apply");
            return;
        }

        var itemsToApply = Rows.Where(row => row.IsSelected).Select(row => row.Item).ToList();
        if (itemsToApply.Count == 0)
        {
            _messageDialogService.ShowError("Select at least one item to apply.", "Apply");
            return;
        }

        IsBusy = true;
        NotifyCommandState();
        StatusMessage = "Creating rollback state and applying selected changes...";
        Results.Clear();

        try
        {
            var session = await _workflowService.ApplyAsync(_workspaceState.LoadedProfile, itemsToApply);
            foreach (var result in session.Results)
            {
                Results.Add(result);
            }

            _workspaceState.CurrentCompareReport = await _workflowService.CompareAsync(_workspaceState.LoadedProfile);
            _workspaceState.SetRollbackHistory(await _workflowService.LoadRollbackHistoryAsync());
            _workspaceState.StatusText = $"Apply session completed for {_workspaceState.LoadedProfile.Metadata.ProfileName}.";
            StatusMessage = $"Applied {session.Results.Count(result => result.Status == BaselineStatus.Applied)} items, skipped {session.Results.Count(result => result.Status == BaselineStatus.Skipped)}, failed {session.Results.Count(result => result.Status == BaselineStatus.Failed)}.";
        }
        catch (Exception ex)
        {
            StatusMessage = "Apply failed.";
            _messageDialogService.ShowError(ex.Message, "Apply failed");
        }
        finally
        {
            IsBusy = false;
            NotifyCommandState();
        }
    }

    private void WorkspaceStateOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspaceState.CurrentCompareReport))
        {
            RebuildRows();
        }
        else if (e.PropertyName == nameof(WorkspaceState.LoadedProfile))
        {
            NotifyCommandState();
        }
    }

    private void RebuildRows()
    {
        foreach (var row in Rows)
        {
            row.PropertyChanged -= CompareRowOnPropertyChanged;
        }

        Rows.Clear();
        var report = _workspaceState.CurrentCompareReport;
        if (report is null)
        {
            NotifyCommandState();
            return;
        }

        foreach (var item in report.Items.OrderBy(item => item.Category).ThenBy(item => item.DisplayName))
        {
            var row = new CompareRowViewModel(item, item.Status == BaselineStatus.Ready);
            row.PropertyChanged += CompareRowOnPropertyChanged;
            Rows.Add(row);
        }

        NotifyCommandState();
    }

    private void SelectAll()
    {
        foreach (var row in Rows)
        {
            row.IsSelected = row.Item.Status is BaselineStatus.Ready or BaselineStatus.Warning;
        }
    }

    private void SelectMismatches()
    {
        foreach (var row in Rows)
        {
            row.IsSelected = row.Item.Status == BaselineStatus.Ready;
        }
    }

    private void ClearSelection()
    {
        foreach (var row in Rows)
        {
            row.IsSelected = false;
        }
    }

    private void CompareRowOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CompareRowViewModel.IsSelected))
        {
            NotifyCommandState();
        }
    }

    private bool CanSelectRows() => !IsBusy && Rows.Count > 0;

    private bool CanApplySelected()
    {
        return !IsBusy &&
               _workspaceState.LoadedProfile is not null &&
               _workspaceState.CurrentCompareReport is not null &&
               Rows.Any(row => row.IsSelected);
    }

    private void NotifyCommandState()
    {
        SelectAllCommand.NotifyCanExecuteChanged();
        SelectMismatchesCommand.NotifyCanExecuteChanged();
        ClearSelectionCommand.NotifyCanExecuteChanged();
        ApplySelectedCommand.NotifyCanExecuteChanged();
    }
}
