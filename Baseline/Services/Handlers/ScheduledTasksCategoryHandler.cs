using BaseLine.Core;
using BaseLine.Infrastructure;
using System.Text;

namespace BaseLine.Services;

public sealed class ScheduledTasksCategoryHandler : IProfileCategoryHandler
{
    private readonly ISystemCommandExecutor _commandExecutor;

    public ScheduledTasksCategoryHandler(ISystemCommandExecutor commandExecutor)
    {
        _commandExecutor = commandExecutor;
    }

    public ProfileCategory Category => ProfileCategory.ScheduledTasks;

    public async Task<CategoryCaptureResult> CaptureAsync(CaptureOptions options, CancellationToken cancellationToken = default)
    {
        var payload = new ScheduledTasksPayload { Items = await ReadTasksAsync(cancellationToken) };
        return new CategoryCaptureResult
        {
            Category = Category,
            Payload = payload,
            Messages = [$"Captured {payload.Items.Count} scheduled tasks."]
        };
    }

    public async Task<IReadOnlyList<CompareItem>> CompareAsync(BaselineProfile profile, CancellationToken cancellationToken = default)
    {
        var payload = profile.Categories.ScheduledTasks;
        if (payload is null)
        {
            return [];
        }

        var current = (await ReadTasksAsync(cancellationToken))
            .GroupBy(item => item.TaskPath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        return payload.Items
            .GroupBy(task => task.TaskPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(task =>
        {
            current.TryGetValue(task.TaskPath, out var currentTask);
            var currentState = currentTask is null ? "Missing task" : currentTask.Enabled ? "Enabled" : "Disabled";
            var profileState = task.Enabled ? "Enabled" : "Disabled";
            var status = currentTask is null
                ? BaselineStatus.MissingDependency
                : currentTask.Enabled == task.Enabled ? BaselineStatus.AlreadyMatches : BaselineStatus.Ready;

            return new CompareItem
            {
                Id = $"{Category}:{task.TaskPath}",
                Category = Category,
                GroupName = "Scheduled Tasks",
                DisplayName = task.TaskName,
                ProfileValue = profileState,
                CurrentValue = currentState,
                RecommendedAction = status == BaselineStatus.AlreadyMatches ? "No action" : "Enable or disable task",
                SafetyLevel = task.SafetyLevel,
                Status = status,
                Notes = $"{task.TriggerSummary} | {task.ActionPath}"
            };
        }).ToList();
    }

    public async Task<ApplyCategoryResult> ApplyAsync(BaselineProfile profile, IReadOnlyList<CompareItem> items, CancellationToken cancellationToken = default)
    {
        var payload = profile.Categories.ScheduledTasks;
        if (payload is null)
        {
            return new ApplyCategoryResult();
        }

        var currentTasks = (await ReadTasksAsync(cancellationToken))
            .GroupBy(item => item.TaskPath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var results = new List<ApplyResultItem>();
        var rollbackItems = new List<RollbackItem>();

        foreach (var item in items)
        {
            var taskPath = item.Id[(item.Id.IndexOf(':') + 1)..];
            var profileTask = payload.Items.FirstOrDefault(entry => entry.TaskPath == taskPath);
            if (profileTask is null)
            {
                continue;
            }

            if (!currentTasks.TryGetValue(taskPath, out var currentTask))
            {
                results.Add(new ApplyResultItem { ItemId = item.Id, Category = Category, DisplayName = item.DisplayName, Status = BaselineStatus.Skipped, Message = "Task missing on target machine." });
                continue;
            }

            rollbackItems.Add(new RollbackItem
            {
                ItemId = item.Id,
                Category = Category,
                DisplayName = item.DisplayName,
                Kind = RollbackKind.ScheduledTask,
                ExistedBefore = true,
                PreviousBooleanValue = currentTask.Enabled,
                Metadata = new Dictionary<string, string> { ["taskPath"] = taskPath }
            });

            var result = await _commandExecutor.ExecuteAsync("schtasks", $"/Change /TN \"{taskPath}\" {(profileTask.Enabled ? "/ENABLE" : "/DISABLE")}", cancellationToken);
            results.Add(new ApplyResultItem { ItemId = item.Id, Category = Category, DisplayName = item.DisplayName, Status = result.IsSuccess ? BaselineStatus.Applied : BaselineStatus.Failed, Message = result.IsSuccess ? "Scheduled task updated." : result.StandardError.Trim() });
        }

        return new ApplyCategoryResult { ResultItems = results, RollbackItems = rollbackItems };
    }

    public async Task<IReadOnlyList<ApplyResultItem>> RollbackAsync(IReadOnlyList<RollbackItem> items, CancellationToken cancellationToken = default)
    {
        var results = new List<ApplyResultItem>();
        foreach (var item in items)
        {
            if (!item.Metadata.TryGetValue("taskPath", out var taskPath))
            {
                continue;
            }

            var result = await _commandExecutor.ExecuteAsync("schtasks", $"/Change /TN \"{taskPath}\" {(item.PreviousBooleanValue == true ? "/ENABLE" : "/DISABLE")}", cancellationToken);
            results.Add(new ApplyResultItem { ItemId = item.ItemId, Category = Category, DisplayName = item.DisplayName, Status = result.IsSuccess ? BaselineStatus.RolledBack : BaselineStatus.Failed, Message = result.IsSuccess ? "Scheduled task restored." : result.StandardError.Trim() });
        }

        return results;
    }

    private async Task<List<ScheduledTaskProfile>> ReadTasksAsync(CancellationToken cancellationToken)
    {
        var result = await _commandExecutor.ExecuteAsync("schtasks", "/Query /FO CSV /V", cancellationToken);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"schtasks failed: {result.ErrorSummary}");
        }

        var lines = result.StandardOutput.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length <= 1)
        {
            return [];
        }

        var headers = ParseCsvLine(lines[0]);
        var nameIndex = headers.FindIndex(header => header.Equals("TaskName", StringComparison.OrdinalIgnoreCase));
        var statusIndex = headers.FindIndex(header => header.Equals("Status", StringComparison.OrdinalIgnoreCase));
        var scheduleIndex = headers.FindIndex(header => header.Equals("Schedule Type", StringComparison.OrdinalIgnoreCase));
        var commandIndex = headers.FindIndex(header => header.Equals("Task To Run", StringComparison.OrdinalIgnoreCase));

        var tasks = new List<ScheduledTaskProfile>();
        foreach (var line in lines.Skip(1))
        {
            var columns = ParseCsvLine(line);
            if (columns.Count <= Math.Max(Math.Max(nameIndex, statusIndex), Math.Max(scheduleIndex, commandIndex)))
            {
                continue;
            }

            var taskPath = columns[nameIndex];
            if (string.IsNullOrWhiteSpace(taskPath) || taskPath.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var trigger = columns[scheduleIndex];
            tasks.Add(new ScheduledTaskProfile
            {
                TaskPath = taskPath,
                TaskName = taskPath.Split('\\', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? taskPath,
                Enabled = !columns[statusIndex].Contains("Disabled", StringComparison.OrdinalIgnoreCase),
                ActionPath = columns[commandIndex],
                TriggerSummary = trigger,
                SafetyLevel = trigger.Contains("Logon", StringComparison.OrdinalIgnoreCase) || trigger.Contains("Startup", StringComparison.OrdinalIgnoreCase)
                    ? SafetyLevel.Moderate
                    : SafetyLevel.Safe
            });
        }

        return tasks
            .GroupBy(task => task.TaskPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static List<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        var insideQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (insideQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    insideQuotes = !insideQuotes;
                }
            }
            else if (ch == ',' && !insideQuotes)
            {
                values.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        values.Add(current.ToString());
        return values;
    }
}
