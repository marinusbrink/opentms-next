using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTms.HostIntegration;
using OpenTms.Integrations.AiConnector;
using OpenTms.Integrations.Permissions;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Xunit;
using static OpenTms.AiConnector.AiConnectorHarness;

namespace OpenTms.AiConnector;

/// <summary>
/// Design 74 "Refusals" and "Build notes, Switching off commits in the tenant first" (risk class Critical), on the real host and PostgreSQL, where the tenant's and the host's databases share no transaction.
/// Revoke and withdraw meet a real failure, a trigger refusing the update of one row: a failure on the tenant's side, also one only at COMMIT, has revoked nothing in the host and leaves the connection working;
/// a failure on the host's side leaves the row switched off, <c>/mcp</c> refusing at once and an error logged, and a repeated <c>POST .../revoke</c> completes the host side. For a tenant with its own database
/// and one in the host database, where the two sides happen to share a database and still no transaction.
/// </summary>
[Collection(HostIntegrationCollection.Name)]
public class SwitchingOffFailureHostTests
{
    private const string Root = "/api/integrations/ai-connector";
    private const string ConnectionsTable = "integrations.\"AiConnections\"";
    private const string AuthorizationsTable = "\"OpenIddictAuthorizations\"";
    private const string TokensTable = "\"OpenIddictTokens\"";

    private readonly AiConnectorHarness _h;

    public SwitchingOffFailureHostTests(HostIntegrationFixture host)
    {
        _h = new AiConnectorHarness(host);
    }

    /// <summary>One tenant per tier that only revokes its own new row, so other tests' connections stay as they are.</summary>
    private Guid RevokeTenant(string tier) => tier == "own" ? _h.TenantA : _h.TenantB;

    /// <summary>A withdrawal touches a whole tenant: tenant C, which other withdraw tests use too and put back, or a shared-tier tenant of this test alone.</summary>
    private async Task<Guid> WithdrawTenantAsync(string tier) => tier == "own" ? _h.TenantC : await _h.NewTenantAsync();

    private sealed record Connection(TestUser User, TokenAnswer Token, Guid AuthorizationId, Guid RowId);

    private async Task<Connection> ConnectAsync(Guid tenant)
    {
        await _h.ReadyAsync(tenant);
        var user = await _h.CreateUserAsync(tenant, AllToolPermissions);
        var token = await _h.ConnectAsync(user);
        var authorization = AuthorizationIdOf(token.AccessToken!);
        var row = await _h.InScopeAsync(tenant, async services => (await services.GetRequiredService<IRepository<AiConnection, Guid>>().FirstAsync(c => c.AuthorizationId == authorization)).Id);
        return new Connection(user, token, Guid.Parse(authorization), row);
    }

    private async Task<string> ManagerTokenAsync(Guid tenant)
        => await _h.BrowserTokenAsync(await _h.CreateUserAsync(tenant, IntegrationsPermissions.AiConnector.Use, IntegrationsPermissions.AiConnector.Manage));

    private async Task<DateTime?> RevokedAtAsync(Guid tenant, Guid rowId)
        => await _h.InScopeAsync(tenant, async services => (await services.GetRequiredService<IRepository<AiConnection, Guid>>().GetAsync(rowId)).RevokedAt);

    private async Task<long> RevokedAuthorizationsAsync(Guid authorization)
        => await _h.CountAsync(null, "SELECT count(*) FROM \"OpenIddictAuthorizations\" WHERE \"Id\" = @id AND \"Status\" = 'revoked'", ("id", authorization));

    private async Task<long> TokensAsync(Guid authorization)
        => await _h.CountAsync(null, "SELECT count(*) FROM \"OpenIddictTokens\" WHERE \"AuthorizationId\" = @id", ("id", authorization));

    private async Task<long> RevokedTokensAsync(Guid authorization)
        => await _h.CountAsync(null, "SELECT count(*) FROM \"OpenIddictTokens\" WHERE \"AuthorizationId\" = @id AND \"Status\" = 'revoked'", ("id", authorization));

