using System.Text.RegularExpressions;
using BaseLine.Core;
using BaseLine.Infrastructure;

namespace BaseLine.Services;

public sealed class PowerConfigurationCategoryHandler : IProfileCategoryHandler
{
    private readonly IRegistryAccessor _registryAccessor;
    private readonly ISystemCommandExecutor _commandExecutor;

    private static readonly (string SettingId, string GroupName, string DisplayName, string Subgroup, string PowerSetting, string Unit)[] SettingDefinitions =
    [
        ("display-timeout", "Display", "Display timeout", "SUB_VIDEO", "VIDEOIDLE", " min"),
        ("sleep-timeout", "Sleep", "Sleep timeout", "SUB_SLEEP", "STANDBYIDLE", " min"),
        ("disk-timeout", "Disk", "Disk idle timeout", "SUB_DISK", "DISKIDLE", " min"),
        ("proc-min", "Processor", "Minimum processor state", "SUB_PROCESSOR", "PROCTHROTTLEMIN", "%"),
        ("proc-max", "Processor", "Maximum processor state", "SUB_PROCESSOR", "PROCTHROTTLEMAX", "%")
    ];

    public PowerConfigurationCategoryHandler(IRegistryAccessor registryAccessor, ISystemCommandExecutor commandExecutor)
    {
        _registryAccessor = registryAccessor;
        _commandExecutor = commandExecutor;
    }

    public ProfileCategory Category => ProfileCategory.PowerConfiguration;

    public async Task<CategoryCaptureResult> CaptureAsync(CaptureOptions options, CancellationToken cancellationToken = default)
    {
        var payload = await ReadPowerConfigurationAsync(cancellationToken);
        return new CategoryCaptureResult
        {
            Category = Category,
            Payload = payload,
            Messages = [$"Captured active scheme {payload.ActiveSchemeName} with {payload.Settings.Count} key settings."]
        };
    }

    public async Task<IReadOnlyList<CompareItem>> CompareAsync(BaselineProfile profile, CancellationToken cancellationToken = default)
    {
        var payload = profile.Categories.PowerConfiguration;
        if (payload is null)
        {
            return [];
        }

        var current = await ReadPowerConfigurationAsync(cancellationToken);
        var items = new List<CompareItem>
        {
            new()
            {
                Id = $"{Category}:scheme",
                Category = Category,
                GroupName = "Power Plan",
                DisplayName = "Active power plan",
                ProfileValue = payload.ActiveSchemeName,
                CurrentValue = current.ActiveSchemeName,
                RecommendedAction = payload.ActiveSchemeGuid == current.ActiveSchemeGuid ? "No action" : "Set active power plan",
                SafetyLevel = SafetyLevel.Safe,
                Status = payload.ActiveSchemeGuid == current.ActiveSchemeGuid ? BaselineStatus.AlreadyMatches : BaselineStatus.Ready
            },
            new()
            {
                Id = $"{Category}:hibernate",
                Category = Category,
                GroupName = "Power Plan",
                DisplayName = "Hibernate",
                ProfileValue = payload.HibernateEnabled ? "On" : "Off",
                CurrentValue = current.HibernateEnabled ? "On" : "Off",
                RecommendedAction = payload.HibernateEnabled == current.HibernateEnabled ? "No action" : "Toggle hibernate",
                SafetyLevel = SafetyLevel.Moderate,
                Status = payload.HibernateEnabled == current.HibernateEnabled ? BaselineStatus.AlreadyMatches : BaselineStatus.Ready
            },
            new()
            {
                Id = $"{Category}:fast-startup",
                Category = Category,
                GroupName = "Power Plan",
                DisplayName = "Fast startup",
                ProfileValue = payload.FastStartupEnabled ? "On" : "Off",
                CurrentValue = current.FastStartupEnabled ? "On" : "Off",
                RecommendedAction = payload.FastStartupEnabled == current.FastStartupEnabled ? "No action" : "Apply fast startup state",
                SafetyLevel = SafetyLevel.Moderate,
                Status = payload.FastStartupEnabled == current.FastStartupEnabled ? BaselineStatus.AlreadyMatches : BaselineStatus.Ready
            }
        };

        foreach (var setting in payload.Settings)
        {
            var currentSetting = current.Settings.FirstOrDefault(item => item.SettingId == setting.SettingId);
            var matches = currentSetting is not null && currentSetting.AcValue == setting.AcValue && currentSetting.DcValue == setting.DcValue;

            items.Add(new CompareItem
            {
                Id = $"{Category}:{setting.SettingId}",
                Category = Category,
                GroupName = setting.GroupName,
                DisplayName = setting.DisplayName,
                ProfileValue = $"AC {setting.AcValue}{setting.Unit} / DC {setting.DcValue}{setting.Unit}",
                CurrentValue = currentSetting is null ? "(unsupported)" : $"AC {currentSetting.AcValue}{setting.Unit} / DC {currentSetting.DcValue}{setting.Unit}",
                RecommendedAction = matches ? "No action" : "Apply power setting",
                SafetyLevel = setting.SafetyLevel,
                Status = currentSetting is null ? BaselineStatus.Unsupported : matches ? BaselineStatus.AlreadyMatches : BaselineStatus.Ready
            });
        }

        return items;
    }

