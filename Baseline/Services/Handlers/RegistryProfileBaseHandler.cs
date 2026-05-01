using BaseLine.Core;
using BaseLine.Infrastructure;

namespace BaseLine.Services;

public abstract class RegistryProfileBaseHandler : IProfileCategoryHandler
{
    protected readonly IRegistryAccessor RegistryAccessor;

    protected RegistryProfileBaseHandler(IRegistryAccessor registryAccessor)
    {
        RegistryAccessor = registryAccessor;
    }

    public abstract ProfileCategory Category { get; }

    protected abstract IEnumerable<StructuredRegistryTemplate> GetSelectedTemplates(CaptureOptions options);
    protected abstract IReadOnlyList<RegistryProfileEntry> GetPayloadItems(BaselineProfile profile);

    public Task<CategoryCaptureResult> CaptureAsync(CaptureOptions options, CancellationToken cancellationToken = default)
    {
        var entries = new List<RegistryProfileEntry>();
        foreach (var template in GetSelectedTemplates(options))
        {
            var value = RegistryAccessor.ReadValue(template.Root, template.Path, template.ValueName);
            if (value is null)
            {
                continue;
            }

            entries.Add(new RegistryProfileEntry
            {
                Id = template.Id,
                GroupName = template.GroupName,
                DisplayName = template.DisplayName,
                Root = template.Root,
                Path = template.Path,
                ValueName = template.ValueName,
                Value = value,
                SafetyLevel = template.SafetyLevel,
                CompatibilityNote = template.CompatibilityNote,
                IsCustom = template.IsCustom
            });
        }

        object payload = Category == ProfileCategory.RegistryTweaks
            ? new RegistryTweaksPayload { Items = entries }
            : new PoliciesPayload { Items = entries };

        return Task.FromResult(new CategoryCaptureResult
        {
            Category = Category,
            Payload = payload,
            Messages = [$"Captured {entries.Count} structured {Category} entries."]
        });
    }

    public Task<IReadOnlyList<CompareItem>> CompareAsync(BaselineProfile profile, CancellationToken cancellationToken = default)
    {
        var entries = GetPayloadItems(profile);
        var items = entries.Select(entry =>
        {
            var current = RegistryAccessor.ReadValue(entry.Root, entry.Path, entry.ValueName);
            var currentText = current?.ToDisplayString() ?? "(missing)";
            var profileText = entry.Value.ToDisplayString();
            var matches = current is not null && currentText == profileText;

            return new CompareItem
            {
                Id = $"{Category}:{entry.Id}",
                Category = Category,
                GroupName = entry.GroupName,
                DisplayName = entry.DisplayName,
                ProfileValue = profileText,
                CurrentValue = currentText,
                RecommendedAction = matches ? "No action" : "Apply structured registry value",
                SafetyLevel = entry.SafetyLevel,
                Status = matches ? BaselineStatus.AlreadyMatches : current is null ? BaselineStatus.Warning : BaselineStatus.Ready,
                Notes = entry.CompatibilityNote
            };
        }).ToList();

        return Task.FromResult<IReadOnlyList<CompareItem>>(items);
    }

    public Task<ApplyCategoryResult> ApplyAsync(BaselineProfile profile, IReadOnlyList<CompareItem> items, CancellationToken cancellationToken = default)
    {
        var entryMap = GetPayloadItems(profile).ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        var results = new List<ApplyResultItem>();
        var rollbackItems = new List<RollbackItem>();

        foreach (var item in items)
        {
            var entryId = item.Id.Split(':').Last();
            if (!entryMap.TryGetValue(entryId, out var entry))
            {
                continue;
            }

            var existing = RegistryAccessor.ReadValue(entry.Root, entry.Path, entry.ValueName);
            rollbackItems.Add(new RollbackItem
            {
                ItemId = item.Id,
                Category = Category,
                DisplayName = item.DisplayName,
                Kind = RollbackKind.RegistryValue,
                ExistedBefore = existing is not null,
                PreviousRegistryValue = existing,
                Metadata = new Dictionary<string, string>
                {
                    ["root"] = entry.Root.ToString(),
                    ["path"] = entry.Path,
                    ["valueName"] = entry.ValueName
                }
            });

            var success = RegistryAccessor.WriteValue(entry.Root, entry.Path, entry.ValueName, entry.Value);
            results.Add(new ApplyResultItem { ItemId = item.Id, Category = Category, DisplayName = item.DisplayName, Status = success ? BaselineStatus.Applied : BaselineStatus.Failed, Message = success ? "Registry value applied." : "Failed to apply registry value." });
        }

        return Task.FromResult(new ApplyCategoryResult { ResultItems = results, RollbackItems = rollbackItems });
    }

    public Task<IReadOnlyList<ApplyResultItem>> RollbackAsync(IReadOnlyList<RollbackItem> items, CancellationToken cancellationToken = default)
    {
        var results = new List<ApplyResultItem>();
        foreach (var item in items)
        {
            if (!TryGetTarget(item, out var root, out var path, out var valueName))
            {
                continue;
            }

            var success = item.ExistedBefore && item.PreviousRegistryValue is not null
                ? RegistryAccessor.WriteValue(root, path, valueName, item.PreviousRegistryValue)
                : RegistryAccessor.DeleteValue(root, path, valueName);

            results.Add(new ApplyResultItem { ItemId = item.ItemId, Category = Category, DisplayName = item.DisplayName, Status = success ? BaselineStatus.RolledBack : BaselineStatus.Failed, Message = success ? "Registry value restored." : "Failed to restore registry value." });
        }

        return Task.FromResult<IReadOnlyList<ApplyResultItem>>(results);
    }

    private static bool TryGetTarget(RollbackItem item, out RegistryRoot root, out string path, out string valueName)
    {
        root = RegistryRoot.LocalMachine;
        path = string.Empty;
        valueName = string.Empty;

        if (!item.Metadata.TryGetValue("root", out var rootText) ||
            !item.Metadata.TryGetValue("path", out var resolvedPath) ||
            !item.Metadata.TryGetValue("valueName", out var resolvedValueName))
        {
            return false;
        }

        path = resolvedPath;
        valueName = resolvedValueName;
        return Enum.TryParse(rootText, out root);
    }
}
