using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace OpenTms.Hosting;

/// <summary>
/// Cloud Run terminates TLS and forwards plain HTTP with X-Forwarded-Proto (docs/designs/gcp-infrastructure.md).
/// Risk class: Critical — authentication: with AuthServer:RequireHttpsMetadata on, OpenIddict refuses every request
/// whose scheme is not https, so sign-in on a deployed environment depends on this.
/// </summary>
public class ForwardedHeadersSetupTests
{
    private static async Task<HttpContext> SendThroughMiddlewareAsync(string? forwardedProto, IPAddress remoteAddress)
    {
        var options = new ForwardedHeadersOptions();
        ForwardedHeadersSetup.Apply(options);

        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Connection.RemoteIpAddress = remoteAddress;
        if (forwardedProto != null)
        {
            context.Request.Headers["X-Forwarded-Proto"] = forwardedProto;
        }

        var middleware = new ForwardedHeadersMiddleware(_ => Task.CompletedTask, NullLoggerFactory.Instance, Options.Create(options));
        await middleware.Invoke(context);
        return context;
    }

    [Fact]
    public async Task The_scheme_comes_from_the_proxy_header_even_from_an_address_outside_loopback()
    {
        // Cloud Run's front end has no fixed address; the default (loopback-only) known networks would ignore it.
        var context = await SendThroughMiddlewareAsync("https", IPAddress.Parse("169.254.1.1"));

        context.Request.Scheme.ShouldBe("https");
    }

    [Fact]
    public async Task Without_the_header_the_scheme_stays_as_received()
    {
        var context = await SendThroughMiddlewareAsync(null, IPAddress.Parse("169.254.1.1"));

        context.Request.Scheme.ShouldBe("http");
    }

    [Fact]
    public async Task Only_the_scheme_is_taken_over_not_the_client_address()
    {
        var options = new ForwardedHeadersOptions();
        ForwardedHeadersSetup.Apply(options);

        options.ForwardedHeaders.ShouldBe(ForwardedHeaders.XForwardedProto);

        var context = await SendThroughMiddlewareAsync("https", IPAddress.Parse("169.254.1.1"));
        context.Connection.RemoteIpAddress.ShouldBe(IPAddress.Parse("169.254.1.1"));
    }
}
