using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using BaseLine.Core;

namespace BaseLine.Infrastructure;

public sealed class AppPaths
{
    public string BaseDirectory { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Baseline");
    public string ProfilesDirectory => Path.Combine(BaseDirectory, "Profiles");
    public string RollbackDirectory => Path.Combine(BaseDirectory, "Rollback");
    public string StateDirectory => Path.Combine(BaseDirectory, "State");
    public string RecentProfilesPath => Path.Combine(StateDirectory, "recent-profiles.json");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(BaseDirectory);
        Directory.CreateDirectory(ProfilesDirectory);
        Directory.CreateDirectory(RollbackDirectory);
        Directory.CreateDirectory(StateDirectory);
    }
}

public interface IProfileFileService
{
    Task SaveAsync(BaselineProfile profile, string path, CancellationToken cancellationToken = default);
    Task<BaselineProfile> LoadAsync(string path, CancellationToken cancellationToken = default);
    JsonSerializerOptions SerializerOptions { get; }
}

public sealed class ProfileFileService : IProfileFileService
{
    public JsonSerializerOptions SerializerOptions { get; } = CreateOptions();

    public async Task SaveAsync(BaselineProfile profile, string path, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(profile, SerializerOptions);
        await File.WriteAllTextAsync(path, json, cancellationToken);
    }

    public async Task<BaselineProfile> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException("Profile file was not found.");
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            var profile = JsonSerializer.Deserialize<BaselineProfile>(json, SerializerOptions);
            return ValidateProfile(profile);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Profile file is not valid JSON: {ex.Message}", ex);
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static BaselineProfile ValidateProfile(BaselineProfile? profile)
    {
        if (profile is null)
        {
            throw new InvalidOperationException("Profile file could not be deserialized.");
        }

        profile.SchemaVersion = string.IsNullOrWhiteSpace(profile.SchemaVersion) ? "1.0" : profile.SchemaVersion.Trim();
        if (!profile.SchemaVersion.StartsWith("1.", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unsupported profile schema version '{profile.SchemaVersion}'.");
        }

        profile.Metadata ??= new ProfileMetadata();
        profile.Categories ??= new CategoryPayloads();

        if (string.IsNullOrWhiteSpace(profile.Metadata.ProfileName))
        {
            throw new InvalidOperationException("Profile file is missing a profile name.");
        }

        if (profile.Metadata.CreatedAt == default)
        {
            profile.Metadata.CreatedAt = DateTimeOffset.Now;
        }

        profile.Metadata.SelectedCategories = profile.Metadata.SelectedCategories
            .Distinct()
            .ToList();

        if (profile.Metadata.SelectedCategories.Count == 0)
        {
            profile.Metadata.SelectedCategories = profile.Categories.IncludedCategories.ToList();
        }

        if (profile.Metadata.SelectedCategories.Count == 0)
        {
            throw new InvalidOperationException("Profile file does not contain any captured categories.");
        }

        profile.Metadata.SourceMachineName = string.IsNullOrWhiteSpace(profile.Metadata.SourceMachineName)
            ? "Unknown machine"
            : profile.Metadata.SourceMachineName.Trim();

        profile.Metadata.WindowsVersion = string.IsNullOrWhiteSpace(profile.Metadata.WindowsVersion)
            ? "Unknown Windows version"
            : profile.Metadata.WindowsVersion.Trim();

        profile.Metadata.AppVersion = string.IsNullOrWhiteSpace(profile.Metadata.AppVersion)
            ? "Unknown"
            : profile.Metadata.AppVersion.Trim();

        return profile;
    }
}

public interface IRollbackStore
{
    Task SaveAsync(RollbackRecord record, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RollbackRecord>> LoadAllAsync(CancellationToken cancellationToken = default);
}

public sealed class RollbackStore : IRollbackStore
{
    private readonly AppPaths _paths;
    private readonly IProfileFileService _profileFileService;

    public RollbackStore(AppPaths paths, IProfileFileService profileFileService)
    {
        _paths = paths;
        _profileFileService = profileFileService;
    }

    public async Task SaveAsync(RollbackRecord record, CancellationToken cancellationToken = default)
    {
        _paths.EnsureCreated();
        var fileName = $"{record.CreatedAt:yyyyMMdd-HHmmss}-{record.SessionId}.json";
        var path = Path.Combine(_paths.RollbackDirectory, fileName);
        var json = JsonSerializer.Serialize(record, _profileFileService.SerializerOptions);
        await File.WriteAllTextAsync(path, json, cancellationToken);
    }

    public async Task<IReadOnlyList<RollbackRecord>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        _paths.EnsureCreated();
        var records = new List<RollbackRecord>();
        foreach (var file in Directory.GetFiles(_paths.RollbackDirectory, "*.json").OrderByDescending(item => item))
        {
            try
            {
                var json = await File.ReadAllTextAsync(file, cancellationToken);
                var record = JsonSerializer.Deserialize<RollbackRecord>(json, _profileFileService.SerializerOptions);
                if (record is not null)
                {
                    records.Add(record);
                }
            }
            catch
            {
            }
        }

        return records;
    }
}

public interface IRecentProfilesStore
{
    Task<IReadOnlyList<string>> LoadAsync(CancellationToken cancellationToken = default);
    Task AddAsync(string path, CancellationToken cancellationToken = default);
}

public sealed class RecentProfilesStore : IRecentProfilesStore
{
    private readonly AppPaths _paths;

    public RecentProfilesStore(AppPaths paths)
    {
        _paths = paths;
    }

    public async Task<IReadOnlyList<string>> LoadAsync(CancellationToken cancellationToken = default)
    {
        _paths.EnsureCreated();
        if (!File.Exists(_paths.RecentProfilesPath))
        {
            return [];
        }

        try
        {
            var json = await File.ReadAllTextAsync(_paths.RecentProfilesPath, cancellationToken);
            var items = JsonSerializer.Deserialize<List<string>>(json) ?? [];
            var existingItems = items
                .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (existingItems.Count != items.Count)
            {
                await SaveAsync(existingItems, cancellationToken);
            }

            return existingItems;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public async Task AddAsync(string path, CancellationToken cancellationToken = default)
    {
        _paths.EnsureCreated();
        var items = (await LoadAsync(cancellationToken)).ToList();
        items.RemoveAll(item => string.Equals(item, path, StringComparison.OrdinalIgnoreCase));
        items.Insert(0, path);
        if (items.Count > 10)
        {
            items.RemoveRange(10, items.Count - 10);
        }

        await SaveAsync(items, cancellationToken);
    }

    private async Task SaveAsync(List<string> items, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_paths.RecentProfilesPath, json, cancellationToken);
    }
}
