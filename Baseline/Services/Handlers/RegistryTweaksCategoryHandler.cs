using BaseLine.Core;
using BaseLine.Infrastructure;

namespace BaseLine.Services;

public sealed class RegistryTweaksCategoryHandler : RegistryProfileBaseHandler
{
    public RegistryTweaksCategoryHandler(IRegistryAccessor registryAccessor) : base(registryAccessor)
    {
    }

    public override ProfileCategory Category => ProfileCategory.RegistryTweaks;

    protected override IEnumerable<StructuredRegistryTemplate> GetSelectedTemplates(CaptureOptions options) => options.SelectedRegistryTemplates;

    protected override IReadOnlyList<RegistryProfileEntry> GetPayloadItems(BaselineProfile profile) => profile.Categories.RegistryTweaks?.Items ?? [];
}
