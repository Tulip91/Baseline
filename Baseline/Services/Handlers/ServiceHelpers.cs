using Microsoft.Win32;
using BaseLine.Core;

namespace BaseLine.Services.Handlers;

internal static class ServiceHelpers
{
    public static SafetyLevel Classify(string serviceName, string? imagePath)
    {
        var critical = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "RpcSs", "TrustedInstaller", "WinDefend", "EventLog", "BFE", "LanmanWorkstation", "W32Time"
        };

        if (critical.Contains(serviceName) || (imagePath?.Contains(@"\Windows\System32\", StringComparison.OrdinalIgnoreCase) ?? false))
        {
            return SafetyLevel.Advanced;
        }

        return imagePath?.Contains(@"\Program Files", StringComparison.OrdinalIgnoreCase) ?? false
            ? SafetyLevel.Moderate
            : SafetyLevel.Safe;
    }

    public static string NormalizeStartType(long? startValue)
    {
        return startValue switch
        {
            2 => "Automatic",
            3 => "Manual",
            4 => "Disabled",
            0 => "Boot",
            1 => "System",
            _ => "Unknown"
        };
    }

    public static RegistryKey OpenServicesRoot(bool writable) =>
        RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default)
            .OpenSubKey(@"SYSTEM\CurrentControlSet\Services", writable)!;
}