    private async Task ShouldBeUntouchedInTheHostAsync(Connection connection)
    {
        (await RevokedAuthorizationsAsync(connection.AuthorizationId)).ShouldBe(0, "the authorization is not revoked");
        (await TokensAsync(connection.AuthorizationId)).ShouldBeGreaterThan(0, "the control: this authorization has tokens");
        (await RevokedTokensAsync(connection.AuthorizationId)).ShouldBe(0, "no token is revoked");
    }

    private async Task ShouldBeRevokedInTheHostAsync(Guid authorization)
    {
        (await RevokedAuthorizationsAsync(authorization)).ShouldBe(1, "the authorization is revoked");
        var tokens = await TokensAsync(authorization);
        tokens.ShouldBeGreaterThan(0, "the control: this authorization has tokens");
        (await RevokedTokensAsync(authorization)).ShouldBe(tokens, "every token of it is revoked");
    }

    private async Task ShouldBeServedAsync(TokenAnswer token) => (await _h.ListToolsAsync(token.AccessToken!)).ShouldNotBeEmpty();

    private async Task ShouldBeRefusedAsync(TokenAnswer token, string reason)
    {
        using var response = await _h.PostMcpAsync(token.AccessToken, "tools/list");
        await AiConnectorHarness.ShouldBeRefusedAsync(response, reason);
    }

