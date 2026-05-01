using System.Collections.ObjectModel;
using System.ComponentModel;
using BaseLine.Core;
using BaseLine.Infrastructure;
using BaseLine.Services;

namespace BaseLine.ViewModels;

public sealed class ProfilesPageViewModel : PageViewModelBase
{
    private readonly BaselineWorkflowService _workflowService;
    private readonly WorkspaceState _workspaceState;
    private readonly IFileDialogService _fileDialogService;
    private readonly IMessageDialogService _messageDialogService;
    private string? _selectedRecentProfile;

    public ProfilesPageViewModel(
        BaselineWorkflowService workflowService,
        WorkspaceState workspaceState,
        IFileDialogService fileDialogService,
        IMessageDialogService messageDialogService)
        : base("Profiles", "Load an existing baseline profile and review its source metadata.")
    {
        _workflowService = workflowService;
        _workspaceState = workspaceState;
        _fileDialogService = fileDialogService;
        _messageDialogService = messageDialogService;

        RecentProfiles = new ObservableCollection<string>();
        OpenProfileCommand = new AsyncRelayCommand(OpenProfileAsync, () => !IsBusy);
        RefreshRecentCommand = new AsyncRelayCommand(RefreshRecentAsync, () => !IsBusy);
        OpenRecentProfileCommand = new AsyncRelayCommand(OpenRecentAsync, () => !IsBusy && SelectedRecentProfile is not null);

        _workspaceState.PropertyChanged += WorkspaceStateOnPropertyChanged;
        RefreshMetadata();
    }

    public ObservableCollection<string> RecentProfiles { get; }

    public AsyncRelayCommand OpenProfileCommand { get; }
    public AsyncRelayCommand RefreshRecentCommand { get; }
    public AsyncRelayCommand OpenRecentProfileCommand { get; }

    public string? SelectedRecentProfile
    {
        get => _selectedRecentProfile;
        set
        {
            if (SetProperty(ref _selectedRecentProfile, value))
            {
                OpenRecentProfileCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string ProfileSummary =>
        _workspaceState.LoadedProfile is null
            ? "No profile loaded."
            : $"{_workspaceState.LoadedProfile.Metadata.ProfileName}\n{_workspaceState.LoadedProfile.Metadata.SourceMachineName}\n{_workspaceState.LoadedProfile.Metadata.WindowsVersion}\nCreated {_workspaceState.LoadedProfile.Metadata.CreatedAt:g}";

    public string IncludedCategories =>
        _workspaceState.LoadedProfile is null
            ? string.Empty
            : string.Join(", ", _workspaceState.LoadedProfile.Categories.IncludedCategories);

    public override async Task InitializeAsync()
    {
        await RefreshRecentAsync();
        RefreshMetadata();
    }

    private async Task OpenProfileAsync()
    {
        var path = _fileDialogService.PickProfileToOpen();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await OpenPathAsync(path);
    }

    private async Task OpenRecentAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedRecentProfile))
        {
            return;
        }

        await OpenPathAsync(SelectedRecentProfile);
    }

    private async Task OpenPathAsync(string path)
    {
        IsBusy = true;
        try
        {
            var profile = await _workflowService.LoadProfileAsync(path);
            _workspaceState.LoadedProfile = profile;
            _workspaceState.LoadedProfilePath = path;
            _workspaceState.CurrentCompareReport = null;
            _workspaceState.StatusText = $"Loaded profile {profile.Metadata.ProfileName}.";
            StatusMessage = $"Loaded {profile.Metadata.ProfileName}.";
            SelectedRecentProfile = path;
            await RefreshRecentAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = "Profile load failed.";
            _messageDialogService.ShowError(ex.Message, "Profile load failed");
        }
        finally
        {
            IsBusy = false;
            RefreshMetadata();
        }
    }

    private async Task RefreshRecentAsync()
    {
        var recent = await _workflowService.LoadRecentProfilesAsync();
        _workspaceState.SetRecentProfiles(recent);

        RecentProfiles.Clear();
        foreach (var item in _workspaceState.RecentProfilePaths)
        {
            RecentProfiles.Add(item);
        }

        if (SelectedRecentProfile is null || !RecentProfiles.Contains(SelectedRecentProfile))
        {
            SelectedRecentProfile = RecentProfiles.FirstOrDefault();
        }
    }

    private void WorkspaceStateOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkspaceState.LoadedProfile) or nameof(WorkspaceState.LoadedProfilePath))
        {
            RefreshMetadata();
        }
    }

    private void RefreshMetadata()
    {
        OnPropertyChanged(nameof(ProfileSummary));
        OnPropertyChanged(nameof(IncludedCategories));
    }
}
