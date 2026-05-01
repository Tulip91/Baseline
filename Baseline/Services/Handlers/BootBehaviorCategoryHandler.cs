using System.Text.RegularExpressions;
using BaseLine.Core;
using BaseLine.Infrastructure;

namespace BaseLine.Services;

public sealed class BootBehaviorCategoryHandler : IProfileCategoryHandler
{
    private readonly ISystemCommandExecutor _commandExecutor;

    private static readonly IReadOnlyDictionary<string, (string DisplayName, string Description)> SupportedSettings = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
    {
        ["hypervisorlaunchtype"] = ("Hypervisor launch type", "Controls whether the hypervisor starts during boot."),
        ["useplatformtick"] = ("Use platform tick", "Forces use of the platform timer."),
        ["disabledynamictick"] = ("Disable dynamic tick", "Controls whether dynamic tick is disabled."),
        ["tscsyncpolicy"] = ("TSC sync policy", "Controls time stamp counter synchronization.")
    };

    public BootBehaviorCategoryHandler(ISystemCommandExecutor commandExecutor)
    {
        _commandExecutor = commandExecutor;
    }

    public ProfileCategory Category => ProfileCategory.BootBehavior;

    public async Task<CategoryCaptureResult> CaptureAsync(CaptureOptions options, CancellationToken cancellationToken = default)
    {
        var payload = new BootBehaviorPayload { Items = await ReadCurrentSettingsAsync(cancellationToken) };
        return new CategoryCaptureResult
        {
            Category = Category,
            Payload = payload,
            Messages = [$"Captured {payload.Items.Count} supported boot settings."]
        };
    }

    public async Task<IReadOnlyList<CompareItem>> CompareAsync(BaselineProfile profile, CancellationToken cancellationToken = default)
    {
        var payload = profile.Categories.BootBehavior;
        if (payload is null)
        {
            return [];
        }

        var current = (await ReadCurrentSettingsAsync(cancellationToken)).ToDictionary(item => item.SettingName, StringComparer.OrdinalIgnoreCase);
        return payload.Items.Select(setting =>
        {
            current.TryGetValue(setting.SettingName, out var currentValue);
            var currentText = currentValue?.Value ?? "(not set)";
            var profileText = setting.Value ?? "(not set)";
            var matches = string.Equals(currentText, profileText, StringComparison.OrdinalIgnoreCase);

            return new CompareItem
            {
                Id = $"{Category}:{setting.SettingName}",
                Category = Category,
                GroupName = "Boot Behavior",
                DisplayName = setting.DisplayName,
                ProfileValue = profileText,
                CurrentValue = currentText,
                RecommendedAction = matches ? "No action" : "Apply boot behaviour setting",
                SafetyLevel = setting.SafetyLevel,
                Status = matches ? BaselineStatus.AlreadyMatches : BaselineStatus.Ready,
                Notes = setting.Description
            };
        }).ToList();
    }

    public async Task<ApplyCategoryResult> ApplyAsync(BaselineProfile profile, IReadOnlyList<CompareItem> items, CancellationToken cancellationToken = default)
    {
        var payload = profile.Categories.BootBehavior;
        if (payload is null)
        {
            return new ApplyCategoryResult();
        }

        var current = (await ReadCurrentSettingsAsync(cancellationToken)).ToDictionary(item => item.SettingName, StringComparer.OrdinalIgnoreCase);
        var results = new List<ApplyResultItem>();
        var rollbackItems = new List<RollbackItem>();

        foreach (var item in items)
        {
            var name = item.Id.Split(':').Last();
            var target = payload.Items.FirstOrDefault(entry => string.Equals(entry.SettingName, name, StringComparison.OrdinalIgnoreCase));
            if (target is null)
            {
                continue;
            }

            current.TryGetValue(name, out var previous);
            rollbackItems.Add(new RollbackItem
            {
                ItemId = item.Id,
                Category = Category,
                DisplayName = item.DisplayName,
                Kind = RollbackKind.BootSetting,
                ExistedBefore = previous?.Value is not null,
                PreviousStringValue = previous?.Value,
                Metadata = new Dictionary<string, string> { ["settingName"] = name }
            });

            var result = target.Value is null
                ? await _commandExecutor.ExecuteAsync("bcdedit", $"/deletevalue {{current}} {name}", cancellationToken)
                : await _commandExecutor.ExecuteAsync("bcdedit", $"/set {{current}} {name} {target.Value}", cancellationToken);

            results.Add(new ApplyResultItem
            {
                ItemId = item.Id,
                Category = Category,
                DisplayName = item.DisplayName,
                Status = result.IsSuccess ? BaselineStatus.Applied : BaselineStatus.Failed,
                Message = result.IsSuccess ? "Boot setting updated." : result.StandardError.Trim()
            });
        }

        return new ApplyCategoryResult { ResultItems = results, RollbackItems = rollbackItems };
    }

    public async Task<IReadOnlyList<ApplyResultItem>> RollbackAsync(IReadOnlyList<RollbackItem> items, CancellationToken cancellationToken = default)
    {
        var results = new List<ApplyResultItem>();
        foreach (var item in items)
        {
            if (!item.Metadata.TryGetValue("settingName", out var name))
            {
                continue;
            }

            var result = item.ExistedBefore && !string.IsNullOrWhiteSpace(item.PreviousStringValue)
                ? await _commandExecutor.ExecuteAsync("bcdedit", $"/set {{current}} {name} {item.PreviousStringValue}", cancellationToken)
                : await _commandExecutor.ExecuteAsync("bcdedit", $"/deletevalue {{current}} {name}", cancellationToken);

            results.Add(new ApplyResultItem { ItemId = item.ItemId, Category = Category, DisplayName = item.DisplayName, Status = result.IsSuccess ? BaselineStatus.RolledBack : BaselineStatus.Failed, Message = result.IsSuccess ? "Boot setting restored." : result.StandardError.Trim() });
        }

        return results;
    }

    private async Task<List<BootBehaviorItem>> ReadCurrentSettingsAsync(CancellationToken cancellationToken)
    {
        var result = await _commandExecutor.ExecuteAsync("bcdedit", "/enum", cancellationToken);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"bcdedit failed: {result.ErrorSummary}");
        }

        var items = new List<BootBehaviorItem>();
        foreach (var setting in SupportedSettings)
        {
            var match = Regex.Match(result.StandardOutput, $@"^\s*{Regex.Escape(setting.Key)}\s+(.+)$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            items.Add(new BootBehaviorItem
            {
                SettingName = setting.Key,
                DisplayName = setting.Value.DisplayName,
                Description = setting.Value.Description,
                Value = match.Success ? match.Groups[1].Value.Trim() : null
            });
        }

        return items;
    }
}