    private static async Task ShouldHaveFailedWithoutDetailAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError, body);
        foreach (var detail in new[] { InjectedFailure, "Npgsql", "P0001", "AiConnections", "   at " })
        {
            body.ShouldNotContain(detail, Case.Insensitive, "the caller learns nothing of how it failed");
        }
    }

    private List<HostLogEntry> RevokerErrorsSince(int mark) => _h.Host.Logs.Entries.Skip(mark)
        .Where(e => e.Category == typeof(AiConnectionAuthorizationRevoker).FullName && e.Level >= LogLevel.Error).ToList();

    private async Task<HttpResponseMessage> RevokeAsync(string token, Guid rowId)
        => await _h.RestAsync(token, HttpMethod.Post, $"{Root}/connections/{rowId}/revoke");

    /* ---------- The tenant's side fails ---------- */

    [Theory]
    [InlineData("own", false)]
    [InlineData("own", true)]
    [InlineData("shared", false)]
    [InlineData("shared", true)]
    public async Task A_tenant_side_failure_of_revoke_revokes_nothing_in_the_host_and_the_connection_goes_on(string tier, bool atCommit)
    {
        var tenant = RevokeTenant(tier);
        var connection = await ConnectAsync(tenant);
        var manager = await ManagerTokenAsync(tenant);
        var mark = _h.Host.Logs.Entries.Count;

        await using (await _h.FailUpdatesAsync(tenant, ConnectionsTable, "Id", connection.RowId, atCommit))
        {
            using var response = await RevokeAsync(manager, connection.RowId);
            await ShouldHaveFailedWithoutDetailAsync(response);
        }

        (await RevokedAtAsync(tenant, connection.RowId)).ShouldBeNull("the row stays on: its transaction did not commit");
        await ShouldBeUntouchedInTheHostAsync(connection);
        RevokerErrorsSince(mark).ShouldBeEmpty("the host step never ran");
        await ShouldBeServedAsync(connection.Token);

        var refreshed = await _h.RefreshAsync(ClaudeClient, connection.Token.RefreshToken!);
        refreshed.Status.ShouldBe(HttpStatusCode.OK, refreshed.Body);
        await ShouldBeServedAsync(refreshed);

        /* Repeating it once the tenant's side works switches it off properly. */
        using (var again = await RevokeAsync(manager, connection.RowId))
        {
            again.StatusCode.ShouldBe(HttpStatusCode.NoContent, await again.Content.ReadAsStringAsync());
        }

        (await RevokedAtAsync(tenant, connection.RowId)).ShouldNotBeNull();
        await ShouldBeRevokedInTheHostAsync(connection.AuthorizationId);
        await ShouldBeRefusedAsync(refreshed, nameof(AiConnectorRefusal.ConnectionRevoked));
    }

    [Theory]
    [InlineData("own", false)]
    [InlineData("own", true)]
    [InlineData("shared", false)]
    [InlineData("shared", true)]
    public async Task A_tenant_side_failure_of_withdraw_revokes_nothing_in_the_host_and_keeps_the_acceptance(string tier, bool atCommit)
    {
        var tenant = await WithdrawTenantAsync(tier);
        var first = await ConnectAsync(tenant);
        var second = await ConnectAsync(tenant);
        var manager = await ManagerTokenAsync(tenant);
        var mark = _h.Host.Logs.Entries.Count;

        try
        {
            await using (await _h.FailUpdatesAsync(tenant, ConnectionsTable, "Id", second.RowId, atCommit))
            {
                using var response = await _h.RestAsync(manager, HttpMethod.Post, Root + "/withdraw");
                await ShouldHaveFailedWithoutDetailAsync(response);
            }

            foreach (var connection in new[] { first, second })
            {
                (await RevokedAtAsync(tenant, connection.RowId)).ShouldBeNull("nothing of the withdrawal committed");
                await ShouldBeUntouchedInTheHostAsync(connection);
                await ShouldBeServedAsync(connection.Token);
            }

            RevokerErrorsSince(mark).ShouldBeEmpty("the host step never ran");

            using var status = await _h.RestAsync(manager, HttpMethod.Get, Root + "/status");
            var body = await status.Content.ReadAsStringAsync();
            status.StatusCode.ShouldBe(HttpStatusCode.OK, body);
            JsonDocument.Parse(body).RootElement.GetProperty("isAccepted").GetBoolean().ShouldBeTrue("the acceptance rolled back with the rows");
        }
        finally
        {
            await _h.SetAcceptedAsync(tenant);
        }
    }

    /* ---------- The host's side fails ---------- */

    /// <summary>Revoke with the host's update refused: the two tables the host step writes, in one transaction.</summary>
    private async Task<(Connection Connection, string Manager, int Mark)> RevokeWithAHostFailureAsync(Guid tenant, string table, string column)
    {
        var connection = await ConnectAsync(tenant);
        var manager = await ManagerTokenAsync(tenant);
        var mark = _h.Host.Logs.Entries.Count;

        await using (await _h.FailUpdatesAsync(null, table, column, connection.AuthorizationId))
        {
            using var response = await RevokeAsync(manager, connection.RowId);
            response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());

            /* The fault still stands: this does not depend on the host side recovering. */
            await ShouldBeRefusedAsync(connection.Token, nameof(AiConnectorRefusal.ConnectionRevoked));
        }

        return (connection, manager, mark);
    }

    [Theory]
    [InlineData("own", TokensTable, "AuthorizationId")]
    [InlineData("own", AuthorizationsTable, "Id")]
    [InlineData("shared", TokensTable, "AuthorizationId")]
    [InlineData("shared", AuthorizationsTable, "Id")]
    public async Task A_host_side_failure_of_revoke_leaves_the_row_off_mcp_refusing_and_an_error_logged_with_nothing_half_done_in_the_host(string tier, string table, string column)
    {
        var tenant = RevokeTenant(tier);
        var (connection, _, mark) = await RevokeWithAHostFailureAsync(tenant, table, column);

        (await RevokedAtAsync(tenant, connection.RowId)).ShouldNotBeNull("the tenant's side committed first");
        await ShouldBeRefusedAsync(connection.Token, nameof(AiConnectorRefusal.ConnectionRevoked));

        var error = RevokerErrorsSince(mark).ShouldHaveSingleItem();
        error.Message.ShouldContain(connection.AuthorizationId.ToString(), Case.Insensitive);
        error.Message.ShouldNotContain(connection.User.Email, Case.Insensitive);
        error.Exception.ShouldNotBeNull();

        await ShouldBeUntouchedInTheHostAsync(connection);
    }

    [Theory]
    [InlineData("own", TokensTable, "AuthorizationId")]
    [InlineData("own", AuthorizationsTable, "Id")]
    [InlineData("shared", TokensTable, "AuthorizationId")]
    [InlineData("shared", AuthorizationsTable, "Id")]
    public async Task A_retried_revoke_after_a_host_side_failure_completes_the_host_side_and_keeps_the_first_switching_off(string tier, string table, string column)
    {
        var tenant = RevokeTenant(tier);
        var (connection, manager, _) = await RevokeWithAHostFailureAsync(tenant, table, column);
        var revokedAt = await RevokedAtAsync(tenant, connection.RowId);

        var mark = _h.Host.Logs.Entries.Count;
        using (var retry = await RevokeAsync(manager, connection.RowId))
        {
            retry.StatusCode.ShouldBe(HttpStatusCode.NoContent, await retry.Content.ReadAsStringAsync());
        }

        RevokerErrorsSince(mark).ShouldBeEmpty();
        (await RevokedAtAsync(tenant, connection.RowId)).ShouldBe(revokedAt, "the first switching off stays on record");
        await ShouldBeRevokedInTheHostAsync(connection.AuthorizationId);

        var refused = await _h.RefreshAsync(ClaudeClient, connection.Token.RefreshToken!);
        refused.Status.ShouldBe(HttpStatusCode.BadRequest, refused.Body);
        refused.AccessToken.ShouldBeNull();
    }

    /// <summary>Design 74: "the tool can renew a token that /mcp refuses" until the retry, which then stops what was renewed too.</summary>
    [Theory]
    [InlineData("own", TokensTable, "AuthorizationId")]
    [InlineData("own", AuthorizationsTable, "Id")]
    [InlineData("shared", TokensTable, "AuthorizationId")]
    [InlineData("shared", AuthorizationsTable, "Id")]
    public async Task Until_the_retry_after_a_host_side_failure_a_refresh_renews_a_token_that_mcp_refuses_and_the_retry_stops_that_too(string tier, string table, string column)
    {
        var tenant = RevokeTenant(tier);
        var (connection, manager, _) = await RevokeWithAHostFailureAsync(tenant, table, column);

        var renewed = await _h.RefreshAsync(ClaudeClient, connection.Token.RefreshToken!);
        renewed.Status.ShouldBe(HttpStatusCode.OK, renewed.Body);
        await ShouldBeRefusedAsync(renewed, nameof(AiConnectorRefusal.ConnectionRevoked));

        using (var retry = await RevokeAsync(manager, connection.RowId))
        {
            retry.StatusCode.ShouldBe(HttpStatusCode.NoContent, await retry.Content.ReadAsStringAsync());
        }

        await ShouldBeRevokedInTheHostAsync(connection.AuthorizationId);
        var refused = await _h.RefreshAsync(ClaudeClient, renewed.RefreshToken!);
        refused.Status.ShouldBe(HttpStatusCode.BadRequest, refused.Body);
        refused.AccessToken.ShouldBeNull();
    }

    /// <summary>The user comes back after the switching off was completed: a connection that works, on an authorization of its own, as after any revoke; the old token stays refused.</summary>
    [Theory]
    [InlineData(TokensTable, "AuthorizationId")]
    [InlineData(AuthorizationsTable, "Id")]
    public async Task Connecting_again_after_a_retried_revoke_gives_a_working_connection_and_leaves_the_old_token_refused(string table, string column)
    {
        var tenant = RevokeTenant("own");
        var (connection, manager, _) = await RevokeWithAHostFailureAsync(tenant, table, column);
        using (var retry = await RevokeAsync(manager, connection.RowId))
        {
            retry.StatusCode.ShouldBe(HttpStatusCode.NoContent, await retry.Content.ReadAsStringAsync());
        }

        var again = await _h.ConnectAsync(connection.User);

        await ShouldBeServedAsync(again);
        await ShouldBeRefusedAsync(connection.Token, nameof(AiConnectorRefusal.ConnectionRevoked));
        AuthorizationIdOf(again.AccessToken!).ShouldNotBe(connection.AuthorizationId.ToString(), "a switched-off connection does not come back");
    }

    [Theory]
    [InlineData("own")]
    [InlineData("shared")]
    public async Task A_host_side_failure_of_withdraw_leaves_it_withdrawn_stops_only_that_connection_in_the_host_and_a_revoke_completes_it(string tier)
    {
        var tenant = await WithdrawTenantAsync(tier);
        var failing = await ConnectAsync(tenant);
        var other = await ConnectAsync(tenant);
        var manager = await ManagerTokenAsync(tenant);
        var mark = _h.Host.Logs.Entries.Count;

        try
        {
            await using (await _h.FailUpdatesAsync(null, TokensTable, "AuthorizationId", failing.AuthorizationId))
            {
                using var response = await _h.RestAsync(manager, HttpMethod.Post, Root + "/withdraw");
                var body = await response.Content.ReadAsStringAsync();
                response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
                JsonDocument.Parse(body).RootElement.GetProperty("isAccepted").GetBoolean().ShouldBeFalse();

                await ShouldBeRefusedAsync(failing.Token, nameof(AiConnectorRefusal.NotAccepted));
                await ShouldBeRefusedAsync(other.Token, nameof(AiConnectorRefusal.NotAccepted));
            }

            (await RevokedAtAsync(tenant, failing.RowId)).ShouldNotBeNull();
            (await RevokedAtAsync(tenant, other.RowId)).ShouldNotBeNull();

            var error = RevokerErrorsSince(mark).ShouldHaveSingleItem("one line for the one that failed");
            error.Message.ShouldContain(failing.AuthorizationId.ToString(), Case.Insensitive);

            await ShouldBeUntouchedInTheHostAsync(failing);
            await ShouldBeRevokedInTheHostAsync(other.AuthorizationId);

            /* Withdrawing again looks only at rows still on, so the retry is the revoke endpoint, also while the tenant has withdrawn. */
            using (var retry = await RevokeAsync(manager, failing.RowId))
            {
                retry.StatusCode.ShouldBe(HttpStatusCode.NoContent, await retry.Content.ReadAsStringAsync());
            }

            await ShouldBeRevokedInTheHostAsync(failing.AuthorizationId);
            var refused = await _h.RefreshAsync(ClaudeClient, failing.Token.RefreshToken!);
            refused.Status.ShouldBe(HttpStatusCode.BadRequest, refused.Body);
            refused.AccessToken.ShouldBeNull();
        }
        finally
        {
            await _h.SetAcceptedAsync(tenant);
        }
    }

    /* ---------- Isolation ---------- */

    [Fact]
    public async Task A_host_side_failure_of_one_tenants_revoke_touches_no_connection_of_another_tenant()
    {
        var (mine, other, _) = _h.Pair("own");
        var theirs = await ConnectAsync(other);
        var connection = await ConnectAsync(mine);
        var manager = await ManagerTokenAsync(mine);

        await using (await _h.FailUpdatesAsync(null, TokensTable, "AuthorizationId", connection.AuthorizationId))
        {
            using var response = await RevokeAsync(manager, connection.RowId);
            response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        }

        await ShouldBeUntouchedInTheHostAsync(theirs);
        await ShouldBeServedAsync(theirs.Token);

        /* The other tenant's manager cannot retry it: the row is not found there. */
        using var foreign = await RevokeAsync(await ManagerTokenAsync(other), connection.RowId);
        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound, await foreign.Content.ReadAsStringAsync());
        await ShouldBeUntouchedInTheHostAsync(connection);
    }
}