    public async Task<ApplyCategoryResult> ApplyAsync(BaselineProfile profile, IReadOnlyList<CompareItem> items, CancellationToken cancellationToken = default)
    {
        var payload = profile.Categories.PowerConfiguration;
        if (payload is null)
        {
            return new ApplyCategoryResult();
        }

        var current = await ReadPowerConfigurationAsync(cancellationToken);
        var results = new List<ApplyResultItem>();
        var rollbackItems = new List<RollbackItem>();

        foreach (var item in items)
        {
            var id = item.Id.Split(':').Last();
            switch (id)
            {
                case "scheme":
                    rollbackItems.Add(new RollbackItem
                    {
                        ItemId = item.Id,
                        Category = Category,
                        DisplayName = item.DisplayName,
                        Kind = RollbackKind.PowerSetting,
                        ExistedBefore = true,
                        PreviousStringValue = current.ActiveSchemeGuid,
                        Metadata = new Dictionary<string, string> { ["powerAction"] = "scheme" }
                    });
                    results.Add(await SetSchemeAsync(item.Id, item.DisplayName, payload.ActiveSchemeGuid, apply: true, cancellationToken));
                    break;
                case "hibernate":
                    rollbackItems.Add(new RollbackItem
                    {
                        ItemId = item.Id,
                        Category = Category,
                        DisplayName = item.DisplayName,
                        Kind = RollbackKind.PowerSetting,
                        ExistedBefore = true,
                        PreviousBooleanValue = current.HibernateEnabled,
                        Metadata = new Dictionary<string, string> { ["powerAction"] = "hibernate" }
                    });
                    results.Add(await ToggleHibernateAsync(item.Id, item.DisplayName, payload.HibernateEnabled, true, cancellationToken));
                    break;
                case "fast-startup":
                    var existing = _registryAccessor.ReadValue(RegistryRoot.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled");
                    rollbackItems.Add(new RollbackItem
                    {
                        ItemId = item.Id,
                        Category = Category,
                        DisplayName = item.DisplayName,
                        Kind = RollbackKind.PowerSetting,
                        ExistedBefore = existing is not null,
                        PreviousRegistryValue = existing,
                        Metadata = new Dictionary<string, string>
                        {
                            ["powerAction"] = "fast-startup",
                            ["path"] = @"SYSTEM\CurrentControlSet\Control\Session Manager\Power",
                            ["valueName"] = "HiberbootEnabled"
                        }
                    });
                    var fastStartupSuccess = _registryAccessor.WriteValue(RegistryRoot.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", new RegistryDataSnapshot
                    {
                        DataKind = RegistryDataKind.DWord,
                        NumericValue = payload.FastStartupEnabled ? 1 : 0
                    });
                    results.Add(new ApplyResultItem { ItemId = item.Id, Category = Category, DisplayName = item.DisplayName, Status = fastStartupSuccess ? BaselineStatus.Applied : BaselineStatus.Failed, Message = fastStartupSuccess ? "Fast startup updated." : "Failed to update fast startup." });
                    break;
                default:
                    var target = payload.Settings.FirstOrDefault(setting => setting.SettingId == id);
                    var currentSetting = current.Settings.FirstOrDefault(setting => setting.SettingId == id);
                    if (target is null || currentSetting is null)
                    {
                        continue;
                    }

                    rollbackItems.Add(new RollbackItem
                    {
                        ItemId = item.Id,
                        Category = Category,
                        DisplayName = item.DisplayName,
                        Kind = RollbackKind.PowerSetting,
                        ExistedBefore = true,
                        PreviousStringValue = current.ActiveSchemeGuid,
                        PreviousStringValues = [currentSetting.AcValue?.ToString() ?? "0", currentSetting.DcValue?.ToString() ?? "0"],
                        Metadata = new Dictionary<string, string> { ["powerAction"] = "setting", ["settingId"] = target.SettingId }
                    });
                    results.Add(await ApplySettingAsync(item, current.ActiveSchemeGuid, target, cancellationToken));
                    break;
            }
        }

        return new ApplyCategoryResult { ResultItems = results, RollbackItems = rollbackItems };
    }

    public async Task<IReadOnlyList<ApplyResultItem>> RollbackAsync(IReadOnlyList<RollbackItem> items, CancellationToken cancellationToken = default)
    {
        var results = new List<ApplyResultItem>();
        foreach (var item in items)
        {
            if (!item.Metadata.TryGetValue("powerAction", out var action))
            {
                continue;
            }

            switch (action)
            {
                case "scheme":
                    results.Add(await SetSchemeAsync(item.ItemId, item.DisplayName, item.PreviousStringValue ?? "SCHEME_CURRENT", apply: false, cancellationToken));
                    break;
                case "hibernate":
                    results.Add(await ToggleHibernateAsync(item.ItemId, item.DisplayName, item.PreviousBooleanValue == true, apply: false, cancellationToken));
                    break;
                case "fast-startup":
                    if (!item.Metadata.TryGetValue("path", out var path) || !item.Metadata.TryGetValue("valueName", out var valueName))
                    {
                        continue;
                    }

                    var success = item.ExistedBefore && item.PreviousRegistryValue is not null
                        ? _registryAccessor.WriteValue(RegistryRoot.LocalMachine, path, valueName, item.PreviousRegistryValue)
                        : _registryAccessor.DeleteValue(RegistryRoot.LocalMachine, path, valueName);
                    results.Add(new ApplyResultItem { ItemId = item.ItemId, Category = Category, DisplayName = item.DisplayName, Status = success ? BaselineStatus.RolledBack : BaselineStatus.Failed, Message = success ? "Fast startup restored." : "Failed to restore fast startup." });
                    break;
                case "setting":
                    if (!item.Metadata.TryGetValue("settingId", out var settingId))
                    {
                        continue;
                    }

                    var definition = SettingDefinitions.First(def => def.SettingId == settingId);
                    var scheme = item.PreviousStringValue ?? "SCHEME_CURRENT";
                    var acValue = item.PreviousStringValues.ElementAtOrDefault(0) ?? "0";
                    var dcValue = item.PreviousStringValues.ElementAtOrDefault(1) ?? "0";
                    var acResult = await _commandExecutor.ExecuteAsync("powercfg", $"/SETACVALUEINDEX {scheme} {definition.Subgroup} {definition.PowerSetting} {acValue}", cancellationToken);
                    var dcResult = await _commandExecutor.ExecuteAsync("powercfg", $"/SETDCVALUEINDEX {scheme} {definition.Subgroup} {definition.PowerSetting} {dcValue}", cancellationToken);
                    var activate = await _commandExecutor.ExecuteAsync("powercfg", $"/S {scheme}", cancellationToken);
                    var allSuccess = acResult.IsSuccess && dcResult.IsSuccess && activate.IsSuccess;
                    results.Add(new ApplyResultItem { ItemId = item.ItemId, Category = Category, DisplayName = item.DisplayName, Status = allSuccess ? BaselineStatus.RolledBack : BaselineStatus.Failed, Message = allSuccess ? "Power setting restored." : $"{acResult.StandardError} {dcResult.StandardError} {activate.StandardError}".Trim() });
                    break;
            }
        }

        return results;
    }

    private async Task<PowerConfigurationPayload> ReadPowerConfigurationAsync(CancellationToken cancellationToken)
    {
        var activeSchemeResult = await _commandExecutor.ExecuteAsync("powercfg", "/GETACTIVESCHEME", cancellationToken);
        if (!activeSchemeResult.IsSuccess)
        {
            throw new InvalidOperationException($"powercfg failed: {activeSchemeResult.ErrorSummary}");
        }

        var schemeMatch = Regex.Match(activeSchemeResult.StandardOutput, @"Power Scheme GUID:\s*([a-f0-9\-]+)\s+\((.+)\)", RegexOptions.IgnoreCase);
        var schemeGuid = schemeMatch.Success ? schemeMatch.Groups[1].Value : "SCHEME_CURRENT";
        var schemeName = schemeMatch.Success ? schemeMatch.Groups[2].Value : "Current";

        var payload = new PowerConfigurationPayload
        {
            ActiveSchemeGuid = schemeGuid,
            ActiveSchemeName = schemeName,
            HibernateEnabled = _registryAccessor.ReadValue(RegistryRoot.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled")?.NumericValue == 1,
            FastStartupEnabled = _registryAccessor.ReadValue(RegistryRoot.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled")?.NumericValue == 1
        };

        foreach (var definition in SettingDefinitions)
        {
            var result = await _commandExecutor.ExecuteAsync("powercfg", $"/Q {schemeGuid} {definition.Subgroup} {definition.PowerSetting}", cancellationToken);
            if (!result.IsSuccess)
            {
                continue;
            }

            payload.Settings.Add(new PowerSettingProfile
            {
                SettingId = definition.SettingId,
                GroupName = definition.GroupName,
                DisplayName = definition.DisplayName,
                AcValue = ParsePowerValue(result.StandardOutput, "Current AC Power Setting Index"),
                DcValue = ParsePowerValue(result.StandardOutput, "Current DC Power Setting Index"),
                Unit = definition.Unit
            });
        }

        return payload;
    }

    private async Task<ApplyResultItem> ApplySettingAsync(CompareItem item, string schemeGuid, PowerSettingProfile target, CancellationToken cancellationToken)
    {
        var definition = SettingDefinitions.First(def => def.SettingId == target.SettingId);
        var acResult = await _commandExecutor.ExecuteAsync("powercfg", $"/SETACVALUEINDEX {schemeGuid} {definition.Subgroup} {definition.PowerSetting} {target.AcValue ?? 0}", cancellationToken);
        var dcResult = await _commandExecutor.ExecuteAsync("powercfg", $"/SETDCVALUEINDEX {schemeGuid} {definition.Subgroup} {definition.PowerSetting} {target.DcValue ?? 0}", cancellationToken);
        var activate = await _commandExecutor.ExecuteAsync("powercfg", $"/S {schemeGuid}", cancellationToken);
        var success = acResult.IsSuccess && dcResult.IsSuccess && activate.IsSuccess;
        return new ApplyResultItem { ItemId = item.Id, Category = Category, DisplayName = item.DisplayName, Status = success ? BaselineStatus.Applied : BaselineStatus.Failed, Message = success ? "Power setting applied." : JoinErrors(acResult, dcResult, activate) };
    }

    private async Task<ApplyResultItem> SetSchemeAsync(string itemId, string displayName, string schemeGuid, bool apply, CancellationToken cancellationToken)
    {
        var result = await _commandExecutor.ExecuteAsync("powercfg", $"/S {schemeGuid}", cancellationToken);
        return new ApplyResultItem
        {
            ItemId = itemId,
            Category = Category,
            DisplayName = displayName,
            Status = result.IsSuccess ? (apply ? BaselineStatus.Applied : BaselineStatus.RolledBack) : BaselineStatus.Failed,
            Message = result.IsSuccess ? (apply ? "Active power scheme updated." : "Power scheme restored.") : result.ErrorSummary
        };
    }

    private async Task<ApplyResultItem> ToggleHibernateAsync(string itemId, string displayName, bool enabled, bool apply, CancellationToken cancellationToken)
    {
        var result = await _commandExecutor.ExecuteAsync("powercfg", enabled ? "/hibernate on" : "/hibernate off", cancellationToken);
        return new ApplyResultItem
        {
            ItemId = itemId,
            Category = Category,
            DisplayName = displayName,
            Status = result.IsSuccess ? (apply ? BaselineStatus.Applied : BaselineStatus.RolledBack) : BaselineStatus.Failed,
            Message = result.IsSuccess ? (apply ? "Hibernate state updated." : "Hibernate state restored.") : result.ErrorSummary
        };
    }

    private static int? ParsePowerValue(string output, string label)
    {
        var match = Regex.Match(output, $@"{Regex.Escape(label)}:\s*0x([0-9a-f]+)", RegexOptions.IgnoreCase);
        return match.Success ? Convert.ToInt32(match.Groups[1].Value, 16) : null;
    }

    private static string JoinErrors(params CommandExecutionResult[] results)
    {
        return string.Join(" ", results.Select(result => result.ErrorSummary).Where(message => !string.IsNullOrWhiteSpace(message))).Trim();
    }
}
