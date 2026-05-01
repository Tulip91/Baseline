using BaseLine.Core;
using BaseLine.Infrastructure;
using BaseLine.Services.Handlers;
using Microsoft.Win32;

namespace BaseLine.Services;

public sealed class ServicesCategoryHandler : IProfileCategoryHandler
{
    private readonly IRegistryAccessor _registryAccessor;

    public ServicesCategoryHandler(IRegistryAccessor registryAccessor)
    {
        _registryAccessor = registryAccessor;
    }

    public ProfileCategory Category => ProfileCategory.Services;

    public Task<CategoryCaptureResult> CaptureAsync(CaptureOptions options, CancellationToken cancellationToken = default)
    {
        var payload = new ServicesPayload();

        using var servicesRoot = ServiceHelpers.OpenServicesRoot(writable: false);
        foreach (var serviceName in servicesRoot.GetSubKeyNames())
        {
            using var serviceKey = servicesRoot.OpenSubKey(serviceName, writable: false);
            if (serviceKey is null)
            {
                continue;
            }

            var typeValue = Convert.ToInt64(serviceKey.GetValue("Type", 0));
            var isWin32Service = (typeValue & 0x10) == 0x10 || (typeValue & 0x20) == 0x20;
            if (!isWin32Service)
            {
                continue;
            }

            var startType = ServiceHelpers.NormalizeStartType(Convert.ToInt64(serviceKey.GetValue("Start", 3)));
            var delayedAutoStart = Convert.ToInt64(serviceKey.GetValue("DelayedAutoStart", 0)) == 1;
            var displayName = serviceKey.GetValue("DisplayName")?.ToString() ?? serviceName;
            var imagePath = serviceKey.GetValue("ImagePath")?.ToString();
            payload.Items.Add(new ServiceProfileItem
            {
                ServiceName = serviceName,
                DisplayName = displayName,
                StartType = startType,
                DelayedAutoStart = delayedAutoStart,
                CurrentStatus = "Unknown",
                SafetyLevel = ServiceHelpers.Classify(serviceName, imagePath)
            });
        }

        return Task.FromResult(new CategoryCaptureResult
        {
            Category = Category,
            Payload = payload,
            Messages = [$"Captured {payload.Items.Count} service configurations."]
        });
    }

    public Task<IReadOnlyList<CompareItem>> CompareAsync(BaselineProfile profile, CancellationToken cancellationToken = default)
    {
        var payload = profile.Categories.Services;
        if (payload is null)
        {
            return Task.FromResult<IReadOnlyList<CompareItem>>([]);
        }

        var items = new List<CompareItem>();
        foreach (var service in payload.Items.OrderBy(item => item.DisplayName))
        {
            var path = $@"SYSTEM\CurrentControlSet\Services\{service.ServiceName}";
            var startType = _registryAccessor.ReadValue(RegistryRoot.LocalMachine, path, "Start")?.NumericValue;
            var delayed = _registryAccessor.ReadValue(RegistryRoot.LocalMachine, path, "DelayedAutoStart")?.NumericValue == 1;

            if (startType is null)
            {
                items.Add(new CompareItem
                {
                    Id = $"{Category}:{service.ServiceName}",
                    Category = Category,
                    GroupName = "Services",
                    DisplayName = service.DisplayName,
                    ProfileValue = $"{service.StartType}{(service.DelayedAutoStart ? " (Delayed)" : string.Empty)}",
                    CurrentValue = "Missing service",
                    RecommendedAction = "Skip",
                    SafetyLevel = service.SafetyLevel,
                    Status = BaselineStatus.MissingDependency,
                    Notes = "Service does not exist on the current machine."
                });
                continue;
            }

            var currentValue = $"{ServiceHelpers.NormalizeStartType(startType)}{(delayed ? " (Delayed)" : string.Empty)}";
            var profileValue = $"{service.StartType}{(service.DelayedAutoStart ? " (Delayed)" : string.Empty)}";
            var matches = string.Equals(currentValue, profileValue, StringComparison.OrdinalIgnoreCase);

            items.Add(new CompareItem
            {
                Id = $"{Category}:{service.ServiceName}",
                Category = Category,
                GroupName = "Services",
                DisplayName = service.DisplayName,
                ProfileValue = profileValue,
                CurrentValue = currentValue,
                RecommendedAction = matches ? "No action" : "Apply service startup values",
                SafetyLevel = service.SafetyLevel,
                Status = matches ? BaselineStatus.AlreadyMatches : BaselineStatus.Ready,
                Notes = service.SafetyLevel == SafetyLevel.Advanced ? "Advanced service. Review before applying." : null
            });
        }

        return Task.FromResult<IReadOnlyList<CompareItem>>(items);
    }

