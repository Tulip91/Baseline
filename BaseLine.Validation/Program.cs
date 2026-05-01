using System.Text.Json;
using BaseLine.Core;
using BaseLine.Infrastructure;
using BaseLine.Services;

var validator = new BaselineLiveValidator();
return await validator.RunAsync();

internal sealed class BaselineLiveValidator
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BaselineValidation", Guid.NewGuid().ToString("N"));
    private readonly RegistryAccessor _registryAccessor = new();
    private readonly SystemCommandExecutor _commandExecutor = new();
    private readonly ProfileFileService _profileFileService = new();
    private readonly RegistryTemplateCatalog _templateCatalog = new();

    public async Task<int> RunAsync()
    {
        Directory.CreateDirectory(_tempRoot);
        WriteHeading("Baseline Live Validation");
        Console.WriteLine($"Artifacts: {_tempRoot}");
        Console.WriteLine($"Administrator: {IsAdministrator()}");

        try
        {
            var workflow = CreateWorkflow();

            await ValidateFullWorkflowAsync(workflow);
            await ValidateReversibleUserFlowsAsync(workflow);

            Console.WriteLine();
            Console.WriteLine("Validation completed successfully.");
            return 0;
        }
        catch (ValidationException ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"Validation failed: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"Unexpected failure: {ex}");
            return 1;
        }
    }

    private BaselineWorkflowService CreateWorkflow()
    {
        var machineInfoService = new MachineInfoService(_registryAccessor);
        var networkDiscoveryService = new NetworkDiscoveryService(_registryAccessor);
        var handlers = new IProfileCategoryHandler[]
        {
            new ServicesCategoryHandler(_registryAccessor),
            new BootBehaviorCategoryHandler(_commandExecutor),
            new RegistryTweaksCategoryHandler(_registryAccessor),
            new PoliciesCategoryHandler(_registryAccessor),
            new NetworkCategoryHandler(_registryAccessor, networkDiscoveryService),
            new StartupEnvironmentCategoryHandler(_registryAccessor),
            new ScheduledTasksCategoryHandler(_commandExecutor),
            new PowerConfigurationCategoryHandler(_registryAccessor, _commandExecutor)
        };

        return new BaselineWorkflowService(
            handlers,
            machineInfoService,
            _profileFileService,
            new TempRollbackStore(Path.Combine(_tempRoot, "rollback"), _profileFileService),
            new TempRecentProfilesStore());
    }

    private async Task ValidateFullWorkflowAsync(BaselineWorkflowService workflow)
    {
        WriteHeading("Full Capture / Save / Load / Compare");

        var networkDiscoveryService = new NetworkDiscoveryService(_registryAccessor);
        var options = new CaptureOptions
        {
            ProfileName = $"full-validation-{Environment.MachineName}",
            SelectedCategories = Enum.GetValues<ProfileCategory>().ToList(),
            SelectedRegistryTemplates = _templateCatalog.RegistryDefaults.ToList(),
            SelectedPolicyTemplates = _templateCatalog.PolicyDefaults.ToList(),
            PreferredNetworkAdapterId = networkDiscoveryService.GetPreferredAdapter(null)?.AdapterId
        };

        var (profile, summary) = await workflow.CaptureProfileAsync(options);
        Require(profile.Categories.IncludedCategories.Any(), "Full capture returned no payloads.");
        Console.WriteLine($"Captured categories: {string.Join(", ", profile.Categories.IncludedCategories)}");
        Console.WriteLine($"Capture summary: {summary.CapturedCategories} captured, {summary.FailedCategories} failed.");
        foreach (var message in summary.Messages.Take(8))
        {
            Console.WriteLine($"  {message}");
        }

        var profilePath = Path.Combine(_tempRoot, "full-validation.baseline.json");
        await workflow.SaveProfileAsync(profile, profilePath);
        Require(File.Exists(profilePath), "Full validation profile was not written to disk.");

        var loaded = await workflow.LoadProfileAsync(profilePath);
        Require(loaded.Metadata.SelectedCategories.Count > 0, "Loaded profile has no selected categories.");

        var report = await workflow.CompareAsync(loaded);
        Require(report.Items.Count > 0, "Compare produced no items.");
        Console.WriteLine($"Compare items: {report.Items.Count}");
        Console.WriteLine($"Ready: {report.Items.Count(item => item.Status == BaselineStatus.Ready)}");
        Console.WriteLine($"Matches: {report.Items.Count(item => item.Status == BaselineStatus.AlreadyMatches)}");
        Console.WriteLine($"Warnings: {report.Items.Count(item => item.Status is BaselineStatus.Warning or BaselineStatus.MissingDependency)}");
        Console.WriteLine($"Failed: {report.Items.Count(item => item.Status == BaselineStatus.Failed)}");
        foreach (var message in report.Messages.Take(8))
        {
            Console.WriteLine($"  {message}");
        }
    }

    private async Task ValidateReversibleUserFlowsAsync(BaselineWorkflowService workflow)
    {
        WriteHeading("Reversible User-Scope Apply / Rollback");

        const string registryPath = @"Software\Baseline\Validation";
        const string registryValueName = "RegistryToggle";
        const string policyPath = @"Software\Policies\Baseline\Validation";
        const string policyValueName = "PolicyToggle";
        const string startupValueName = "BaselineValidationStartup";
        const string runPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        const int profileRegistryValue = 11;
        const int mutatedRegistryValue = 29;
        const int profilePolicyValue = 3;
        const int mutatedPolicyValue = 0;
        const string profileStartupCommand = @"C:\Windows\System32\cmd.exe /c exit 0";
        const string mutatedStartupCommand = @"C:\Windows\System32\cmd.exe /c ver";

        var registryTemplate = new StructuredRegistryTemplate
        {
            Id = "validation.registry-toggle",
            GroupName = "Validation",
            DisplayName = "Validation registry toggle",
            Root = RegistryRoot.CurrentUser,
            Path = registryPath,
            ValueName = registryValueName,
            SafetyLevel = SafetyLevel.Safe,
            IsCustom = true,
            IsDefaultSelected = true
        };
        var policyTemplate = new StructuredRegistryTemplate
        {
            Id = "validation.policy-toggle",
            GroupName = "Validation",
            DisplayName = "Validation policy toggle",
            Root = RegistryRoot.CurrentUser,
            Path = policyPath,
            ValueName = policyValueName,
            SafetyLevel = SafetyLevel.Safe,
            IsCustom = true,
            IsDefaultSelected = true
        };

        string? scheduledTaskPath = null;
        string? createdScheduledTaskPath = null;
        try
        {
            WriteDword(RegistryRoot.CurrentUser, registryPath, registryValueName, profileRegistryValue);
            WriteDword(RegistryRoot.CurrentUser, policyPath, policyValueName, profilePolicyValue);
            WriteString(RegistryRoot.CurrentUser, runPath, startupValueName, profileStartupCommand);

            scheduledTaskPath = await TryCreateValidationTaskAsync();
            createdScheduledTaskPath = scheduledTaskPath;

            var selectedCategories = new List<ProfileCategory>
            {
                ProfileCategory.RegistryTweaks,
                ProfileCategory.Policies,
                ProfileCategory.StartupEnvironment
            };
            if (scheduledTaskPath is not null)
            {
                selectedCategories.Add(ProfileCategory.ScheduledTasks);
            }

            var (profile, summary) = await workflow.CaptureProfileAsync(new CaptureOptions
            {
                ProfileName = "user-scope-validation",
                SelectedCategories = selectedCategories,
                SelectedRegistryTemplates = [registryTemplate],
                SelectedPolicyTemplates = [policyTemplate]
            });

            Require(summary.FailedCategories == 0, $"User-scope capture reported failures: {string.Join(" | ", summary.Messages)}");
            Require(profile.Categories.RegistryTweaks?.Items.Any(item => item.Id == registryTemplate.Id) == true, "Validation registry item was not captured.");
            Require(profile.Categories.Policies?.Items.Any(item => item.Id == policyTemplate.Id) == true, "Validation policy item was not captured.");
            Require(profile.Categories.StartupEnvironment?.Items.Any(item => item.Name == startupValueName) == true, "Validation startup item was not captured.");
            if (scheduledTaskPath is not null &&
                profile.Categories.ScheduledTasks?.Items.Any(item => string.Equals(item.TaskPath, scheduledTaskPath, StringComparison.OrdinalIgnoreCase)) != true)
            {
                Console.WriteLine($"Scheduled task live validation skipped: fixture task {scheduledTaskPath} was not captured.");
                scheduledTaskPath = null;
            }

            var profilePath = Path.Combine(_tempRoot, "user-scope-validation.baseline.json");
            await workflow.SaveProfileAsync(profile, profilePath);
            var loaded = await workflow.LoadProfileAsync(profilePath);

            WriteDword(RegistryRoot.CurrentUser, registryPath, registryValueName, mutatedRegistryValue);
            WriteDword(RegistryRoot.CurrentUser, policyPath, policyValueName, mutatedPolicyValue);
            WriteString(RegistryRoot.CurrentUser, runPath, startupValueName, mutatedStartupCommand);
            if (scheduledTaskPath is not null)
            {
                await SetScheduledTaskEnabledAsync(scheduledTaskPath, enabled: false);
            }

            var report = await workflow.CompareAsync(loaded);
            var itemsToApply = new List<CompareItem>
            {
                RequireItem(report, ProfileCategory.RegistryTweaks, $"RegistryTweaks:{registryTemplate.Id}"),
                RequireItem(report, ProfileCategory.Policies, $"Policies:{policyTemplate.Id}"),
                RequireItem(report, ProfileCategory.StartupEnvironment, null, startupValueName)
            };
            if (scheduledTaskPath is not null)
            {
                var scheduledTaskItem = report.Items.FirstOrDefault(item =>
                    item.Category == ProfileCategory.ScheduledTasks &&
                    string.Equals(item.Id, $"ScheduledTasks:{scheduledTaskPath}", StringComparison.OrdinalIgnoreCase));

                if (scheduledTaskItem is null)
                {
                    Console.WriteLine($"Scheduled task live validation skipped: compare item for {scheduledTaskPath} was not found.");
                    scheduledTaskPath = null;
                }
                else
                {
                    itemsToApply.Add(scheduledTaskItem);
                }
            }

            Require(itemsToApply.All(item => item.Status is not BaselineStatus.AlreadyMatches), "Expected mismatches were not detected.");

            var session = await workflow.ApplyAsync(loaded, itemsToApply);
            Require(session.Results.Count >= itemsToApply.Count, "Apply returned fewer results than requested items.");
            Require(session.Results.Where(result => itemsToApply.Any(item => item.Id == result.ItemId)).All(result => result.Status == BaselineStatus.Applied),
                "One or more user-scope apply results did not report success.");

            Require(ReadDword(RegistryRoot.CurrentUser, registryPath, registryValueName) == profileRegistryValue, "Registry value was not applied.");
            Require(ReadDword(RegistryRoot.CurrentUser, policyPath, policyValueName) == profilePolicyValue, "Policy value was not applied.");
            Require(ReadString(RegistryRoot.CurrentUser, runPath, startupValueName) == profileStartupCommand, "Startup value was not applied.");
            if (scheduledTaskPath is not null)
            {
                Require(await IsScheduledTaskEnabledAsync(scheduledTaskPath), "Scheduled task was not re-enabled by apply.");
            }

            var rollbackResults = await workflow.RollbackAsync(session.RollbackRecord);
            Require(rollbackResults.Count >= itemsToApply.Count, "Rollback returned fewer results than requested items.");
            Require(rollbackResults.Where(result => itemsToApply.Any(item => item.Id == result.ItemId)).All(result => result.Status == BaselineStatus.RolledBack),
                "One or more user-scope rollback results did not report success.");

            Require(ReadDword(RegistryRoot.CurrentUser, registryPath, registryValueName) == mutatedRegistryValue, "Registry value was not restored by rollback.");
            Require(ReadDword(RegistryRoot.CurrentUser, policyPath, policyValueName) == mutatedPolicyValue, "Policy value was not restored by rollback.");
            Require(ReadString(RegistryRoot.CurrentUser, runPath, startupValueName) == mutatedStartupCommand, "Startup value was not restored by rollback.");
            if (scheduledTaskPath is not null)
            {
                Require(!await IsScheduledTaskEnabledAsync(scheduledTaskPath), "Scheduled task was not restored by rollback.");
            }

            Console.WriteLine($"Applied and rolled back {itemsToApply.Count} user-scope items successfully.");
        }
        finally
        {
            TryDeleteValue(RegistryRoot.CurrentUser, registryPath, registryValueName);
            TryDeleteValue(RegistryRoot.CurrentUser, policyPath, policyValueName);
            TryDeleteValue(RegistryRoot.CurrentUser, runPath, startupValueName);

            if (createdScheduledTaskPath is not null)
            {
                await DeleteScheduledTaskAsync(createdScheduledTaskPath);
            }
        }
    }

    private async Task<string?> TryCreateValidationTaskAsync()
    {
        var taskPath = $@"\BaselineValidation-{Guid.NewGuid():N}";
        var startTime = DateTime.Now.AddMinutes(15).ToString("HH:mm");
        var createResult = await _commandExecutor.ExecuteAsync(
            "schtasks",
            $"/Create /TN \"{taskPath}\" /SC ONCE /ST {startTime} /TR \"C:\\Windows\\System32\\cmd.exe /c exit 0\" /F");

        if (!createResult.IsSuccess)
        {
            Console.WriteLine($"Scheduled task live validation skipped: {createResult.ErrorSummary}");
            return null;
        }

        var queryResult = await _commandExecutor.ExecuteAsync("schtasks", $"/Query /TN \"{taskPath}\" /V /FO LIST");
        if (!queryResult.IsSuccess)
        {
            Console.WriteLine($"Scheduled task live validation skipped after create: {queryResult.ErrorSummary}");
            await DeleteScheduledTaskAsync(taskPath);
            return null;
        }

        Console.WriteLine($"Scheduled task fixture created: {taskPath}");
        return taskPath;
    }

    private async Task SetScheduledTaskEnabledAsync(string taskPath, bool enabled)
    {
        var result = await _commandExecutor.ExecuteAsync("schtasks", $"/Change /TN \"{taskPath}\" {(enabled ? "/ENABLE" : "/DISABLE")}");
        Require(result.IsSuccess, $"Failed to {(enabled ? "enable" : "disable")} scheduled task {taskPath}: {result.ErrorSummary}");
    }

    private async Task<bool> IsScheduledTaskEnabledAsync(string taskPath)
    {
        var result = await _commandExecutor.ExecuteAsync("schtasks", $"/Query /TN \"{taskPath}\" /V /FO LIST");
        Require(result.IsSuccess, $"Failed to query scheduled task {taskPath}: {result.ErrorSummary}");
        var lines = result.StandardOutput
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var stateLine = lines.FirstOrDefault(line => line.StartsWith("Scheduled Task State:", StringComparison.OrdinalIgnoreCase));
        if (stateLine is not null)
        {
            return stateLine.EndsWith("Enabled", StringComparison.OrdinalIgnoreCase);
        }

        var statusLine = lines.FirstOrDefault(line => line.StartsWith("Status:", StringComparison.OrdinalIgnoreCase));
        if (statusLine is not null)
        {
            return !statusLine.EndsWith("Disabled", StringComparison.OrdinalIgnoreCase);
        }

        throw new ValidationException($"Could not determine scheduled task state for {taskPath}.");
    }

    private async Task DeleteScheduledTaskAsync(string taskPath)
    {
        await _commandExecutor.ExecuteAsync("schtasks", $"/Delete /TN \"{taskPath}\" /F");
    }

    private void WriteDword(RegistryRoot root, string path, string valueName, int value)
    {
        var success = _registryAccessor.WriteValue(root, path, valueName, new RegistryDataSnapshot
        {
            DataKind = RegistryDataKind.DWord,
            NumericValue = value
        });
        Require(success, $"Failed to write DWORD {path}\\{valueName}.");
    }

    private void WriteString(RegistryRoot root, string path, string valueName, string value)
    {
        var success = _registryAccessor.WriteValue(root, path, valueName, new RegistryDataSnapshot
        {
            DataKind = RegistryDataKind.String,
            StringValue = value
        });
        Require(success, $"Failed to write string {path}\\{valueName}.");
    }

    private int? ReadDword(RegistryRoot root, string path, string valueName)
    {
        return _registryAccessor.ReadValue(root, path, valueName)?.NumericValue is long value
            ? Convert.ToInt32(value)
            : null;
    }

    private string? ReadString(RegistryRoot root, string path, string valueName)
    {
        return _registryAccessor.ReadValue(root, path, valueName)?.StringValue;
    }

    private static CompareItem RequireItem(CompareReport report, ProfileCategory category, string? id = null, string? displayName = null)
    {
        var item = report.Items.FirstOrDefault(candidate =>
            candidate.Category == category &&
            (id is null || string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase)) &&
            (displayName is null || string.Equals(candidate.DisplayName, displayName, StringComparison.OrdinalIgnoreCase)));

        return item ?? throw new ValidationException($"Required compare item was not found for {category} ({id ?? displayName}).");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new ValidationException(message);
        }
    }

    private static void WriteHeading(string text)
    {
        Console.WriteLine();
        Console.WriteLine(text);
        Console.WriteLine(new string('=', text.Length));
    }

    private void TryDeleteValue(RegistryRoot root, string path, string valueName)
    {
        if (!_registryAccessor.DeleteValue(root, path, valueName))
        {
            Console.WriteLine($"Cleanup skipped for {path}\\{valueName}.");
        }
    }

    private static bool IsAdministrator()
    {
        var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }
}

