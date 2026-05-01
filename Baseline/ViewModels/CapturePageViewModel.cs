using System.Collections.ObjectModel;
using System.ComponentModel;
using BaseLine.Core;
using BaseLine.Infrastructure;
using BaseLine.Services;

namespace BaseLine.ViewModels;

public sealed class CapturePageViewModel : PageViewModelBase
{
    private readonly BaselineWorkflowService _workflowService;
    private readonly WorkspaceState _workspaceState;
    private readonly IFileDialogService _fileDialogService;
    private readonly IMessageDialogService _messageDialogService;
    private readonly RegistryTemplateCatalog _registryTemplateCatalog;
    private readonly NetworkDiscoveryService _networkDiscoveryService;

    private string _profileName = $"{Environment.MachineName}-baseline";
    private RegistryRoot _customRegistryRoot = RegistryRoot.CurrentUser;
    private string _customRegistryPath = string.Empty;
    private string _customRegistryValueName = string.Empty;
    private RegistryRoot _customPolicyRoot = RegistryRoot.LocalMachine;
    private string _customPolicyPath = string.Empty;
    private string _customPolicyValueName = string.Empty;
    private NetworkAdapterProfile? _selectedAdapter;

    public CapturePageViewModel(
        BaselineWorkflowService workflowService,
        WorkspaceState workspaceState,
        IFileDialogService fileDialogService,
        IMessageDialogService messageDialogService,
        RegistryTemplateCatalog registryTemplateCatalog,
        NetworkDiscoveryService networkDiscoveryService)
        : base("Capture", "Package a known-good machine into a structured baseline profile.")
    {
        _workflowService = workflowService;
        _workspaceState = workspaceState;
        _fileDialogService = fileDialogService;
        _messageDialogService = messageDialogService;
        _registryTemplateCatalog = registryTemplateCatalog;
        _networkDiscoveryService = networkDiscoveryService;

        Categories = new ObservableCollection<CategorySelectionItemViewModel>
        {
            new(ProfileCategory.Services, "Services", "Service start types, delayed auto-start, and status."),
            new(ProfileCategory.BootBehavior, "Boot Behavior", "Supported BCD settings only."),
            new(ProfileCategory.RegistryTweaks, "Registry Tweaks", "Curated structured tweaks and custom entries."),
            new(ProfileCategory.Policies, "Policies", "Registry-backed policy values under supported policy hives."),
            new(ProfileCategory.Network, "Network", "Active adapters, DNS, and selected TCP values."),
            new(ProfileCategory.StartupEnvironment, "Startup", "Run keys, startup folders, and approvals."),
            new(ProfileCategory.ScheduledTasks, "Scheduled Tasks", "Non-Microsoft and logon/startup tasks."),
            new(ProfileCategory.PowerConfiguration, "Power", "Active plan, hibernate, fast startup, and key settings.")
        };

        RegistryTemplates = new ObservableCollection<RegistryTemplateSelectionViewModel>(_registryTemplateCatalog.RegistryDefaults.Select(item => new RegistryTemplateSelectionViewModel(item)));
        PolicyTemplates = new ObservableCollection<RegistryTemplateSelectionViewModel>(_registryTemplateCatalog.PolicyDefaults.Select(item => new RegistryTemplateSelectionViewModel(item)));
        ActiveAdapters = new ObservableCollection<NetworkAdapterProfile>();

        foreach (var category in Categories)
        {
            category.PropertyChanged += CaptureScopeOnPropertyChanged;
        }

        foreach (var template in RegistryTemplates.Concat(PolicyTemplates))
        {
            template.PropertyChanged += CaptureScopeOnPropertyChanged;
        }

        ActiveAdapters.CollectionChanged += (_, _) => OnPropertyChanged(nameof(AdapterCount));

        CaptureCommand = new AsyncRelayCommand(CaptureAsync, () => !IsBusy);
        RefreshAdaptersCommand = new RelayCommand(RefreshAdapters);
        AddRegistryEntryCommand = new RelayCommand(AddRegistryEntry);
        AddPolicyEntryCommand = new RelayCommand(AddPolicyEntry);
    }

    public ObservableCollection<CategorySelectionItemViewModel> Categories { get; }
    public ObservableCollection<RegistryTemplateSelectionViewModel> RegistryTemplates { get; }
    public ObservableCollection<RegistryTemplateSelectionViewModel> PolicyTemplates { get; }
    public ObservableCollection<NetworkAdapterProfile> ActiveAdapters { get; }

