using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Anthropic;
using Anthropic.Exceptions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using OpenTms.Integrations.OrderIntake;
using OpenTms.Integrations.OrderIntake.Ai;
using OpenTms.Integrations.OrderIntake.Channels;
using OpenTms.Platform.Maintenance;

namespace OpenTms.Integrations.ServiceSettings;

/// <summary>
/// The test of the AI section of Administration → Services → Integrations (design 72, "Host settings"): retrieves the
/// learning model through the official Anthropic SDK (<c>GET /v1/models/{model}</c>, free of tokens) with the effective
/// key. Not configured is Degraded; a refused key or an unknown model is Unhealthy (Failing). Runs on demand from the
/// page's Test only.
/// </summary>
public class AnthropicHealthCheck : IHealthCheck
{
    public const string Name = "anthropic";

    /// <summary>The named HttpClient the SDK uses (design 72's AI adapter may share it).</summary>
    public const string HttpClientName = "OrderIntake.Anthropic";

    private static readonly string[] Settings = [$"{OrderIntakeAiOptions.SectionName}:ApiKey", $"{OrderIntakeAiOptions.SectionName}:LearningModel"];

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OrderIntakeAiOptions _options;

    public AnthropicHealthCheck(IHttpClientFactory httpClientFactory, IOptions<OrderIntakeAiOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        const string host = "api.anthropic.com";
        var extra = new Dictionary<string, object> { ["model"] = _options.LearningModel };
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return HealthCheckResult.Degraded(
                "anthropic: no API key (Integrations:OrderIntake:Ai:ApiKey).",
                data: HealthCheckFacts.Data(HealthCheckFacts.Reasons.NotConfigured, Settings, host, extra: extra));
        }

        try
        {
            var client = new AnthropicClient { ApiKey = _options.ApiKey, HttpClient = _httpClientFactory.CreateClient(HttpClientName), MaxRetries = 0 };
            await client.Models.Retrieve(_options.LearningModel, cancellationToken: cancellationToken);
            return HealthCheckResult.Healthy(
                $"anthropic: the key is accepted and model {_options.LearningModel} exists.",
                HealthCheckFacts.Data(HealthCheckFacts.Reasons.Ok, Settings, host, extra: extra));
        }
        catch (Exception e) when (e is AnthropicUnauthorizedException or AnthropicForbiddenException)
        {
            extra["status"] = e is AnthropicUnauthorizedException ? 401 : 403;
            return HealthCheckResult.Unhealthy("anthropic: the API key is refused.", data: HealthCheckFacts.Data(HealthCheckFacts.Reasons.Failing, Settings, host, extra: extra));
        }
        catch (AnthropicNotFoundException)
        {
            extra["status"] = 404;
            return HealthCheckResult.Unhealthy(
                $"anthropic: model {_options.LearningModel} does not exist.", data: HealthCheckFacts.Data(HealthCheckFacts.Reasons.Failing, Settings, host, extra: extra));
        }
        catch (Exception e) when (e is AnthropicIOException or HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("anthropic: not reachable.", data: HealthCheckFacts.Data(HealthCheckFacts.Reasons.Unreachable, Settings, host, extra: extra));
        }
        catch (AnthropicException e)
        {
            return HealthCheckResult.Unhealthy(
                $"anthropic: the request failed ({e.GetType().Name}).", data: HealthCheckFacts.Data(HealthCheckFacts.Reasons.Failing, Settings, host, extra: extra));
        }
    }
}

/// <summary>
/// The test of a mail provider's OAuth client (design 72, "Host settings"): a token request with a code that cannot be
/// valid. <c>invalid_grant</c> means the provider accepted the client id and secret and refused only the code (Healthy);
/// <c>invalid_client</c> or <c>unauthorized_client</c> means the client is wrong (Unhealthy).
/// </summary>
public abstract class MailOAuthClientHealthCheck : IHealthCheck
{
    /// <summary>The code of the test request: never a real authorization code.</summary>
    public const string DummyCode = "opentms-connection-test";

    private readonly IMailProviderClient _client;
    private readonly MailOAuthOptions _options;
    private readonly MailboxOAuthUrls _urls;

    protected MailOAuthClientHealthCheck(MailProviderClientResolver clients, IOptions<MailOAuthOptions> options, MailboxOAuthUrls urls)
    {
        _client = clients.Get(Provider);
        _options = options.Value;
        _urls = urls;
    }

    protected abstract MailProvider Provider { get; }

    protected abstract string CheckName { get; }

    protected abstract string Host { get; }

    protected abstract string SettingsSection { get; }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        string[] settings = [$"{MailOAuthOptions.SectionName}:{SettingsSection}:ClientId", $"{MailOAuthOptions.SectionName}:{SettingsSection}:ClientSecret"];
        if (!_options.For(Provider).IsConfigured)
        {
            return HealthCheckResult.Degraded(
                $"{CheckName}: the OAuth client id or secret is missing.",
                data: HealthCheckFacts.Data(HealthCheckFacts.Reasons.NotConfigured, settings, Host));
        }

        var redirectUri = await _urls.FindRedirectUriAsync() ?? "http://localhost/" + MailboxOAuthUrls.CallbackPath;
        try
        {
            await _client.ExchangeCodeAsync(DummyCode, redirectUri, cancellationToken);
            return Accepted(settings);
        }
        catch (MailProviderException e) when (e.OAuthError == "invalid_grant")
        {
            return Accepted(settings);
        }
        catch (MailProviderException e) when (e.ErrorCode.EndsWith("_unreachable", StringComparison.Ordinal))
        {
            return HealthCheckResult.Unhealthy($"{CheckName}: not reachable.", data: HealthCheckFacts.Data(HealthCheckFacts.Reasons.Unreachable, settings, Host));
        }
        catch (MailProviderException e)
        {
            return HealthCheckResult.Unhealthy(
                $"{CheckName}: the provider refused the OAuth client ({e.OAuthError ?? e.ErrorCode}).",
                data: HealthCheckFacts.Data(HealthCheckFacts.Reasons.Failing, settings, Host,
                    extra: new Dictionary<string, object> { ["error"] = e.OAuthError ?? e.ErrorCode }));
        }
    }

    private HealthCheckResult Accepted(string[] settings)
        => HealthCheckResult.Healthy($"{CheckName}: the OAuth client is accepted.", HealthCheckFacts.Data(HealthCheckFacts.Reasons.Ok, settings, Host));
}

public class GoogleOAuthHealthCheck : MailOAuthClientHealthCheck
{
    public const string Name = "google-oauth";

    public GoogleOAuthHealthCheck(MailProviderClientResolver clients, IOptions<MailOAuthOptions> options, MailboxOAuthUrls urls)
        : base(clients, options, urls)
    {
    }

    protected override MailProvider Provider => MailProvider.Gmail;

    protected override string CheckName => Name;

    protected override string Host => "oauth2.googleapis.com";

    protected override string SettingsSection => "Google";
}

public class MicrosoftOAuthHealthCheck : MailOAuthClientHealthCheck
{
    public const string Name = "microsoft-oauth";

    public MicrosoftOAuthHealthCheck(MailProviderClientResolver clients, IOptions<MailOAuthOptions> options, MailboxOAuthUrls urls)
        : base(clients, options, urls)
    {
    }

    protected override MailProvider Provider => MailProvider.Microsoft365;

    protected override string CheckName => Name;

    protected override string Host => "login.microsoftonline.com";

    protected override string SettingsSection => "Microsoft";
}
