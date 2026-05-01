using BaseLine.Core;
using BaseLine.Infrastructure;

namespace BaseLine.Services;

public sealed class PoliciesCategoryHandler : RegistryProfileBaseHandler
{
    public PoliciesCategoryHandler(IRegistryAccessor registryAccessor) : base(registryAccessor)
    {
    }

    public override ProfileCategory Category => ProfileCategory.Policies;

    protected override IEnumerable<StructuredRegistryTemplate> GetSelectedTemplates(CaptureOptions options) => options.SelectedPolicyTemplates;

    protected override IReadOnlyList<RegistryProfileEntry> GetPayloadItems(BaselineProfile profile) => profile.Categories.Policies?.Items ?? [];
}
