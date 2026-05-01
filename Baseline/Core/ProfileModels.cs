using System.Text.Json.Serialization;

namespace BaseLine.Core;

public sealed class BaselineProfile
{
    public string SchemaVersion { get; set; } = "1.0";
    public ProfileMetadata Metadata { get; set; } = new();
    public CategoryPayloads Categories { get; set; } = new();
}

public sealed class ProfileMetadata
{
    public string ProfileName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string SourceMachineName { get; set; } = string.Empty;
    public string WindowsVersion { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
    public List<ProfileCategory> SelectedCategories { get; set; } = [];
}

public sealed class CategoryPayloads
{
    public ServicesPayload? Services { get; set; }
    public BootBehaviorPayload? BootBehavior { get; set; }
    public RegistryTweaksPayload? RegistryTweaks { get; set; }
    public PoliciesPayload? Policies { get; set; }
    public NetworkPayload? Network { get; set; }
    public StartupEnvironmentPayload? StartupEnvironment { get; set; }
    public ScheduledTasksPayload? ScheduledTasks { get; set; }
    public PowerConfigurationPayload? PowerConfiguration { get; set; }

    [JsonIgnore]
    public IEnumerable<ProfileCategory> IncludedCategories
    {
        get
        {
            if (Services is not null) yield return ProfileCategory.Services;
            if (BootBehavior is not null) yield return ProfileCategory.BootBehavior;
            if (RegistryTweaks is not null) yield return ProfileCategory.RegistryTweaks;
            if (Policies is not null) yield return ProfileCategory.Policies;
            if (Network is not null) yield return ProfileCategory.Network;
            if (StartupEnvironment is not null) yield return ProfileCategory.StartupEnvironment;
            if (ScheduledTasks is not null) yield return ProfileCategory.ScheduledTasks;
            if (PowerConfiguration is not null) yield return ProfileCategory.PowerConfiguration;
        }
    }
}

public sealed class ServicesPayload
{
    public List<ServiceProfileItem> Items { get; set; } = [];
}

public sealed class ServiceProfileItem
{
    public string ServiceName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string StartType { get; set; } = string.Empty;
    public bool DelayedAutoStart { get; set; }
    public string? CurrentStatus { get; set; }
    public SafetyLevel SafetyLevel { get; set; }
}

public sealed class BootBehaviorPayload
{
    public List<BootBehaviorItem> Items { get; set; } = [];
}

public sealed class BootBehaviorItem
{
    public string SettingName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Value { get; set; }
    public string Description { get; set; } = string.Empty;
    public SafetyLevel SafetyLevel { get; set; } = SafetyLevel.Advanced;
}

public sealed class RegistryTweaksPayload
{
    public List<RegistryProfileEntry> Items { get; set; } = [];
}

public sealed class PoliciesPayload
{
    public List<RegistryProfileEntry> Items { get; set; } = [];
}

public sealed class RegistryProfileEntry
{
    public string Id { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public RegistryRoot Root { get; set; }
    public string Path { get; set; } = string.Empty;
    public string ValueName { get; set; } = string.Empty;
    public RegistryDataSnapshot Value { get; set; } = new();
    public SafetyLevel SafetyLevel { get; set; }
    public string? CompatibilityNote { get; set; }
    public bool IsCustom { get; set; }
}

public sealed class RegistryDataSnapshot
{
    public RegistryDataKind DataKind { get; set; } = RegistryDataKind.None;
    public string? StringValue { get; set; }
    public List<string> MultiStringValue { get; set; } = [];
    public long? NumericValue { get; set; }
    public byte[] BinaryValue { get; set; } = [];

    public string ToDisplayString()
    {
        return DataKind switch
        {
            RegistryDataKind.MultiString => MultiStringValue.Count == 0 ? "(empty)" : string.Join(", ", MultiStringValue),
            RegistryDataKind.DWord or RegistryDataKind.QWord => NumericValue?.ToString() ?? "(null)",
            RegistryDataKind.Binary => BinaryValue.Length == 0 ? "(empty)" : Convert.ToHexString(BinaryValue),
            RegistryDataKind.None => "(not set)",
            _ => string.IsNullOrWhiteSpace(StringValue) ? "(empty)" : StringValue
        };
    }
}

public sealed class NetworkPayload
{
    public List<NetworkAdapterProfile> Adapters { get; set; } = [];
    public List<RegistryProfileEntry> GlobalSettings { get; set; } = [];
}

public sealed class NetworkAdapterProfile
{
    public string AdapterId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string InterfaceRegistryPath { get; set; } = string.Empty;
    public List<string> DnsServers { get; set; } = [];
    public bool IsDhcpEnabled { get; set; }
    public int? InterfaceMetric { get; set; }
}

public sealed class StartupEnvironmentPayload
{
    public List<StartupItemProfile> Items { get; set; } = [];
}

public sealed class StartupItemProfile
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string SourceLocation { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string? StartupApprovalState { get; set; }
    public SafetyLevel SafetyLevel { get; set; } = SafetyLevel.Moderate;
}

public sealed class ScheduledTasksPayload
{
    public List<ScheduledTaskProfile> Items { get; set; } = [];
}

public sealed class ScheduledTaskProfile
{
    public string TaskPath { get; set; } = string.Empty;
    public string TaskName { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string? ActionPath { get; set; }
    public string? TriggerSummary { get; set; }
    public SafetyLevel SafetyLevel { get; set; } = SafetyLevel.Moderate;
}

public sealed class PowerConfigurationPayload
{
    public string ActiveSchemeGuid { get; set; } = string.Empty;
    public string ActiveSchemeName { get; set; } = string.Empty;
    public bool HibernateEnabled { get; set; }
    public bool FastStartupEnabled { get; set; }
    public List<PowerSettingProfile> Settings { get; set; } = [];
}

public sealed class PowerSettingProfile
{
    public string SettingId { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int? AcValue { get; set; }
    public int? DcValue { get; set; }
    public string Unit { get; set; } = string.Empty;
    public SafetyLevel SafetyLevel { get; set; } = SafetyLevel.Safe;
}
