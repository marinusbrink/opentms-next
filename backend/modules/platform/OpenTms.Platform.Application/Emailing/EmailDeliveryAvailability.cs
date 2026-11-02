using System.Threading.Tasks;
using OpenTms.Platform.Features;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Features;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Platform.Emailing;

/// <summary>
/// Whether mail sent now actually goes out (design 64): the mail service is configured and, for a tenant,
/// <see cref="PlatformFeatures.EmailDelivery"/> is on; host mail whenever the service is configured (the host has no
/// feature values). A caller whose mail is the only way in (an invitation, a reset link) asks first, because
/// <see cref="SendLogEmailSender"/> otherwise logs the mail and drops it.
/// </summary>
public class EmailDeliveryAvailability : ITransientDependency
{
    private readonly IEmailDeliveryProvider _provider;
    private readonly IFeatureChecker _featureChecker;
    private readonly ICurrentTenant _currentTenant;

    public EmailDeliveryAvailability(IEmailDeliveryProvider provider, IFeatureChecker featureChecker, ICurrentTenant currentTenant)
    {
        _provider = provider;
        _featureChecker = featureChecker;
        _currentTenant = currentTenant;
    }

    public async Task<bool> IsAvailableAsync()
    {
        if (!(await _provider.GetStatusAsync()).IsConfigured)
        {
            return false;
        }

        return _currentTenant.Id == null || await _featureChecker.IsEnabledAsync(PlatformFeatures.EmailDelivery);
    }
}
