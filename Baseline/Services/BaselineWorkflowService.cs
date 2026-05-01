using BaseLine.Core;
using BaseLine.Infrastructure;

namespace BaseLine.Services;

public sealed class BaselineWorkflowService
{
    private readonly IReadOnlyDictionary<ProfileCategory, IProfileCategoryHandler> _handlers;
    private readonly MachineInfoService _machineInfoService;
    private readonly IProfileFileService _profileFileService;
    private readonly IRollbackStore _rollbackStore;
    private readonly IRecentProfilesStore _recentProfilesStore;

    public BaselineWorkflowService(
        IEnumerable<IProfileCategoryHandler> handlers,
        MachineInfoService machineInfoService,
        IProfileFileService profileFileService,
        IRollbackStore rollbackStore,
        IRecentProfilesStore recentProfilesStore)
    {
        _handlers = handlers.ToDictionary(handler => handler.Category);
        _machineInfoService = machineInfoService;
        _profileFileService = profileFileService;
        _rollbackStore = rollbackStore;
        _recentProfilesStore = recentProfilesStore;
    }

    public async Task<(BaselineProfile Profile, CaptureSummary Summary)> CaptureProfileAsync(CaptureOptions options, CancellationToken cancellationToken = default)
    {
        var requestedCategories = options.SelectedCategories.Distinct().ToList();
        var profile = new BaselineProfile
        {
            Metadata = _machineInfoService.CreateProfileMetadata(options.ProfileName, requestedCategories)
        };

        var summary = new CaptureSummary
        {
            RequestedCategories = requestedCategories.Count
        };

        foreach (var category in requestedCategories)
        {
            if (!_handlers.TryGetValue(category, out var handler))
            {
                summary.FailedCategories++;
                summary.Messages.Add($"Capture unavailable for {GetCategoryLabel(category)}.");
                continue;
            }

            try
            {
                var result = await handler.CaptureAsync(options, cancellationToken);
                AssignPayload(profile.Categories, category, result.Payload);
                summary.CapturedCategories++;
                summary.Messages.AddRange(result.Messages);
            }
            catch (Exception ex)
            {
                summary.FailedCategories++;
                summary.Messages.Add($"Failed to capture {GetCategoryLabel(category)}: {GetErrorMessage(ex)}");
            }
        }

        return (profile, summary);
    }

    public async Task SaveProfileAsync(BaselineProfile profile, string path, CancellationToken cancellationToken = default)
    {
        await _profileFileService.SaveAsync(profile, path, cancellationToken);
        await _recentProfilesStore.AddAsync(path, cancellationToken);
    }

    public async Task<BaselineProfile> LoadProfileAsync(string path, CancellationToken cancellationToken = default)
    {
        var profile = await _profileFileService.LoadAsync(path, cancellationToken);
        await _recentProfilesStore.AddAsync(path, cancellationToken);
        return profile;
    }

    public async Task<CompareReport> CompareAsync(BaselineProfile profile, CancellationToken cancellationToken = default)
    {
        var report = new CompareReport
        {
            ProfileName = profile.Metadata.ProfileName,
            GeneratedAt = DateTimeOffset.Now
        };

        foreach (var category in profile.Metadata.SelectedCategories)
        {
            if (!_handlers.TryGetValue(category, out var handler))
            {
                report.Messages.Add($"Compare unavailable for {GetCategoryLabel(category)}.");
                report.Items.Add(CreateCategoryCompareIssue(category, BaselineStatus.Unsupported, "No category handler is registered."));
                continue;
            }

            if (!HasPayload(profile, category))
            {
                report.Messages.Add($"{GetCategoryLabel(category)} is selected in the profile but has no captured payload.");
                report.Items.Add(CreateCategoryCompareIssue(category, BaselineStatus.Failed, "The loaded profile does not contain captured data for this category."));
                continue;
            }

            try
            {
                report.Items.AddRange(await handler.CompareAsync(profile, cancellationToken));
            }
            catch (Exception ex)
            {
                var message = $"Failed to compare {GetCategoryLabel(category)}: {GetErrorMessage(ex)}";
                report.Messages.Add(message);
                report.Items.Add(CreateCategoryCompareIssue(category, BaselineStatus.Failed, message));
            }
        }

        return report;
    }

