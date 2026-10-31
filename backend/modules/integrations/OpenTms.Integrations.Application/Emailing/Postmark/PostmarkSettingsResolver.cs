using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using OpenTms.Platform.Emailing;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Settings;

namespace OpenTms.Integrations.Emailing.Postmark;

/// <summary>
/// What Postmark is called with (design 64, "Host e-mail settings"): the host's e-mail settings, global values in the host
/// database, override the deployment's configuration (<c>Integrations:Postmark</c>) field by field; an empty setting keeps
/// the configuration's value. Read in the host's context whatever tenant is current, because the settings live in the host
/// database and apply to every tenant. Resolved per call, so a saved change applies at once (ABP's setting cache is
/// invalidated on save; with several instances, after its expiry).
/// </summary>
public class PostmarkSettingsResolver : ITransientDependency
{
    private readonly ISettingProvider _settings;
    private readonly ICurrentTenant _currentTenant;
    private readonly PostmarkOptions _configuration;

    public PostmarkSettingsResolver(ISettingProvider settings, ICurrentTenant currentTenant, IOptions<PostmarkOptions> configuration)
    {
        _settings = settings;
        _currentTenant = currentTenant;
        _configuration = configuration.Value;
    }

    public async Task<ResolvedPostmarkSettings> ResolveAsync()
    {
        using (_currentTenant.Change(null))
        {
            var token = await GetAsync(EmailSettingNames.ServerToken);
            var trackOpens = await GetAsync(EmailSettingNames.TrackOpens);
            var options = new PostmarkOptions
            {
                BaseUrl = _configuration.BaseUrl,
                MessageStream = _configuration.MessageStream,
                ServerToken = token ?? _configuration.ServerToken,
                FromAddress = await GetAsync(EmailSettingNames.FromAddress) ?? _configuration.FromAddress,
                FromName = await GetAsync(EmailSettingNames.FromName) ?? _configuration.FromName,
                TrackOpens = bool.TryParse(trackOpens, out var opens) ? opens : _configuration.TrackOpens,
                TrackLinks = await GetAsync(EmailSettingNames.TrackLinks) ?? _configuration.TrackLinks,
                WebhookUsername = await GetAsync(EmailSettingNames.WebhookUsername) ?? _configuration.WebhookUsername,
                WebhookPassword = await GetAsync(EmailSettingNames.WebhookPassword) ?? _configuration.WebhookPassword
            };
            var source = token != null
                ? EmailSettingSource.Settings
                : string.IsNullOrWhiteSpace(_configuration.ServerToken) ? EmailSettingSource.None : EmailSettingSource.Configuration;
            return new ResolvedPostmarkSettings(options, source);
        }
    }

    private async Task<string?> GetAsync(string name)
    {
        var value = await _settings.GetOrNullAsync(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}

/// <summary>The combined settings and where the token came from.</summary>
public sealed record ResolvedPostmarkSettings(PostmarkOptions Options, EmailSettingSource ServerTokenSource)
{
    public EmailDeliveryStatus ToStatus() => new()
    {
        IsConfigured = Options.IsConfigured,
        ServerTokenSource = ServerTokenSource,
        FromAddress = string.IsNullOrWhiteSpace(Options.FromAddress) ? null : Options.FromAddress,
        FromName = Options.FromName,
        TrackOpens = Options.TrackOpens,
        TrackLinks = Options.TrackLinks,
        IsWebhookConfigured = Options.IsWebhookConfigured
    };
}
