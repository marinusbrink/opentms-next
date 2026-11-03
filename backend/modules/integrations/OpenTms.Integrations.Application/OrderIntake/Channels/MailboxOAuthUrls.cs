using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.UI.Navigation.Urls;

namespace OpenTms.Integrations.OrderIntake.Channels;

/// <summary>
/// The two addresses of the OAuth round trip (design 72 "Connect", assumption 5): the redirect URI registered at Google and
/// Microsoft, <c>{App:SelfUrl}/api/integrations/mailbox-oauth/callback</c> (the "MVC" application of
/// <see cref="AppUrlOptions"/>, as the invitation mail and the e-mail webhook use), and the SPA page the callback sends
/// the browser back to. The SPA's address is the first of <c>App:RedirectAllowedUrls</c>, the rule the sign-in pages
/// follow (the repository has no <c>App:ClientUrl</c>); never a URL from the request.
/// </summary>
public class MailboxOAuthUrls : ITransientDependency
{
    public const string CallbackPath = "api/integrations/mailbox-oauth/callback";

    /// <summary>The SPA page of the mailboxes (Integrations app → Order intake → Mailboxes).</summary>
    public const string MailboxesPagePath = "integrations/mailboxes";

    private const string SelfApplicationName = "MVC";

    private readonly IAppUrlProvider _appUrlProvider;
    private readonly AppUrlOptions _appUrlOptions;

    public MailboxOAuthUrls(IAppUrlProvider appUrlProvider, IOptions<AppUrlOptions> appUrlOptions)
    {
        _appUrlProvider = appUrlProvider;
        _appUrlOptions = appUrlOptions.Value;
    }

    /// <summary>The redirect URI, or null when <c>App:SelfUrl</c> is not configured.</summary>
    public async Task<string?> FindRedirectUriAsync()
    {
        try
        {
            var root = await _appUrlProvider.GetUrlAsync(SelfApplicationName);
            return string.IsNullOrWhiteSpace(root) ? null : root.TrimEnd('/') + "/" + CallbackPath;
        }
        catch (AbpException)
        {
            return null;
        }
    }

    public async Task<string> GetRedirectUriAsync()
        => await FindRedirectUriAsync() ?? throw new AbpException("App:SelfUrl is not configured: the mailbox OAuth redirect URI cannot be built.");

    /// <summary>The mailboxes page of the SPA with one query parameter.</summary>
    public string MailboxesPage(string parameter, string value)
    {
        var spa = _appUrlOptions.RedirectAllowedUrls.FirstOrDefault(url => !string.IsNullOrWhiteSpace(url));
        var root = spa == null ? "/" : spa.Trim().TrimEnd('/') + "/";
        return root + MailboxesPagePath + "?" + Uri.EscapeDataString(parameter) + "=" + Uri.EscapeDataString(value);
    }
}
