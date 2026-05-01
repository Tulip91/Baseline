namespace BaseLine.Core;

public sealed class CaptureOptions
{
    public string ProfileName { get; set; } = string.Empty;
    public List<ProfileCategory> SelectedCategories { get; set; } = [];
    public List<StructuredRegistryTemplate> SelectedRegistryTemplates { get; set; } = [];
    public List<StructuredRegistryTemplate> SelectedPolicyTemplates { get; set; } = [];
    public string? PreferredNetworkAdapterId { get; set; }
}

public sealed class StructuredRegistryTemplate
{
    public string Id { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public RegistryRoot Root { get; set; }
    public string Path { get; set; } = string.Empty;
    public string ValueName { get; set; } = string.Empty;
    public SafetyLevel SafetyLevel { get; set; }
    public string? CompatibilityNote { get; set; }
    public bool IsDefaultSelected { get; set; }
    public bool IsCustom { get; set; }
}

public sealed class CompareReport
{
    public DateTimeOffset GeneratedAt { get; set; }
    public string ProfileName { get; set; } = string.Empty;
    public List<CompareItem> Items { get; set; } = [];
    public List<string> Messages { get; set; } = [];
}

public sealed class CompareItem
{
    public string Id { get; set; } = string.Empty;
    public ProfileCategory Category { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string ProfileValue { get; set; } = string.Empty;
    public string CurrentValue { get; set; } = string.Empty;
    public string RecommendedAction { get; set; } = string.Empty;
    public SafetyLevel SafetyLevel { get; set; }
    public BaselineStatus Status { get; set; }
    public string? Notes { get; set; }
}

public sealed class ApplySession
{
    public Guid SessionId { get; set; } = Guid.NewGuid();
    public string ProfileName { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;
    public List<ApplyResultItem> Results { get; set; } = [];
    public RollbackRecord RollbackRecord { get; set; } = new();
}

public sealed class ApplyResultItem
{
    public string ItemId { get; set; } = string.Empty;
    public ProfileCategory Category { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public BaselineStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed class RollbackRecord
{
    public Guid SessionId { get; set; }
    public string ProfileName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public List<RollbackItem> Items { get; set; } = [];
}

public sealed class RollbackItem
{
    public string ItemId { get; set; } = string.Empty;
    public ProfileCategory Category { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public RollbackKind Kind { get; set; }
    public bool ExistedBefore { get; set; }
    public string? PreviousStringValue { get; set; }
    public List<string> PreviousStringValues { get; set; } = [];
    public long? PreviousNumericValue { get; set; }
    public bool? PreviousBooleanValue { get; set; }
    public RegistryDataSnapshot? PreviousRegistryValue { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = [];
}

public sealed class CaptureSummary
{
    public int RequestedCategories { get; set; }
    public int CapturedCategories { get; set; }
    public int FailedCategories { get; set; }
    public List<string> Messages { get; set; } = [];

    public bool HasFailures => FailedCategories > 0;
}

public sealed class CategoryCaptureResult
{
    public required ProfileCategory Category { get; init; }
    public required object Payload { get; init; }
    public List<string> Messages { get; init; } = [];
}

public sealed class ApplyCategoryResult
{
    public List<ApplyResultItem> ResultItems { get; init; } = [];
    public List<RollbackItem> RollbackItems { get; init; } = [];
}
