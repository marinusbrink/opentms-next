using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Timing;

namespace OpenTms.Integrations.OrderIntake.Channels;

/// <summary>
/// The HTTP plumbing the Gmail and Graph clients share: the OAuth token request (form post, <c>invalid_grant</c> on a
/// refresh → <see cref="MailboxReconnectRequiredException"/>) and bearer-authorised GETs. Errors carry short codes
/// (<c>gmail_http_500</c>, <c>graph_unreachable</c>), never response bodies or addresses. Each call is one attempt: a
/// failing sync is retried by the next period of the worker.
/// </summary>
public abstract class MailProviderClientBase : IMailProviderClient
{
    /// <summary>The timeout of one provider call.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    protected IHttpClientFactory HttpClientFactory { get; }

    protected MailOAuthOptions Options { get; }

    protected IClock Clock { get; }

    protected MailProviderClientBase(IHttpClientFactory httpClientFactory, IOptions<MailOAuthOptions> options, IClock clock)
    {
        HttpClientFactory = httpClientFactory;
        Options = options.Value;
        Clock = clock;
    }

    public abstract MailProvider Provider { get; }

    /// <summary>The named HttpClient of this provider.</summary>
    protected abstract string HttpClientName { get; }

    /// <summary>The prefix of this provider's error codes ("gmail", "graph").</summary>
    protected abstract string ErrorPrefix { get; }

    protected abstract string TokenEndpoint { get; }

    public abstract string BuildAuthorizeUrl(string state, string redirectUri);

    public abstract Task<MailTokenResult> ExchangeCodeAsync(string code, string redirectUri, CancellationToken cancellationToken = default);

    public abstract Task<MailTokenResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);

    public abstract Task<string> GetAccountAddressAsync(string accessToken, CancellationToken cancellationToken = default);

    public abstract Task<IReadOnlyList<MailFolder>> ListFoldersAsync(string accessToken, CancellationToken cancellationToken = default);

    public abstract Task<string> GetCurrentCursorAsync(string accessToken, string folderId, CancellationToken cancellationToken = default);

    public abstract IAsyncEnumerable<MailChange> GetChangesAsync(
        string accessToken, string folderId, string cursor, DateTime? lastSyncAt, CancellationToken cancellationToken = default);

    public abstract Task<MailRawMessage?> FetchRawAsync(string accessToken, MailMessageRef message, CancellationToken cancellationToken = default);

    /// <summary>The configured OAuth client of this provider, or <see cref="IntegrationsErrorCodes.ProviderNotConfigured"/>.</summary>
    protected MailOAuthClientOptions RequireClient()
    {
        var client = Options.For(Provider);
        if (!client.IsConfigured)
        {
            throw new BusinessException(IntegrationsErrorCodes.ProviderNotConfigured).WithData("provider", Provider.ToString());
        }

        return client;
    }

    protected static string Query(string baseUrl, IEnumerable<KeyValuePair<string, string>> parameters)
        => baseUrl + "?" + string.Join("&", parameters.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));

    /// <summary>Posts a token request. <paramref name="isRefresh"/>: an <c>invalid_grant</c> means the refresh token is gone.</summary>
    protected async Task<MailTokenResult> PostTokenAsync(IDictionary<string, string> form, bool isRefresh, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint) { Content = new FormUrlEncodedContent(form) };
        using var response = await SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = ReadOAuthError(body);
            if (isRefresh && error == "invalid_grant")
            {
                throw new MailboxReconnectRequiredException(Provider);
            }

            throw new MailProviderException(
                $"{ErrorPrefix}_token_{error ?? ((int)response.StatusCode).ToString()}",
                $"The {Provider} token endpoint answered HTTP {(int)response.StatusCode} ({error ?? "no error code"}).",
                oAuthError: error);
        }

        using var document = Parse(body);
        var root = document.RootElement;
        var accessToken = root.TryGetProperty("access_token", out var access) ? access.GetString() : null;
        if (string.IsNullOrEmpty(accessToken))
        {
            throw new MailProviderException($"{ErrorPrefix}_token_no_access_token", $"The {Provider} token endpoint answered without an access token.");
        }

        var refreshToken = root.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() : null;
        return new MailTokenResult(accessToken, string.IsNullOrEmpty(refreshToken) ? null : refreshToken);
    }

    /// <summary>A bearer GET returning the JSON body; null on 404 (or on <paramref name="nullOn"/>) when <paramref name="allowNotFound"/>.</summary>
    protected async Task<JsonDocument?> GetJsonAsync(
        string url, string accessToken, CancellationToken cancellationToken, bool allowNotFound = false,
        HttpStatusCode? nullOn = null, IReadOnlyDictionary<string, string>? headers = null)
    {
        using var request = Authorized(HttpMethod.Get, url, accessToken, headers);
        using var response = await SendAsync(request, cancellationToken);
        if ((allowNotFound && response.StatusCode == HttpStatusCode.NotFound) || (nullOn != null && response.StatusCode == nullOn))
        {
            return null;
        }

        EnsureSuccess(response);
        return Parse(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    /// <summary>A bearer GET returning the raw bytes; null on 404.</summary>
    protected async Task<byte[]?> GetBytesAsync(string url, string accessToken, CancellationToken cancellationToken)
    {
        using var request = Authorized(HttpMethod.Get, url, accessToken, null);
        using var response = await SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        EnsureSuccess(response);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    protected static string? GetString(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    protected static IEnumerable<JsonElement> GetArray(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
            : [];

    private HttpRequestMessage Authorized(HttpMethod method, string url, string accessToken, IReadOnlyDictionary<string, string>? headers)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (headers != null)
        {
            foreach (var (name, value) in headers)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }
        }

        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var http = HttpClientFactory.CreateClient(HttpClientName);
        try
        {
            return await http.SendAsync(request, cancellationToken);
        }
        catch (Exception e) when (e is HttpRequestException || (e is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new MailProviderException($"{ErrorPrefix}_unreachable", $"{Provider} could not be reached.", e);
        }
    }

    private void EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new MailProviderException($"{ErrorPrefix}_http_{(int)response.StatusCode}", $"{Provider} answered HTTP {(int)response.StatusCode}.");
        }
    }

    private JsonDocument Parse(string body)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException e)
        {
            throw new MailProviderException($"{ErrorPrefix}_invalid_response", $"{Provider} answered something that is not JSON.", e);
        }
    }

    private static string? ReadOAuthError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var error = GetString(document.RootElement, "error");
            /* Only the short OAuth codes: never a description that might echo input. */
            return error != null && error.Length <= 64 && error.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') ? error : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