    public int SelectedCategoryCount => Categories.Count(item => item.IsSelected);
    public int SelectedRegistryTemplateCount => RegistryTemplates.Count(item => item.IsSelected);
    public int SelectedPolicyTemplateCount => PolicyTemplates.Count(item => item.IsSelected);
    public int AdapterCount => ActiveAdapters.Count;

    public AsyncRelayCommand CaptureCommand { get; }
    public RelayCommand RefreshAdaptersCommand { get; }
    public RelayCommand AddRegistryEntryCommand { get; }
    public RelayCommand AddPolicyEntryCommand { get; }

    public string ProfileName
    {
        get => _profileName;
        set => SetProperty(ref _profileName, value);
    }

    public RegistryRoot CustomRegistryRoot
    {
        get => _customRegistryRoot;
        set => SetProperty(ref _customRegistryRoot, value);
    }

    public string CustomRegistryPath
    {
        get => _customRegistryPath;
        set => SetProperty(ref _customRegistryPath, value);
    }

    public string CustomRegistryValueName
    {
        get => _customRegistryValueName;
        set => SetProperty(ref _customRegistryValueName, value);
    }

    public RegistryRoot CustomPolicyRoot
    {
        get => _customPolicyRoot;
        set => SetProperty(ref _customPolicyRoot, value);
    }

    public string CustomPolicyPath
    {
        get => _customPolicyPath;
        set => SetProperty(ref _customPolicyPath, value);
    }

    public string CustomPolicyValueName
    {
        get => _customPolicyValueName;
        set => SetProperty(ref _customPolicyValueName, value);
    }

    public NetworkAdapterProfile? SelectedAdapter
    {
        get => _selectedAdapter;
        set => SetProperty(ref _selectedAdapter, value);
    }

    public override Task InitializeAsync()
    {
        RefreshAdapters();
        return Task.CompletedTask;
    }

