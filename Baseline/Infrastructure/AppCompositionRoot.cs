using BaseLine.Services;
using BaseLine.ViewModels;

namespace BaseLine.Infrastructure;

public sealed class AppCompositionRoot : IDisposable
{
    private readonly Dictionary<Type, object> _services = [];

    private AppCompositionRoot()
    {
    }

    public static AppCompositionRoot Create()
    {
        var root = new AppCompositionRoot();
        root.RegisterServices();
        return root;
    }

    public T Resolve<T>() where T : notnull => (T)_services[typeof(T)];

    public void Dispose()
    {
        foreach (var disposable in _services.Values.OfType<IDisposable>())
        {
            disposable.Dispose();
        }
    }

    private void RegisterServices()
    {
        var paths = new AppPaths();
        paths.EnsureCreated();

        Register(paths);
        Register<IRegistryAccessor>(new RegistryAccessor());
        Register<ISystemCommandExecutor>(new SystemCommandExecutor());
        Register<IProfileFileService>(new ProfileFileService());
        Register<IRollbackStore>(new RollbackStore(paths, Resolve<IProfileFileService>()));
        Register<IRecentProfilesStore>(new RecentProfilesStore(paths));
        Register<IFileDialogService>(new FileDialogService());
        Register<IMessageDialogService>(new MessageDialogService());
        Register(new MachineInfoService(Resolve<IRegistryAccessor>()));
        Register(new RegistryTemplateCatalog());
        Register(new NetworkDiscoveryService(Resolve<IRegistryAccessor>()));

        var handlers = new IProfileCategoryHandler[]
        {
            new ServicesCategoryHandler(Resolve<IRegistryAccessor>()),
            new BootBehaviorCategoryHandler(Resolve<ISystemCommandExecutor>()),
            new RegistryTweaksCategoryHandler(Resolve<IRegistryAccessor>()),
            new PoliciesCategoryHandler(Resolve<IRegistryAccessor>()),
            new NetworkCategoryHandler(Resolve<IRegistryAccessor>(), Resolve<NetworkDiscoveryService>()),
            new StartupEnvironmentCategoryHandler(Resolve<IRegistryAccessor>()),
            new ScheduledTasksCategoryHandler(Resolve<ISystemCommandExecutor>()),
            new PowerConfigurationCategoryHandler(Resolve<IRegistryAccessor>(), Resolve<ISystemCommandExecutor>())
        };

        Register<IEnumerable<IProfileCategoryHandler>>(handlers);
        Register(new WorkspaceState());
        Register(new BaselineWorkflowService(
            handlers,
            Resolve<MachineInfoService>(),
            Resolve<IProfileFileService>(),
            Resolve<IRollbackStore>(),
            Resolve<IRecentProfilesStore>()));

        Register(new CapturePageViewModel(
            Resolve<BaselineWorkflowService>(),
            Resolve<WorkspaceState>(),
            Resolve<IFileDialogService>(),
            Resolve<IMessageDialogService>(),
            Resolve<RegistryTemplateCatalog>(),
            Resolve<NetworkDiscoveryService>()));
        Register(new ProfilesPageViewModel(
            Resolve<BaselineWorkflowService>(),
            Resolve<WorkspaceState>(),
            Resolve<IFileDialogService>(),
            Resolve<IMessageDialogService>()));
        Register(new ComparePageViewModel(
            Resolve<BaselineWorkflowService>(),
            Resolve<WorkspaceState>(),
            Resolve<IMessageDialogService>()));
        Register(new ApplyPageViewModel(
            Resolve<BaselineWorkflowService>(),
            Resolve<WorkspaceState>(),
            Resolve<IMessageDialogService>()));
        Register(new RollbackPageViewModel(
            Resolve<BaselineWorkflowService>(),
            Resolve<WorkspaceState>(),
            Resolve<IMessageDialogService>()));
        Register(new ShellViewModel(
            Resolve<WorkspaceState>(),
            Resolve<CapturePageViewModel>(),
            Resolve<ProfilesPageViewModel>(),
            Resolve<ComparePageViewModel>(),
            Resolve<ApplyPageViewModel>(),
            Resolve<RollbackPageViewModel>()));
    }

    private void Register<T>(T instance) where T : notnull
    {
        _services[typeof(T)] = instance;
    }
}
