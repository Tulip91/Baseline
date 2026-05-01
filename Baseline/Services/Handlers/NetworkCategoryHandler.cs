using BaseLine.Core;
using BaseLine.Infrastructure;

namespace BaseLine.Services;

public sealed class NetworkCategoryHandler : IProfileCategoryHandler
{
    private readonly IRegistryAccessor _registryAccessor;
    private readonly NetworkDiscoveryService _networkDiscoveryService;

    public NetworkCategoryHandler(IRegistryAccessor registryAccessor, NetworkDiscoveryService networkDiscoveryService)
    {
        _registryAccessor = registryAccessor;
        _networkDiscoveryService = networkDiscoveryService;
    }

    public ProfileCategory Category => ProfileCategory.Network;

    public Task<CategoryCaptureResult> CaptureAsync(CaptureOptions options, CancellationToken cancellationToken = default)
    {
        var payload = new NetworkPayload
        {
            Adapters = _networkDiscoveryService.GetActiveAdapters().ToList(),
            GlobalSettings = new List<RegistryProfileEntry?>
            {
                ReadGlobalSetting("network.default-ttl", "Network", "Default TTL", RegistryRoot.LocalMachine, @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "DefaultTTL", SafetyLevel.Moderate),
                ReadGlobalSetting("network.enable-pmtu", "Network", "Enable PMTU discovery", RegistryRoot.LocalMachine, @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "EnablePMTUDiscovery", SafetyLevel.Moderate)
            }.Where(item => item is not null).Cast<RegistryProfileEntry>().ToList()
        };

        return Task.FromResult(new CategoryCaptureResult
        {
            Category = Category,
            Payload = payload,
            Messages = [$"Captured {payload.Adapters.Count} active adapter profiles."]
        });
    }

    public Task<IReadOnlyList<CompareItem>> CompareAsync(BaselineProfile profile, CancellationToken cancellationToken = default)
    {
        var payload = profile.Categories.Network;
        if (payload is null)
        {
            return Task.FromResult<IReadOnlyList<CompareItem>>([]);
        }

        var currentAdapters = _networkDiscoveryService.GetActiveAdapters();
        var items = new List<CompareItem>();

        foreach (var adapter in payload.Adapters)
        {
            var current = currentAdapters.FirstOrDefault(item => string.Equals(item.AdapterId, adapter.AdapterId, StringComparison.OrdinalIgnoreCase))
                ?? currentAdapters.FirstOrDefault(item => string.Equals(item.Name, adapter.Name, StringComparison.OrdinalIgnoreCase));

            if (current is null)
            {
                items.Add(new CompareItem
                {
                    Id = $"{Category}:adapter:{adapter.AdapterId}",
                    Category = Category,
                    GroupName = "Adapters",
                    DisplayName = adapter.Name,
                    ProfileValue = $"DNS {string.Join(", ", adapter.DnsServers)} | Metric {adapter.InterfaceMetric}",
                    CurrentValue = "Adapter not detected",
                    RecommendedAction = "Skip or remap",
                    SafetyLevel = SafetyLevel.Moderate,
                    Status = BaselineStatus.Warning,
                    Notes = "Adapter mismatch detected. Apply will target the first active adapter if no exact match exists."
                });
                continue;
            }

            var currentValue = $"DNS {string.Join(", ", current.DnsServers)} | Metric {current.InterfaceMetric}";
            var profileValue = $"DNS {string.Join(", ", adapter.DnsServers)} | Metric {adapter.InterfaceMetric}";
            var matches = current.DnsServers.SequenceEqual(adapter.DnsServers, StringComparer.OrdinalIgnoreCase) &&
                          current.InterfaceMetric == adapter.InterfaceMetric;

            items.Add(new CompareItem
            {
                Id = $"{Category}:adapter:{adapter.AdapterId}",
                Category = Category,
                GroupName = "Adapters",
                DisplayName = adapter.Name,
                ProfileValue = profileValue,
                CurrentValue = currentValue,
                RecommendedAction = matches ? "No action" : "Apply DNS and metric",
                SafetyLevel = SafetyLevel.Moderate,
                Status = matches ? BaselineStatus.AlreadyMatches : BaselineStatus.Ready,
                Notes = adapter.InterfaceRegistryPath
            });
        }

        foreach (var global in payload.GlobalSettings)
        {
            var current = _registryAccessor.ReadValue(global.Root, global.Path, global.ValueName);
            var currentText = current?.ToDisplayString() ?? "(missing)";
            var profileText = global.Value.ToDisplayString();
            var matches = currentText == profileText;
            items.Add(new CompareItem
            {
                Id = $"{Category}:global:{global.Id}",
                Category = Category,
                GroupName = global.GroupName,
                DisplayName = global.DisplayName,
                ProfileValue = profileText,
                CurrentValue = currentText,
                RecommendedAction = matches ? "No action" : "Apply global network setting",
                SafetyLevel = global.SafetyLevel,
                Status = matches ? BaselineStatus.AlreadyMatches : BaselineStatus.Ready
            });
        }

        return Task.FromResult<IReadOnlyList<CompareItem>>(items);
    }

