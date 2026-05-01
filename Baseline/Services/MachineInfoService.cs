using System.Reflection;
using BaseLine.Core;
using BaseLine.Infrastructure;

namespace BaseLine.Services;

public sealed class MachineInfoService
{
    private readonly IRegistryAccessor _registryAccessor;

    public MachineInfoService(IRegistryAccessor registryAccessor)
    {
        _registryAccessor = registryAccessor;
    }

    public ProfileMetadata CreateProfileMetadata(string profileName, IEnumerable<ProfileCategory> categories)
    {
        return new ProfileMetadata
        {
            ProfileName = profileName,
            CreatedAt = DateTimeOffset.Now,
            SourceMachineName = Environment.MachineName,
            WindowsVersion = GetWindowsVersion(),
            AppVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0",
            SelectedCategories = categories.ToList()
        };
    }

    private string GetWindowsVersion()
    {
        var path = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        var productName = _registryAccessor.ReadValue(RegistryRoot.LocalMachine, path, "ProductName")?.StringValue ?? "Windows";
        var displayVersion = _registryAccessor.ReadValue(RegistryRoot.LocalMachine, path, "DisplayVersion")?.StringValue ?? string.Empty;
        var build = _registryAccessor.ReadValue(RegistryRoot.LocalMachine, path, "CurrentBuild")?.StringValue ?? string.Empty;
        return $"{productName} {displayVersion} (build {build})".Trim();
    }
}