    public async Task<ApplySession> ApplyAsync(BaselineProfile profile, IReadOnlyList<CompareItem> itemsToApply, CancellationToken cancellationToken = default)
    {
        var session = new ApplySession
        {
            ProfileName = profile.Metadata.ProfileName,
            RollbackRecord = new RollbackRecord
            {
                SessionId = Guid.NewGuid(),
                ProfileName = profile.Metadata.ProfileName,
                CreatedAt = DateTimeOffset.Now
            }
        };

        foreach (var group in itemsToApply.GroupBy(item => item.Category))
        {
            if (!_handlers.TryGetValue(group.Key, out var handler))
            {
                session.Results.AddRange(group.Select(item => CreateApplyResult(item, BaselineStatus.Unsupported, "No category handler is registered.")));
                continue;
            }

            if (!HasPayload(profile, group.Key))
            {
                session.Results.AddRange(group.Select(item => CreateApplyResult(item, BaselineStatus.Failed, "The loaded profile does not contain captured data for this category.")));
                continue;
            }

            var requestedItems = group.ToList();
            try
            {
                var categoryResult = await handler.ApplyAsync(profile, requestedItems, cancellationToken);
                session.Results.AddRange(categoryResult.ResultItems);
                session.RollbackRecord.Items.AddRange(categoryResult.RollbackItems);
                AppendMissingApplyResults(session.Results, requestedItems);
            }
            catch (Exception ex)
            {
                var message = GetErrorMessage(ex);
                session.Results.AddRange(requestedItems.Select(item => CreateApplyResult(item, BaselineStatus.Failed, message)));
            }
        }

        if (session.RollbackRecord.Items.Count > 0)
        {
            await _rollbackStore.SaveAsync(session.RollbackRecord, cancellationToken);
        }

        return session;
    }

    public async Task<IReadOnlyList<ApplyResultItem>> RollbackAsync(RollbackRecord record, CancellationToken cancellationToken = default)
    {
        var results = new List<ApplyResultItem>();
        foreach (var group in record.Items.GroupBy(item => item.Category))
        {
            if (!_handlers.TryGetValue(group.Key, out var handler))
            {
                results.AddRange(group.Select(item => new ApplyResultItem
                {
                    ItemId = item.ItemId,
                    Category = item.Category,
                    DisplayName = item.DisplayName,
                    Status = BaselineStatus.Unsupported,
                    Message = "No category handler is registered."
                }));
                continue;
            }

            var requestedItems = group.ToList();
            try
            {
                results.AddRange(await handler.RollbackAsync(requestedItems, cancellationToken));
                AppendMissingRollbackResults(results, requestedItems);
            }
            catch (Exception ex)
            {
                var message = GetErrorMessage(ex);
                results.AddRange(requestedItems.Select(item => new ApplyResultItem
                {
                    ItemId = item.ItemId,
                    Category = item.Category,
                    DisplayName = item.DisplayName,
                    Status = BaselineStatus.Failed,
                    Message = message
                }));
            }
        }

        return results;
    }

    public Task<IReadOnlyList<RollbackRecord>> LoadRollbackHistoryAsync(CancellationToken cancellationToken = default) => _rollbackStore.LoadAllAsync(cancellationToken);

    public Task<IReadOnlyList<string>> LoadRecentProfilesAsync(CancellationToken cancellationToken = default) => _recentProfilesStore.LoadAsync(cancellationToken);

    private static void AppendMissingApplyResults(List<ApplyResultItem> results, IReadOnlyList<CompareItem> requestedItems)
    {
        var returnedIds = results.Select(item => item.ItemId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in requestedItems.Where(item => !returnedIds.Contains(item.Id)))
        {
            results.Add(CreateApplyResult(item, BaselineStatus.Skipped, "No apply result was returned for this item."));
        }
    }