    public Task<ApplyCategoryResult> ApplyAsync(BaselineProfile profile, IReadOnlyList<CompareItem> items, CancellationToken cancellationToken = default)
    {
        var payload = profile.Categories.Network;
        if (payload is null)
        {
            return Task.FromResult(new ApplyCategoryResult());
        }

        var results = new List<ApplyResultItem>();
        var rollbackItems = new List<RollbackItem>();
        var availableAdapters = _networkDiscoveryService.GetActiveAdapters();

        foreach (var item in items)
        {
            var parts = item.Id.Split(':');
            if (parts.Length < 3)
            {
                continue;
            }

            if (parts[1] == "adapter")
            {
                var profileAdapter = payload.Adapters.FirstOrDefault(adapter => adapter.AdapterId == parts[2]);
                if (profileAdapter is null)
                {
                    continue;
                }

                var targetAdapter = availableAdapters.FirstOrDefault(adapter => adapter.AdapterId == profileAdapter.AdapterId)
                    ?? availableAdapters.FirstOrDefault();
                if (targetAdapter is null)
                {
                    results.Add(new ApplyResultItem { ItemId = item.Id, Category = Category, DisplayName = item.DisplayName, Status = BaselineStatus.Skipped, Message = "No active adapter available." });
                    continue;
                }

                rollbackItems.Add(new RollbackItem
                {
                    ItemId = item.Id,
                    Category = Category,
                    DisplayName = item.DisplayName,
                    Kind = RollbackKind.NetworkAdapter,
                    ExistedBefore = true,
                    PreviousStringValues = targetAdapter.DnsServers.ToList(),
                    PreviousNumericValue = targetAdapter.InterfaceMetric,
                    Metadata = new Dictionary<string, string> { ["path"] = targetAdapter.InterfaceRegistryPath }
                });

                var dnsSuccess = _registryAccessor.WriteValue(RegistryRoot.LocalMachine, targetAdapter.InterfaceRegistryPath, "NameServer", new RegistryDataSnapshot
                {
                    DataKind = RegistryDataKind.String,
                    StringValue = string.Join(",", profileAdapter.DnsServers)
                });
                var metricSuccess = profileAdapter.InterfaceMetric is null || _registryAccessor.WriteValue(RegistryRoot.LocalMachine, targetAdapter.InterfaceRegistryPath, "InterfaceMetric", new RegistryDataSnapshot
                {
                    DataKind = RegistryDataKind.DWord,
                    NumericValue = profileAdapter.InterfaceMetric
                });

                results.Add(new ApplyResultItem { ItemId = item.Id, Category = Category, DisplayName = item.DisplayName, Status = dnsSuccess && metricSuccess ? BaselineStatus.Applied : BaselineStatus.Failed, Message = dnsSuccess && metricSuccess ? $"Applied to {targetAdapter.Name}." : "Failed to update adapter configuration." });
            }
            else if (parts[1] == "global")
            {
                var global = payload.GlobalSettings.FirstOrDefault(entry => entry.Id == parts[2]);
                if (global is null)
                {
                    continue;
                }

                var previous = _registryAccessor.ReadValue(global.Root, global.Path, global.ValueName);
                rollbackItems.Add(new RollbackItem
                {
                    ItemId = item.Id,
                    Category = Category,
                    DisplayName = item.DisplayName,
                    Kind = RollbackKind.RegistryValue,
                    ExistedBefore = previous is not null,
                    PreviousRegistryValue = previous,
                    Metadata = new Dictionary<string, string>
                    {
                        ["root"] = global.Root.ToString(),
                        ["path"] = global.Path,
                        ["valueName"] = global.ValueName
                    }
                });

                var success = _registryAccessor.WriteValue(global.Root, global.Path, global.ValueName, global.Value);
                results.Add(new ApplyResultItem { ItemId = item.Id, Category = Category, DisplayName = item.DisplayName, Status = success ? BaselineStatus.Applied : BaselineStatus.Failed, Message = success ? "Network registry setting applied." : "Failed to apply network registry setting." });
            }
        }

        return Task.FromResult(new ApplyCategoryResult { ResultItems = results, RollbackItems = rollbackItems });
    }

