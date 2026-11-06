using System.Threading.Tasks;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Features;
using Volo.Abp.Uow;

namespace OpenTms.Integrations.AiConnector;

/// <inheritdoc cref="IAiConnectorToolAccess"/>
public class AiConnectorToolAccess : IAiConnectorToolAccess, ITransientDependency
{
    private readonly IFeatureChecker _features;
    private readonly IPermissionChecker _permissions;

    public AiConnectorToolAccess(IFeatureChecker features, IPermissionChecker permissions)
    {
        _features = features;
        _permissions = permissions;
    }

    [UnitOfWork(isTransactional: false)]
    public virtual async Task<bool> IsAllowedAsync(AiConnectorToolDefinition tool)
    {
        foreach (var requirement in tool.FeatureRequirements)
        {
            var anyOn = false;
            foreach (var feature in requirement)
            {
                if (await _features.IsEnabledAsync(feature))
                {
                    anyOn = true;
                    break;
                }
            }

            if (!anyOn)
            {
                return false;
            }
        }

        foreach (var permission in tool.Permissions)
        {
            if (!await _permissions.IsGrantedAsync(permission))
            {
                return false;
            }
        }

        return true;
    }
}
