using System.Threading.Tasks;

namespace OpenTms.Integrations.AiConnector;

/// <summary>Whether the current user may call a tool in the current tenant: its permissions and features. In-process only.</summary>
public interface IAiConnectorToolAccess
{
    Task<bool> IsAllowedAsync(AiConnectorToolDefinition tool);
}
