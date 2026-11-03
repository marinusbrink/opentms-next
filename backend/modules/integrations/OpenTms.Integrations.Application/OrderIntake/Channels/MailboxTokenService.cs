using System.Threading;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Security.Encryption;

namespace OpenTms.Integrations.OrderIntake.Channels;

/// <summary>
/// The refresh token of a mailbox connection (design 72 "Privacy note"): encrypted at rest with
/// <see cref="IStringEncryptionService"/>, decrypted only here to get an access token, never returned or logged. A token the
/// provider rotated (Microsoft does on every refresh) is put back on the connection; the caller saves it.
/// </summary>
public class MailboxTokenService : ITransientDependency
{
    private readonly IStringEncryptionService _encryption;
    private readonly MailProviderClientResolver _clients;

    public MailboxTokenService(IStringEncryptionService encryption, MailProviderClientResolver clients)
    {
        _encryption = encryption;
        _clients = clients;
    }

    public string Encrypt(string refreshToken)
        => _encryption.Encrypt(refreshToken) ?? throw new AbpException("The refresh token could not be encrypted.");

    /// <summary>
    /// A fresh access token for the connection. <see cref="MailboxReconnectRequiredException"/> when the provider refused
    /// the refresh token; a rotated refresh token is stored on <paramref name="connection"/> (not saved).
    /// </summary>
    public async Task<string> GetAccessTokenAsync(MailboxConnection connection, CancellationToken cancellationToken = default)
    {
        if (connection.RefreshTokenEncrypted == null)
        {
            throw new BusinessException(IntegrationsErrorCodes.MailboxStateInvalid).WithData("status", connection.Status);
        }

        var refreshToken = _encryption.Decrypt(connection.RefreshTokenEncrypted);
        if (string.IsNullOrEmpty(refreshToken))
        {
            /* Encrypted with another pass phrase: only a new consent helps. */
            throw new MailboxReconnectRequiredException(connection.Provider);
        }

        var tokens = await _clients.Get(connection.Provider).RefreshAsync(refreshToken, cancellationToken);
        if (tokens.RefreshToken != null && tokens.RefreshToken != refreshToken)
        {
            connection.ReplaceRefreshToken(Encrypt(tokens.RefreshToken));
        }

        return tokens.AccessToken;
    }
}