    public Task<IReadOnlyList<ApplyResultItem>> RollbackAsync(IReadOnlyList<RollbackItem> items, CancellationToken cancellationToken = default)
    {
        var results = new List<ApplyResultItem>();
        foreach (var item in items)
        {
            if (item.Kind == RollbackKind.NetworkAdapter)
            {
                if (!item.Metadata.TryGetValue("path", out var path))
                {
                    continue;
                }

                var dnsSuccess = _registryAccessor.WriteValue(RegistryRoot.LocalMachine, path, "NameServer", new RegistryDataSnapshot
                {
                    DataKind = RegistryDataKind.String,
                    StringValue = string.Join(",", item.PreviousStringValues)
                });
                var metricSuccess = item.PreviousNumericValue is null || _registryAccessor.WriteValue(RegistryRoot.LocalMachine, path, "InterfaceMetric", new RegistryDataSnapshot
                {
                    DataKind = RegistryDataKind.DWord,
                    NumericValue = item.PreviousNumericValue
                });

                results.Add(new ApplyResultItem { ItemId = item.ItemId, Category = Category, DisplayName = item.DisplayName, Status = dnsSuccess && metricSuccess ? BaselineStatus.RolledBack : BaselineStatus.Failed, Message = dnsSuccess && metricSuccess ? "Adapter settings restored." : "Failed to restore adapter settings." });
            }
            else
            {
                if (!item.Metadata.TryGetValue("root", out var rootText) ||
                    !item.Metadata.TryGetValue("path", out var path) ||
                    !item.Metadata.TryGetValue("valueName", out var valueName) ||
                    !Enum.TryParse(rootText, out RegistryRoot root))
                {
                    continue;
                }

                var success = item.ExistedBefore && item.PreviousRegistryValue is not null
                    ? _registryAccessor.WriteValue(root, path, valueName, item.PreviousRegistryValue)
                    : _registryAccessor.DeleteValue(root, path, valueName);

                results.Add(new ApplyResultItem { ItemId = item.ItemId, Category = Category, DisplayName = item.DisplayName, Status = success ? BaselineStatus.RolledBack : BaselineStatus.Failed, Message = success ? "Network registry setting restored." : "Failed to restore network registry setting." });
            }
        }

        return Task.FromResult<IReadOnlyList<ApplyResultItem>>(results);
    }

    private RegistryProfileEntry? ReadGlobalSetting(string id, string group, string displayName, RegistryRoot root, string path, string valueName, SafetyLevel safetyLevel)
    {
        var value = _registryAccessor.ReadValue(root, path, valueName);
        return value is null
            ? null
            : new RegistryProfileEntry
            {
                Id = id,
                GroupName = group,
                DisplayName = displayName,
                Root = root,
                Path = path,
                ValueName = valueName,
                Value = value,
                SafetyLevel = safetyLevel
            };
    }
}