    private static void AppendMissingRollbackResults(List<ApplyResultItem> results, IReadOnlyList<RollbackItem> requestedItems)
    {
        var returnedIds = results.Select(item => item.ItemId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in requestedItems.Where(item => !returnedIds.Contains(item.ItemId)))
        {
            results.Add(new ApplyResultItem
            {
                ItemId = item.ItemId,
                Category = item.Category,
                DisplayName = item.DisplayName,
                Status = BaselineStatus.Skipped,
                Message = "No rollback result was returned for this item."
            });
        }
    }

    private static CompareItem CreateCategoryCompareIssue(ProfileCategory category, BaselineStatus status, string message)
    {
        var label = GetCategoryLabel(category);
        return new CompareItem
        {
            Id = $"{category}:category-status",
            Category = category,
            GroupName = label,
            DisplayName = $"{label} availability",
            ProfileValue = "(captured category)",
            CurrentValue = "(unavailable)",
            RecommendedAction = "Review category status",
            SafetyLevel = SafetyLevel.Advanced,
            Status = status,
            Notes = message
        };
    }

    private static ApplyResultItem CreateApplyResult(CompareItem item, BaselineStatus status, string message)
    {
        return new ApplyResultItem
        {
            ItemId = item.Id,
            Category = item.Category,
            DisplayName = item.DisplayName,
            Status = status,
            Message = message
        };
    }

    private static bool HasPayload(BaselineProfile profile, ProfileCategory category)
    {
        return category switch
        {
            ProfileCategory.Services => profile.Categories.Services is not null,
            ProfileCategory.BootBehavior => profile.Categories.BootBehavior is not null,
            ProfileCategory.RegistryTweaks => profile.Categories.RegistryTweaks is not null,
            ProfileCategory.Policies => profile.Categories.Policies is not null,
            ProfileCategory.Network => profile.Categories.Network is not null,
            ProfileCategory.StartupEnvironment => profile.Categories.StartupEnvironment is not null,
            ProfileCategory.ScheduledTasks => profile.Categories.ScheduledTasks is not null,
            ProfileCategory.PowerConfiguration => profile.Categories.PowerConfiguration is not null,
            _ => false
        };
    }

    private static string GetCategoryLabel(ProfileCategory category)
    {
        return category switch
        {
            ProfileCategory.BootBehavior => "Boot Behavior",
            ProfileCategory.RegistryTweaks => "Registry Tweaks",
            ProfileCategory.StartupEnvironment => "Startup Environment",
            ProfileCategory.ScheduledTasks => "Scheduled Tasks",
            ProfileCategory.PowerConfiguration => "Power Configuration",
            _ => category.ToString()
        };
    }

    private static string GetErrorMessage(Exception exception)
    {
        var message = exception.GetBaseException().Message;
        return string.IsNullOrWhiteSpace(message) ? "Operation failed." : message.Trim();
    }

    private static void AssignPayload(CategoryPayloads payloads, ProfileCategory category, object payload)
    {
        switch (category)
        {
            case ProfileCategory.Services:
                payloads.Services = (ServicesPayload)payload;
                break;
            case ProfileCategory.BootBehavior:
                payloads.BootBehavior = (BootBehaviorPayload)payload;
                break;
            case ProfileCategory.RegistryTweaks:
                payloads.RegistryTweaks = (RegistryTweaksPayload)payload;
                break;
            case ProfileCategory.Policies:
                payloads.Policies = (PoliciesPayload)payload;
                break;
            case ProfileCategory.Network:
                payloads.Network = (NetworkPayload)payload;
                break;
            case ProfileCategory.StartupEnvironment:
                payloads.StartupEnvironment = (StartupEnvironmentPayload)payload;
                break;
            case ProfileCategory.ScheduledTasks:
                payloads.ScheduledTasks = (ScheduledTasksPayload)payload;
                break;
            case ProfileCategory.PowerConfiguration:
                payloads.PowerConfiguration = (PowerConfigurationPayload)payload;
                break;
        }
    }
}
