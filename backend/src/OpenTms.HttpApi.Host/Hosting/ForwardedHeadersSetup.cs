using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;

namespace OpenTms.Hosting;

/* Cloud Run (and any TLS-terminating proxy) forwards plain HTTP with X-Forwarded-Proto: https. The host must take
 * the scheme from that header in every environment, otherwise OpenIddict sees HTTP and refuses the request when
 * AuthServer:RequireHttpsMetadata is on (docs/designs/gcp-infrastructure.md). The proxy's addresses are not fixed,
 * so the known networks are cleared.
 */
public static class ForwardedHeadersSetup
{
    public static void Apply(ForwardedHeadersOptions options)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    }
}
