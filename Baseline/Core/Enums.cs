namespace BaseLine.Core;

public enum ProfileCategory
{
    Services,
    BootBehavior,
    RegistryTweaks,
    Policies,
    Network,
    StartupEnvironment,
    ScheduledTasks,
    PowerConfiguration
}

public enum SafetyLevel
{
    Safe,
    Moderate,
    Advanced
}

public enum BaselineStatus
{
    Ready,
    AlreadyMatches,
    Unsupported,
    MissingDependency,
    Warning,
    Failed,
    Applied,
    Skipped,
    RolledBack
}

public enum RegistryRoot
{
    LocalMachine,
    CurrentUser
}

public enum RegistryDataKind
{
    String,
    ExpandString,
    MultiString,
    DWord,
    QWord,
    Binary,
    None
}

public enum RollbackKind
{
    RegistryValue,
    ServiceConfiguration,
    BootSetting,
    NetworkAdapter,
    StartupItem,
    ScheduledTask,
    PowerSetting
}
