using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace OpenTms.AiConnector;

/// <summary>
/// The host tests run in Development, where the host skips <c>UseErrorPage</c>; production does not, and there it turned the
/// 401 of <c>/mcp</c> into a redirect to <c>/Error</c>, so an AI tool never saw the challenge that starts its sign-in (design 74).
/// This pipeline has the error page and the gate in the host's order. Risk class: Critical (the connector cannot connect without it).
/// </summary>
public class McpErrorPageTests
{
    private const string SelfUrl = "https://api.example.test";

    private static async Task<WebApplication> StartAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Configuration["App:SelfUrl"] = SelfUrl;
        var app = builder.Build();
        app.UseStatusCodePagesWithRedirects("~/Error?httpStatusCode={0}");
        app.Use(AiConnectorTokenGate.InvokeAsync);
        app.Run(context =>
        {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            return Task.CompletedTask;
        });
        await app.StartAsync();
        return app;
    }

    [Fact]
    public async Task Without_a_token_mcp_answers_401_with_its_challenge_not_a_redirect_to_the_error_page()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();

        var response = await client.PostAsync("/mcp", new StringContent("{}"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Location.ShouldBeNull();
        response.Headers.WwwAuthenticate.ToString().ShouldContain($"resource_metadata=\"{SelfUrl}/.well-known/oauth-protected-resource\"");
    }

    [Fact]
    public async Task A_get_on_mcp_answers_405_not_a_redirect()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();

        var response = await client.GetAsync("/mcp");

        response.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
        response.Headers.Location.ShouldBeNull();
    }

    [Fact]
    public async Task Every_other_path_keeps_the_error_page()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();

        var response = await client.GetAsync("/api/anything");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect, "the error page is on in this pipeline, so the tests above prove the gate");
        response.Headers.Location!.ToString().ShouldContain("/Error?httpStatusCode=404");
    }
}