    private async Task CaptureAsync()
    {
        if (string.IsNullOrWhiteSpace(ProfileName))
        {
            _messageDialogService.ShowError("Profile name is required.", "Capture");
            return;
        }

        var selectedCategories = Categories.Where(item => item.IsSelected).Select(item => item.Category).ToList();
        if (selectedCategories.Count == 0)
        {
            _messageDialogService.ShowError("Select at least one category to capture.", "Capture");
            return;
        }

        IsBusy = true;
        StatusMessage = "Capturing current machine state...";
        try
        {
            var options = new CaptureOptions
            {
                ProfileName = ProfileName.Trim(),
                SelectedCategories = selectedCategories,
                SelectedRegistryTemplates = RegistryTemplates.Where(item => item.IsSelected).Select(item => item.Template).ToList(),
                SelectedPolicyTemplates = PolicyTemplates.Where(item => item.IsSelected).Select(item => item.Template).ToList(),
                PreferredNetworkAdapterId = SelectedAdapter?.AdapterId
            };

            var (profile, summary) = await _workflowService.CaptureProfileAsync(options);
            var capturedCategories = profile.Categories.IncludedCategories.ToList();
            if (capturedCategories.Count == 0)
            {
                StatusMessage = "Capture failed. No categories were captured.";
                _workspaceState.StatusText = "Capture failed.";
                _messageDialogService.ShowError(summary.Messages.LastOrDefault() ?? "No selected category could be captured from the current machine.", "Capture failed");
                return;
            }

            var path = _fileDialogService.PickProfileToSave($"{ProfileName.Trim()}.baseline.json");
            if (string.IsNullOrWhiteSpace(path))
            {
                StatusMessage = BuildCaptureStatus(summary, capturedCategories.Count);
                _workspaceState.StatusText = "Capture completed but was not saved.";
                return;
            }

            await _workflowService.SaveProfileAsync(profile, path);
            _workspaceState.LoadedProfile = profile;
            _workspaceState.LoadedProfilePath = path;
            _workspaceState.CurrentCompareReport = null;
            _workspaceState.StatusText = summary.HasFailures
                ? $"Captured {profile.Metadata.ProfileName} with {summary.FailedCategories} category issues."
                : $"Captured and saved {profile.Metadata.ProfileName}.";
            StatusMessage = BuildCaptureStatus(summary, capturedCategories.Count);
            _messageDialogService.ShowInfo(BuildCaptureDialog(path, summary, capturedCategories.Count), summary.HasFailures ? "Capture completed with issues" : "Capture complete");
        }
        catch (Exception ex)
        {
            StatusMessage = "Capture failed.";
            _workspaceState.StatusText = "Capture failed.";
            _messageDialogService.ShowError(ex.Message, "Capture failed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RefreshAdapters()
    {
        ActiveAdapters.Clear();
        foreach (var adapter in _networkDiscoveryService.GetActiveAdapters())
        {
            ActiveAdapters.Add(adapter);
        }

        SelectedAdapter = ActiveAdapters.FirstOrDefault(adapter => adapter.IsActive) ?? ActiveAdapters.FirstOrDefault();
        StatusMessage = SelectedAdapter is null ? "No active adapter detected." : $"Active adapter helper ready: {SelectedAdapter.InterfaceRegistryPath}";
    }

    private void AddRegistryEntry()
    {
        if (string.IsNullOrWhiteSpace(CustomRegistryPath) || string.IsNullOrWhiteSpace(CustomRegistryValueName))
        {
            return;
        }

        var template = new RegistryTemplateSelectionViewModel(new StructuredRegistryTemplate
        {
            Id = $"custom-registry-{Guid.NewGuid():N}",
            GroupName = "Custom",
            DisplayName = CustomRegistryValueName.Trim(),
            Root = CustomRegistryRoot,
            Path = CustomRegistryPath.Trim(),
            ValueName = CustomRegistryValueName.Trim(),
            SafetyLevel = SafetyLevel.Advanced,
            IsCustom = true,
            IsDefaultSelected = true
        });
        template.PropertyChanged += CaptureScopeOnPropertyChanged;
        RegistryTemplates.Add(template);
        OnPropertyChanged(nameof(SelectedRegistryTemplateCount));

        CustomRegistryPath = string.Empty;
        CustomRegistryValueName = string.Empty;
    }

    private void AddPolicyEntry()
    {
        if (string.IsNullOrWhiteSpace(CustomPolicyPath) || string.IsNullOrWhiteSpace(CustomPolicyValueName))
        {
            return;
        }

        var template = new RegistryTemplateSelectionViewModel(new StructuredRegistryTemplate
        {
            Id = $"custom-policy-{Guid.NewGuid():N}",
            GroupName = "Custom",
            DisplayName = CustomPolicyValueName.Trim(),
            Root = CustomPolicyRoot,
            Path = CustomPolicyPath.Trim(),
            ValueName = CustomPolicyValueName.Trim(),
            SafetyLevel = SafetyLevel.Advanced,
            IsCustom = true,
            IsDefaultSelected = true
        });
        template.PropertyChanged += CaptureScopeOnPropertyChanged;
        PolicyTemplates.Add(template);
        OnPropertyChanged(nameof(SelectedPolicyTemplateCount));

        CustomPolicyPath = string.Empty;
        CustomPolicyValueName = string.Empty;
    }

    private static string BuildCaptureStatus(CaptureSummary summary, int capturedCategoryCount)
    {
        return summary.HasFailures
            ? $"Captured {capturedCategoryCount} categories with {summary.FailedCategories} issues."
            : $"Captured {capturedCategoryCount} categories successfully.";
    }

    private void CaptureScopeOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CategorySelectionItemViewModel.IsSelected) &&
            e.PropertyName != nameof(RegistryTemplateSelectionViewModel.IsSelected))
        {
            return;
        }

        OnPropertyChanged(nameof(SelectedCategoryCount));
        OnPropertyChanged(nameof(SelectedRegistryTemplateCount));
        OnPropertyChanged(nameof(SelectedPolicyTemplateCount));
    }

    private static string BuildCaptureDialog(string path, CaptureSummary summary, int capturedCategoryCount)
    {
        var lines = new List<string>
        {
            $"Saved profile to:",
            path,
            string.Empty,
            BuildCaptureStatus(summary, capturedCategoryCount)
        };

        if (summary.Messages.Count > 0)
        {
            lines.Add(string.Empty);
            lines.AddRange(summary.Messages.Take(6));
        }

        return string.Join(Environment.NewLine, lines);
    }
}
