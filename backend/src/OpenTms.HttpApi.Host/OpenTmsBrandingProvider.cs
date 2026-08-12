using Microsoft.Extensions.Localization;
using OpenTms.Localization;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Ui.Branding;

namespace OpenTms;

[Dependency(ReplaceServices = true)]
public class OpenTmsBrandingProvider : DefaultBrandingProvider
{
    private IStringLocalizer<OpenTmsResource> _localizer;

    public OpenTmsBrandingProvider(IStringLocalizer<OpenTmsResource> localizer)
    {
        _localizer = localizer;
    }

    public override string AppName => _localizer["AppName"];
}
