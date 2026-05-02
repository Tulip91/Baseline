using BaseLine.Core;

namespace BaseLine.Services;

public sealed class RegistryTemplateCatalog
{
    public IReadOnlyList<StructuredRegistryTemplate> RegistryDefaults { get; } =
    [
        new() { Id = "graphics.hardware-gpu", GroupName = "Graphics", DisplayName = "GPU scheduling", Root = RegistryRoot.LocalMachine, Path = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", ValueName = "HwSchMode", SafetyLevel = SafetyLevel.Moderate, CompatibilityNote = "Requires supported GPU drivers.", IsDefaultSelected = true },
        new() { Id = "scheduler.system-responsiveness", GroupName = "Scheduler", DisplayName = "System responsiveness", Root = RegistryRoot.LocalMachine, Path = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", ValueName = "SystemResponsiveness", SafetyLevel = SafetyLevel.Moderate, IsDefaultSelected = true },
        new() { Id = "network.tcp-no-delay", GroupName = "Network", DisplayName = "TCP no delay", Root = RegistryRoot.LocalMachine, Path = @"SOFTWARE\Microsoft\MSMQ\Parameters", ValueName = "TCPNoDelay", SafetyLevel = SafetyLevel.Advanced, CompatibilityNote = "Only meaningful when MSMQ is present." },
        new() { Id = "explorer.separate-process", GroupName = "Explorer/UI", DisplayName = "Separate folder windows", Root = RegistryRoot.CurrentUser, Path = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", ValueName = "SeparateProcess", SafetyLevel = SafetyLevel.Safe, IsDefaultSelected = true },
        new() { Id = "privacy.tailored-experiences", GroupName = "Privacy", DisplayName = "Tailored experiences", Root = RegistryRoot.CurrentUser, Path = @"Software\Microsoft\Windows\CurrentVersion\Privacy", ValueName = "TailoredExperiencesWithDiagnosticDataEnabled", SafetyLevel = SafetyLevel.Safe, IsDefaultSelected = true },
        new() { Id = "responsiveness.menu-delay", GroupName = "System responsiveness", DisplayName = "Menu show delay", Root = RegistryRoot.CurrentUser, Path = @"Control Panel\Desktop", ValueName = "MenuShowDelay", SafetyLevel = SafetyLevel.Safe, IsDefaultSelected = true }
    ];

    public IReadOnlyList<StructuredRegistryTemplate> PolicyDefaults { get; } =
    [
        new() { Id = "policy.target-release", GroupName = "Updates", DisplayName = "Target release version", Root = RegistryRoot.LocalMachine, Path = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", ValueName = "TargetReleaseVersion", SafetyLevel = SafetyLevel.Moderate, IsDefaultSelected = true },
        new() { Id = "policy.disable-soft-landing", GroupName = "Privacy", DisplayName = "Disable soft landing", Root = RegistryRoot.LocalMachine, Path = @"SOFTWARE\Policies\Microsoft\Windows\CloudContent", ValueName = "DisableSoftLanding", SafetyLevel = SafetyLevel.Safe, IsDefaultSelected = true },
        new() { Id = "policy.hide-recommended", GroupName = "Explorer/UI", DisplayName = "Hide recommended section", Root = RegistryRoot.CurrentUser, Path = @"SOFTWARE\Policies\Microsoft\Windows\Explorer", ValueName = "HideRecommendedSection", SafetyLevel = SafetyLevel.Safe, IsDefaultSelected = true },
        new() { Id = "policy.allow-telemetry", GroupName = "Privacy", DisplayName = "Telemetry policy", Root = RegistryRoot.LocalMachine, Path = @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", ValueName = "AllowTelemetry", SafetyLevel = SafetyLevel.Advanced }
    ];
}
