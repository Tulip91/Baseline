using BaseLine.Core;
using BaseLine.Infrastructure;

namespace BaseLine.Services;

public interface IProfileCategoryHandler
{
    ProfileCategory Category { get; }
    Task<CategoryCaptureResult> CaptureAsync(CaptureOptions options, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CompareItem>> CompareAsync(BaselineProfile profile, CancellationToken cancellationToken = default);
    Task<ApplyCategoryResult> ApplyAsync(BaselineProfile profile, IReadOnlyList<CompareItem> items, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApplyResultItem>> RollbackAsync(IReadOnlyList<RollbackItem> items, CancellationToken cancellationToken = default);
}

public sealed class WorkspaceState : ObservableObject
{
    private BaselineProfile? _loadedProfile;
    private string? _loadedProfilePath;
    private CompareReport? _currentCompareReport;
    private string _statusText = "Ready to capture or load a profile.";

    public BaselineProfile? LoadedProfile
    {
        get => _loadedProfile;
        set => SetProperty(ref _loadedProfile, value);
    }

    public string? LoadedProfilePath
    {
        get => _loadedProfilePath;
        set => SetProperty(ref _loadedProfilePath, value);
    }

    public CompareReport? CurrentCompareReport
    {
        get => _currentCompareReport;
        set => SetProperty(ref _currentCompareReport, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public List<string> RecentProfilePaths { get; private set; } = [];
    public List<RollbackRecord> RollbackHistory { get; private set; } = [];

    public void SetRecentProfiles(IEnumerable<string> paths)
    {
        RecentProfilePaths = paths.ToList();
        OnPropertyChanged(nameof(RecentProfilePaths));
    }

    public void SetRollbackHistory(IEnumerable<RollbackRecord> history)
    {
        RollbackHistory = history.OrderByDescending(item => item.CreatedAt).ToList();
        OnPropertyChanged(nameof(RollbackHistory));
    }
}
