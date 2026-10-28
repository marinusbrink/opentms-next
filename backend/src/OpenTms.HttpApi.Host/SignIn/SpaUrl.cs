using System.Linq;
using Volo.Abp.UI.Navigation.Urls;

namespace OpenTms.SignIn;

/// <summary>
/// The SPA's own address: the first allowed redirect URL (App:RedirectAllowedUrls), never a URL from the request. Where
/// the auth server has nowhere else to send the browser (its own root, a sign-in page opened without a return URL),
/// it sends it here, so a user always lands in the app and never on the auth server's pages
/// (docs/designs/62-sign-in-tenant-detection.md, "Where the browser lands").
/// </summary>
public static class SpaUrl
{
    /// <summary>The SPA's root with a trailing slash, or "/" when no redirect URL is configured.</summary>
    public static string Root(AppUrlOptions options)
    {
        var spa = options.RedirectAllowedUrls.FirstOrDefault(url => !string.IsNullOrWhiteSpace(url));
        return spa == null ? "/" : spa.Trim().TrimEnd('/') + "/";
    }
}
