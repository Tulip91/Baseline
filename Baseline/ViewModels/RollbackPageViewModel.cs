using System.Collections.ObjectModel;
using BaseLine.Infrastructure;
using BaseLine.Services;

namespace BaseLine.ViewModels;

public sealed class RollbackPageViewModel : PageViewModelBase
{
    private readonly BaselineWorkflowService _workflowService;
    private readonly WorkspaceState _workspaceState;
    private readonly IMessageDialogService _messageDialogService;
    private Core.RollbackRecord? _selectedRecord;

    public RollbackPageViewModel(BaselineWorkflowService workflowService, WorkspaceState workspaceState, IMessageDialogService messageDialogService)
        : base("Rollback", "Restore the previous state captured before an apply session changed the machine.")
    {
        _workflowService = workflowService;
        _workspaceState = workspaceState;
        _messageDialogService = messageDialogService;

        Records = new ObservableCollection<Core.RollbackRecord>();
        Items = new ObservableCollection<SimpleRecordViewModel>();

        RefreshHistoryCommand = new AsyncRelayCommand(RefreshHistoryAsync, () => !IsBusy);
        RollbackSelectedCommand = new AsyncRelayCommand(RollbackSelectedAsync, () => !IsBusy && SelectedRecord is not null);
    }

    public ObservableCollection<Core.RollbackRecord> Records { get; }
    public ObservableCollection<SimpleRecordViewModel> Items { get; }

    public AsyncRelayCommand RefreshHistoryCommand { get; }
    public AsyncRelayCommand RollbackSelectedCommand { get; }

    public Core.RollbackRecord? SelectedRecord
    {
        get => _selectedRecord;
        set
        {
            if (SetProperty(ref _selectedRecord, value))
            {
                RebuildItems();
                RollbackSelectedCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public override async Task InitializeAsync()
    {
        await RefreshHistoryAsync();
    }

    private async Task RefreshHistoryAsync()
    {
        IsBusy = true;
        try
        {
            _workspaceState.SetRollbackHistory(await _workflowService.LoadRollbackHistoryAsync());
            Records.Clear();
            foreach (var item in _workspaceState.RollbackHistory)
            {
                Records.Add(item);
            }

            SelectedRecord ??= Records.FirstOrDefault();
            StatusMessage = Records.Count == 0 ? "No rollback sessions recorded yet." : $"Loaded {Records.Count} rollback sessions.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RollbackSelectedAsync()
    {
        if (SelectedRecord is null)
        {
            return;
        }

        if (!_messageDialogService.Confirm("Restore this app-managed rollback session?", "Confirm rollback"))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var results = await _workflowService.RollbackAsync(SelectedRecord);
            if (_workspaceState.LoadedProfile is not null)
            {
                _workspaceState.CurrentCompareReport = await _workflowService.CompareAsync(_workspaceState.LoadedProfile);
            }

            StatusMessage = $"Rollback complete. Restored {results.Count(result => result.Status == Core.BaselineStatus.RolledBack)} items.";
            _workspaceState.StatusText = $"Rollback restored session {SelectedRecord.SessionId}.";
            RebuildItems(results);
        }
        catch (Exception ex)
        {
            StatusMessage = "Rollback failed.";
            _messageDialogService.ShowError(ex.Message, "Rollback failed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RebuildItems(IEnumerable<Core.ApplyResultItem>? results = null)
    {
        Items.Clear();
        if (results is not null)
        {
            foreach (var result in results)
            {
                Items.Add(new SimpleRecordViewModel(result.DisplayName, result.Category.ToString(), $"{result.Status}: {result.Message}"));
            }

            return;
        }

        if (SelectedRecord is null)
        {
            return;
        }

        foreach (var item in SelectedRecord.Items)
        {
            Items.Add(new SimpleRecordViewModel(item.DisplayName, item.Category.ToString(), item.Kind.ToString()));
        }
    }
}
