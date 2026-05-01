using System.IO;
using BaseLine.Core;
using BaseLine.Infrastructure;
using Microsoft.Win32;

namespace BaseLine.Services;

public sealed class StartupEnvironmentCategoryHandler : IProfileCategoryHandler
{
    private readonly IRegistryAccessor _registryAccessor;

    public StartupEnvironmentCategoryHandler(IRegistryAccessor registryAccessor)
    {
        _registryAccessor = registryAccessor;
    }

    public ProfileCategory Category => ProfileCategory.StartupEnvironment;

    public Task<CategoryCaptureResult> CaptureAsync(CaptureOptions options, CancellationToken cancellationToken = default)
    {
        var items = new List<StartupItemProfile>();
        items.AddRange(ReadRunEntries(RegistryRoot.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "HKCU Run"));
        items.AddRange(ReadRunEntries(RegistryRoot.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "HKLM Run"));
        items.AddRange(ReadStartupFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "User Startup Folder"));
        items.AddRange(ReadStartupFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "Common Startup Folder"));

        return Task.FromResult(new CategoryCaptureResult
        {
            Category = Category,
            Payload = new StartupEnvironmentPayload { Items = items },
            Messages = [$"Captured {items.Count} startup items."]
        });
    }

    public Task<IReadOnlyList<CompareItem>> CompareAsync(BaselineProfile profile, CancellationToken cancellationToken = default)
    {
        var payload = profile.Categories.StartupEnvironment;
        if (payload is null)
        {
            return Task.FromResult<IReadOnlyList<CompareItem>>([]);
        }

        var current = CaptureCurrentItems().ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        var items = payload.Items.Select(entry =>
        {
            current.TryGetValue(entry.Id, out var currentItem);
            var currentValue = currentItem is null ? "(missing)" : $"{currentItem.Command} | {(currentItem.Enabled ? "Enabled" : "Disabled")}";
            var profileValue = $"{entry.Command} | {(entry.Enabled ? "Enabled" : "Disabled")}";
            var matches = currentItem is not null && currentItem.Command == entry.Command && currentItem.Enabled == entry.Enabled;
            var folderEntry = entry.SourceLocation.Contains("Folder", StringComparison.OrdinalIgnoreCase);

            return new CompareItem
            {
                Id = $"{Category}:{entry.Id}",
                Category = Category,
                GroupName = entry.SourceLocation,
                DisplayName = entry.Name,
                ProfileValue = profileValue,
                CurrentValue = currentValue,
                RecommendedAction = matches ? "No action" : folderEntry ? "Review manually" : "Apply startup entry",
                SafetyLevel = entry.SafetyLevel,
                Status = currentItem is null ? (folderEntry ? BaselineStatus.Unsupported : BaselineStatus.Warning) : matches ? BaselineStatus.AlreadyMatches : BaselineStatus.Ready,
                Notes = folderEntry ? "Startup folder entries are captured for review but not recreated by v1 apply." : entry.StartupApprovalState
            };
        }).ToList();

        return Task.FromResult<IReadOnlyList<CompareItem>>(items);
    }

    public Task<ApplyCategoryResult> ApplyAsync(BaselineProfile profile, IReadOnlyList<CompareItem> items, CancellationToken cancellationToken = default)
    {
        var payload = profile.Categories.StartupEnvironment;
        if (payload is null)
        {
            return Task.FromResult(new ApplyCategoryResult());
        }

        var results = new List<ApplyResultItem>();
        var rollbackItems = new List<RollbackItem>();

        foreach (var item in items)
        {
            var separatorIndex = item.Id.IndexOf(':');
            var startupId = separatorIndex >= 0 ? item.Id[(separatorIndex + 1)..] : item.Id;
            var startupItem = payload.Items.FirstOrDefault(entry => entry.Id == startupId);
            if (startupItem is null)
            {
                continue;
            }

            if (startupItem.SourceLocation.Contains("Folder", StringComparison.OrdinalIgnoreCase))
            {
                results.Add(new ApplyResultItem { ItemId = item.Id, Category = Category, DisplayName = item.DisplayName, Status = BaselineStatus.Skipped, Message = "Startup folder entries are compare-only in v1." });
                continue;
            }

            var (root, path) = startupItem.SourceLocation.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase)
                ? (RegistryRoot.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run")
                : (RegistryRoot.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run");

            var previous = _registryAccessor.ReadValue(root, path, startupItem.Name);
            rollbackItems.Add(new RollbackItem
            {
                ItemId = item.Id,
                Category = Category,
                DisplayName = item.DisplayName,
                Kind = RollbackKind.StartupItem,
                ExistedBefore = previous is not null,
                PreviousRegistryValue = previous,
                Metadata = new Dictionary<string, string>
                {
                    ["root"] = root.ToString(),
                    ["path"] = path,
                    ["valueName"] = startupItem.Name
                }
            });

            var success = startupItem.Enabled
                ? _registryAccessor.WriteValue(root, path, startupItem.Name, new RegistryDataSnapshot { DataKind = RegistryDataKind.String, StringValue = startupItem.Command })
                : _registryAccessor.DeleteValue(root, path, startupItem.Name);

            results.Add(new ApplyResultItem { ItemId = item.Id, Category = Category, DisplayName = item.DisplayName, Status = success ? BaselineStatus.Applied : BaselineStatus.Failed, Message = success ? "Startup entry updated." : "Failed to update startup entry." });
        }

        return Task.FromResult(new ApplyCategoryResult { ResultItems = results, RollbackItems = rollbackItems });
    }

    public Task<IReadOnlyList<ApplyResultItem>> RollbackAsync(IReadOnlyList<RollbackItem> items, CancellationToken cancellationToken = default)
    {
        var results = new List<ApplyResultItem>();
        foreach (var item in items)
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

            results.Add(new ApplyResultItem { ItemId = item.ItemId, Category = Category, DisplayName = item.DisplayName, Status = success ? BaselineStatus.RolledBack : BaselineStatus.Failed, Message = success ? "Startup item restored." : "Failed to restore startup item." });
        }

        return Task.FromResult<IReadOnlyList<ApplyResultItem>>(results);
    }

    private IEnumerable<StartupItemProfile> CaptureCurrentItems()
    {
        return ReadRunEntries(RegistryRoot.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "HKCU Run")
            .Concat(ReadRunEntries(RegistryRoot.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "HKLM Run"))
            .Concat(ReadStartupFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "User Startup Folder"))
            .Concat(ReadStartupFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "Common Startup Folder"));
    }

    private IEnumerable<StartupItemProfile> ReadRunEntries(RegistryRoot root, string path, string source)
    {
        using var baseKey = root == RegistryRoot.LocalMachine
            ? RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default)
            : RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default);
        using var key = baseKey.OpenSubKey(path);
        if (key is null)
        {
            yield break;
        }

        foreach (var valueName in key.GetValueNames())
        {
            yield return new StartupItemProfile
            {
                Id = $"{source}:{valueName}",
                Name = valueName,
                SourceLocation = source,
                Command = key.GetValue(valueName)?.ToString() ?? string.Empty,
                Enabled = true,
                StartupApprovalState = ReadStartupApproval(source, valueName)
            };
        }
    }

    private IEnumerable<StartupItemProfile> ReadStartupFolder(string path, string source)
    {
        if (!Directory.Exists(path))
        {
            yield break;
        }

        foreach (var file in Directory.GetFiles(path))
        {
            yield return new StartupItemProfile
            {
                Id = $"{source}:{Path.GetFileName(file)}",
                Name = Path.GetFileName(file),
                SourceLocation = source,
                Command = file,
                Enabled = true
            };
        }
    }

    private string? ReadStartupApproval(string source, string valueName)
    {
        var approvalPath = source.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase)
            ? @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"
            : @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        var root = source.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase) ? RegistryRoot.LocalMachine : RegistryRoot.CurrentUser;
        var snapshot = _registryAccessor.ReadValue(root, approvalPath, valueName);
        return snapshot?.BinaryValue.Length > 0 && snapshot.BinaryValue[0] == 0x03 ? "Disabled" : "Enabled";
    }
}