    public Task<ApplyCategoryResult> ApplyAsync(BaselineProfile profile, IReadOnlyList<CompareItem> items, CancellationToken cancellationToken = default)
    {
        var payload = profile.Categories.Services;
        if (payload is null)
        {
            return Task.FromResult(new ApplyCategoryResult());
        }

        var results = new List<ApplyResultItem>();
        var rollbackItems = new List<RollbackItem>();
        using var servicesRoot = ServiceHelpers.OpenServicesRoot(writable: true);

        foreach (var item in items)
        {
            var serviceName = item.Id.Split(':').Last();
            var profileItem = payload.Items.FirstOrDefault(entry => entry.ServiceName == serviceName);
            if (profileItem is null)
            {
                continue;
            }

            using var key = servicesRoot.OpenSubKey(serviceName, writable: true);
            if (key is null)
            {
                results.Add(new ApplyResultItem { ItemId = item.Id, Category = Category, DisplayName = item.DisplayName, Status = BaselineStatus.Skipped, Message = "Service missing on target machine." });
                continue;
            }

            var currentStart = Convert.ToInt64(key.GetValue("Start", 3));
            var currentDelayed = Convert.ToInt64(key.GetValue("DelayedAutoStart", 0)) == 1;
            rollbackItems.Add(new RollbackItem
            {
                ItemId = item.Id,
                Category = Category,
                DisplayName = item.DisplayName,
                Kind = RollbackKind.ServiceConfiguration,
                ExistedBefore = true,
                PreviousStringValue = ServiceHelpers.NormalizeStartType(currentStart),
                PreviousBooleanValue = currentDelayed,
                Metadata = new Dictionary<string, string> { ["serviceName"] = serviceName }
            });

            key.SetValue("Start", profileItem.StartType switch
            {
                "Automatic" => 2,
                "Disabled" => 4,
                _ => 3
            }, RegistryValueKind.DWord);
            key.SetValue("DelayedAutoStart", profileItem.DelayedAutoStart ? 1 : 0, RegistryValueKind.DWord);

            results.Add(new ApplyResultItem { ItemId = item.Id, Category = Category, DisplayName = item.DisplayName, Status = BaselineStatus.Applied, Message = "Service startup configuration updated." });
        }

        return Task.FromResult(new ApplyCategoryResult { ResultItems = results, RollbackItems = rollbackItems });
    }

    public Task<IReadOnlyList<ApplyResultItem>> RollbackAsync(IReadOnlyList<RollbackItem> items, CancellationToken cancellationToken = default)
    {
        var results = new List<ApplyResultItem>();
        using var servicesRoot = ServiceHelpers.OpenServicesRoot(writable: true);
        foreach (var item in items)
        {
            if (!item.Metadata.TryGetValue("serviceName", out var serviceName))
            {
                continue;
            }

            using var key = servicesRoot.OpenSubKey(serviceName, writable: true);
            if (key is null)
            {
                results.Add(new ApplyResultItem { ItemId = item.ItemId, Category = Category, DisplayName = item.DisplayName, Status = BaselineStatus.Skipped, Message = "Service no longer exists." });
                continue;
            }

            key.SetValue("Start", item.PreviousStringValue switch
            {
                "Automatic" => 2,
                "Disabled" => 4,
                _ => 3
            }, RegistryValueKind.DWord);
            key.SetValue("DelayedAutoStart", item.PreviousBooleanValue == true ? 1 : 0, RegistryValueKind.DWord);

            results.Add(new ApplyResultItem { ItemId = item.ItemId, Category = Category, DisplayName = item.DisplayName, Status = BaselineStatus.RolledBack, Message = "Service configuration restored." });
        }

        return Task.FromResult<IReadOnlyList<ApplyResultItem>>(results);
    }
}