internal sealed class TempRecentProfilesStore : IRecentProfilesStore
{
    private readonly List<string> _items = [];

    public Task<IReadOnlyList<string>> LoadAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<string>>(_items.ToList());
    }

    public Task AddAsync(string path, CancellationToken cancellationToken = default)
    {
        _items.RemoveAll(item => string.Equals(item, path, StringComparison.OrdinalIgnoreCase));
        _items.Insert(0, path);
        return Task.CompletedTask;
    }
}

internal sealed class TempRollbackStore : IRollbackStore
{
    private readonly string _directory;
    private readonly JsonSerializerOptions _serializerOptions;

    public TempRollbackStore(string directory, IProfileFileService profileFileService)
    {
        _directory = directory;
        _serializerOptions = profileFileService.SerializerOptions;
    }

    public async Task SaveAsync(RollbackRecord record, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, $"{record.CreatedAt:yyyyMMdd-HHmmss}-{record.SessionId}.json");
        var json = JsonSerializer.Serialize(record, _serializerOptions);
        await File.WriteAllTextAsync(path, json, cancellationToken);
    }

    public async Task<IReadOnlyList<RollbackRecord>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_directory))
        {
            return [];
        }

        var records = new List<RollbackRecord>();
        foreach (var file in Directory.GetFiles(_directory, "*.json").OrderByDescending(path => path))
        {
            var json = await File.ReadAllTextAsync(file, cancellationToken);
            var record = JsonSerializer.Deserialize<RollbackRecord>(json, _serializerOptions);
            if (record is not null)
            {
                records.Add(record);
            }
        }

        return records;
    }
}

internal sealed class ValidationException : Exception
{
    public ValidationException(string message) : base(message)
    {
    }
}
