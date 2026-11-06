using System;
using System.Threading.Tasks;
using OpenTms.Platform.Grid;
using OpenTms.Platform.ReferenceData;
using Volo.Abp.Application.Services;

namespace OpenTms.Integrations.AiConnector;

/// <summary>The connector's address and whether the tenant accepted the data sharing (design 74 "Connector administration").</summary>
public class AiConnectorStatusDto
{
    public string ConnectorUrl { get; set; } = string.Empty;

    public bool IsAccepted { get; set; }

    public DateTime? AcceptedAt { get; set; }

    /// <summary>Null when the accepting user was deleted (art. 17 clears the id; the acceptance stands).</summary>
    public string? AcceptedByUserName { get; set; }
}

/// <summary>A row of the connections grid.</summary>
public class AiConnectionRowDto
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string? UserName { get; set; }

    public string ToolName { get; set; } = string.Empty;

    public DateTime ConnectedAt { get; set; }

    public DateTime? LastUsedAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    /// <summary>Computed at read time: whether <c>/mcp</c> would serve this connection now, as far as OpenTMS can tell.</summary>
    public AiConnectionStatus Status { get; set; }
}

/// <summary>The AI connector's status and data sharing, route root <c>/api/integrations/ai-connector</c>.</summary>
public interface IAiConnectorAppService : IApplicationService
{
    Task<AiConnectorStatusDto> GetStatusAsync();

    Task<AiConnectorStatusDto> AcceptAsync();

    /// <summary>Removes the acceptance and revokes every connection of the tenant.</summary>
    Task<AiConnectorStatusDto> WithdrawAsync();

}

/// <summary>The connections of the AI connector, route <c>/api/integrations/ai-connector/connections</c>.</summary>
public interface IAiConnectionAppService : IApplicationService
{
    /// <summary>The caller's own connections, or every connection with <c>Integrations.AiConnector.Manage</c>; live rows first within a page.</summary>
    Task<GridResponse<AiConnectionRowDto>> GetListAsync(ReferenceDataListRequestDto input);

    /// <summary>Idempotent: revoking a revoked connection answers 204 and retries the host side.</summary>
    Task RevokeAsync(Guid id);
}
