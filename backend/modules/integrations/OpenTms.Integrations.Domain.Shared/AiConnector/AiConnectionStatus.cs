namespace OpenTms.Integrations.AiConnector;

/// <summary>
/// Whether <c>/mcp</c> would serve a connection now, computed when it is read (design 74, PO decision 6). Never stored. The values
/// are part of the contract and the order of the connections grid. A disconnect on the vendor's side cannot be seen here.
/// </summary>
public enum AiConnectionStatus
{
    /// <summary>Nothing in OpenTMS would refuse it.</summary>
    Live = 1,

    /// <summary>Not switched off, but the user is inactive or no longer holds <c>Integrations.AiConnector.Use</c>. Fixing that makes it live again.</summary>
    NotWorking = 2,

    /// <summary>Not switched off, but its refresh token has lapsed: unused for <see cref="AiConnectorConsts.RefreshTokenLifetime"/>. The user has to connect again.</summary>
    Expired = 3,

    /// <summary>Someone switched it off in OpenTMS.</summary>
    SwitchedOff = 4
}
