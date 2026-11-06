using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
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

    /* ---------- Never retried: the user just connects again ---------- */

    private sealed record Row(Guid Id, string AuthorizationId, DateTime ConnectedAt, DateTime? RevokedAt, Guid? RevokedByUserId);

    private async Task<List<Row>> RowsOfAsync(TestUser user)
        => await _h.InScopeAsync(user.TenantId, async services =>
            (await services.GetRequiredService<IRepository<AiConnection, Guid>>().GetListAsync(c => c.UserId == user.Id))
            .Select(c => new Row(c.Id, c.AuthorizationId, c.ConnectedAt, c.RevokedAt, c.RevokedByUserId)).ToList());

    /// <summary>Design 74 lets a refresh renew while the host still holds the authorization as valid; under a fault on its tokens it may fail instead. What it may never give is a token <c>/mcp</c> serves.</summary>
    private async Task<string> RefreshShouldGiveNothingMcpServesAsync(string refreshToken)
    {
        var renewed = await _h.RefreshAsync(ClaudeClient, refreshToken);
        if (renewed.AccessToken != null)
        {
            await ShouldBeRefusedAsync(renewed, nameof(AiConnectorRefusal.ConnectionRevoked));
        }

        return renewed.RefreshToken ?? refreshToken;
    }

    private async Task ShouldBeARefusedRefreshAsync(string refreshToken)
    {
        var refused = await _h.RefreshAsync(ClaudeClient, refreshToken);
        refused.Status.ShouldBe(HttpStatusCode.BadRequest, refused.Body);
        refused.AccessToken.ShouldBeNull();
    }

    private async Task ShouldBeAWorkingNewConnectionAsync(Connection old, TokenAnswer again, Row oldRowBefore)
    {
        var authorization = AuthorizationIdOf(again.AccessToken!);
        authorization.ShouldNotBe(old.AuthorizationId.ToString(), "a new authorization");

        var rows = await RowsOfAsync(old.User);
        rows.Count.ShouldBe(2, "the old row and a new one");
        var newRow = rows.Single(r => r.AuthorizationId == authorization);
        newRow.Id.ShouldNotBe(old.RowId, "a new row");
        newRow.RevokedAt.ShouldBeNull();
        rows.Single(r => r.Id == old.RowId).ShouldBe(oldRowBefore, "the old row stays exactly as it was switched off");

        await ShouldBeServedAsync(again);
        var renewed = await _h.RefreshAsync(ClaudeClient, again.RefreshToken!);
        renewed.Status.ShouldBe(HttpStatusCode.OK, renewed.Body);
        await ShouldBeServedAsync(renewed);
        (await RevokedAuthorizationsAsync(Guid.Parse(authorization))).ShouldBe(0, "the new authorization is valid");
    }

    /// <summary>The critical invariant: a switching off that never completed in the host, and no retry. The connect revokes the old authorization on the way, and nothing of the old connection works.</summary>
    [Theory]
    [InlineData("own", TokensTable, "AuthorizationId")]
    [InlineData("own", AuthorizationsTable, "Id")]
    [InlineData("shared", TokensTable, "AuthorizationId")]
    [InlineData("shared", AuthorizationsTable, "Id")]
    public async Task Connecting_again_after_a_host_side_failure_never_retried_makes_a_new_connection_revokes_the_old_authorization_and_the_old_tokens_stay_refused(string tier, string table, string column)
    {
        var tenant = RevokeTenant(tier);
        var (connection, _, _) = await RevokeWithAHostFailureAsync(tenant, table, column);
        await ShouldBeUntouchedInTheHostAsync(connection); /* The control: the host still holds the old authorization as valid. */
        var oldRow = (await RowsOfAsync(connection.User)).ShouldHaveSingleItem();
        oldRow.RevokedAt.ShouldNotBeNull();

        var mark = _h.Host.Logs.Entries.Count;
        var again = await _h.ConnectAsync(connection.User);

        await ShouldBeAWorkingNewConnectionAsync(connection, again, oldRow);
        RevokerErrorsSince(mark).ShouldBeEmpty("revoking the old authorization succeeded");
        await ShouldBeRevokedInTheHostAsync(connection.AuthorizationId);
        await ShouldBeRefusedAsync(connection.Token, nameof(AiConnectorRefusal.ConnectionRevoked));
        await ShouldBeARefusedRefreshAsync(connection.Token.RefreshToken!);
    }

    /// <summary>The fault still stands while the user connects: revoking the old authorization fails again, is logged, does not stop the connect, and the old tokens stay refused because they name the old row. The next connect repairs the host.</summary>
    [Theory]
    [InlineData("own", TokensTable, "AuthorizationId")]
    [InlineData("own", AuthorizationsTable, "Id")]
    [InlineData("shared", TokensTable, "AuthorizationId")]
    [InlineData("shared", AuthorizationsTable, "Id")]
    public async Task Connecting_again_while_the_old_authorization_cannot_be_revoked_logs_an_error_and_the_old_tokens_stay_refused_anyway(string tier, string table, string column)
    {
        var tenant = RevokeTenant(tier);
        var connection = await ConnectAsync(tenant);
        var manager = await ManagerTokenAsync(tenant);
        TokenAnswer again;
        Row oldRow;
        string oldRefresh;

        await using (await _h.FailUpdatesAsync(null, table, column, connection.AuthorizationId))
        {
            using (var response = await RevokeAsync(manager, connection.RowId))
            {
                response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
            }

            oldRow = (await RowsOfAsync(connection.User)).ShouldHaveSingleItem();
            oldRow.RevokedAt.ShouldNotBeNull();

            var mark = _h.Host.Logs.Entries.Count;
            again = await _h.ConnectAsync(connection.User);

            var error = RevokerErrorsSince(mark).ShouldHaveSingleItem("the connect tried to revoke the old authorization once");
            error.Message.ShouldContain(connection.AuthorizationId.ToString(), Case.Insensitive);
            error.Message.ShouldNotContain(connection.User.Email, Case.Insensitive);
            error.Exception.ShouldNotBeNull();

            await ShouldBeAWorkingNewConnectionAsync(connection, again, oldRow);
            await ShouldBeUntouchedInTheHostAsync(connection);
            await ShouldBeRefusedAsync(connection.Token, nameof(AiConnectorRefusal.ConnectionRevoked));
            oldRefresh = await RefreshShouldGiveNothingMcpServesAsync(connection.Token.RefreshToken!);
        }

        /* The fault is gone and still nobody retried: the old tokens are as refused as they were. */
        await ShouldBeRefusedAsync(connection.Token, nameof(AiConnectorRefusal.ConnectionRevoked));
        oldRefresh = await RefreshShouldGiveNothingMcpServesAsync(oldRefresh);

        /* Connecting once more stays on the new connection and completes the old one in the host. */
        var third = await _h.ConnectAsync(connection.User);
        AuthorizationIdOf(third.AccessToken!).ShouldBe(AuthorizationIdOf(again.AccessToken!), "the new connection is on, so it is reused");
        (await RowsOfAsync(connection.User)).Count.ShouldBe(2);
        await ShouldBeServedAsync(third);
        await ShouldBeRevokedInTheHostAsync(connection.AuthorizationId);
        await ShouldBeARefusedRefreshAsync(oldRefresh);
    }

    /* ---------- A retry under a standing fault ---------- */

    [Theory]
    [InlineData("own", TokensTable, "AuthorizationId")]
    [InlineData("own", AuthorizationsTable, "Id")]
    [InlineData("shared", TokensTable, "AuthorizationId")]
    [InlineData("shared", AuthorizationsTable, "Id")]
    public async Task A_retry_under_a_standing_fault_answers_500_without_detail_and_logs_an_error_and_after_the_fault_a_retry_answers_204_and_completes_the_host_side(string tier, string table, string column)
    {
        var tenant = RevokeTenant(tier);
        var connection = await ConnectAsync(tenant);
        var manager = await ManagerTokenAsync(tenant);
        DateTime? revokedAt;

        await using (await _h.FailUpdatesAsync(null, table, column, connection.AuthorizationId))
        {
            using (var first = await RevokeAsync(manager, connection.RowId))
            {
                first.StatusCode.ShouldBe(HttpStatusCode.NoContent, await first.Content.ReadAsStringAsync());
            }

            revokedAt = await RevokedAtAsync(tenant, connection.RowId);
            revokedAt.ShouldNotBeNull();

            var mark = _h.Host.Logs.Entries.Count;
            using (var retry = await RevokeAsync(manager, connection.RowId))
            {
                await ShouldHaveFailedWithoutDetailAsync(retry);
                var body = await retry.Content.ReadAsStringAsync();
                foreach (var detail in new[] { connection.AuthorizationId.ToString(), connection.RowId.ToString(), "host side", "OpenIddict" })
                {
                    body.ShouldNotContain(detail, Case.Insensitive, "the caller learns nothing of how it failed");
                }
            }

            var error = RevokerErrorsSince(mark).ShouldHaveSingleItem();
            error.Message.ShouldContain(connection.AuthorizationId.ToString(), Case.Insensitive);
            error.Message.ShouldNotContain(connection.User.Email, Case.Insensitive);
            error.Exception.ShouldNotBeNull();

            (await RevokedAtAsync(tenant, connection.RowId)).ShouldBe(revokedAt, "the failed retry changes nothing in the tenant");
            await ShouldBeUntouchedInTheHostAsync(connection);
            await ShouldBeRefusedAsync(connection.Token, nameof(AiConnectorRefusal.ConnectionRevoked));
        }

        var markAfter = _h.Host.Logs.Entries.Count;
        using (var retry = await RevokeAsync(manager, connection.RowId))
        {
            retry.StatusCode.ShouldBe(HttpStatusCode.NoContent, await retry.Content.ReadAsStringAsync());
        }

        RevokerErrorsSince(markAfter).ShouldBeEmpty();
        (await RevokedAtAsync(tenant, connection.RowId)).ShouldBe(revokedAt, "the first switching off stays on record");
        await ShouldBeRevokedInTheHostAsync(connection.AuthorizationId);
        await ShouldBeARefusedRefreshAsync(connection.Token.RefreshToken!);
    }

    /* ---------- A row that is off never comes back ---------- */

    [Fact]
    public void ConnectAgain_moves_a_row_that_is_on_and_throws_on_a_row_that_is_off_changing_nothing()
    {
        var connectedAt = new DateTime(2026, 11, 1, 8, 0, 0);
        var revokedAt = connectedAt.AddHours(1);
        var byUser = Guid.NewGuid();
        var row = new AiConnection(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ClaudeClient, Guid.NewGuid().ToString(), connectedAt);

        row.ConnectAgain(connectedAt.AddMinutes(1));
        row.ConnectedAt.ShouldBe(connectedAt.AddMinutes(1), "the control: a row that is on takes it");

        row.Revoke(revokedAt, byUser);
        Should.Throw<InvalidOperationException>(() => row.ConnectAgain(revokedAt.AddHours(1)));

        row.ConnectedAt.ShouldBe(connectedAt.AddMinutes(1));
        row.RevokedAt.ShouldBe(revokedAt);
        row.RevokedByUserId.ShouldBe(byUser);
    }

    private async Task<bool> TryRecordAsync(TestUser user, string authorizationId)
        => await _h.InScopeAsync(user.TenantId, services => services.GetRequiredService<IAiConnectionRecorder>().TryRecordAsync(user.Id, ClaudeClient, authorizationId), transactional: false);

    [Theory]
    [InlineData("own")]
    [InlineData("shared")]
    public async Task TryRecord_answers_false_on_a_row_that_is_off_also_while_the_host_still_holds_it_as_valid_and_changes_nothing(string tier)
    {
        var tenant = RevokeTenant(tier);
        var (connection, _, _) = await RevokeWithAHostFailureAsync(tenant, TokensTable, "AuthorizationId");
        await ShouldBeUntouchedInTheHostAsync(connection);
        var before = (await RowsOfAsync(connection.User)).ShouldHaveSingleItem();

        (await TryRecordAsync(connection.User, connection.AuthorizationId.ToString())).ShouldBeFalse();

        (await RowsOfAsync(connection.User)).ShouldHaveSingleItem().ShouldBe(before);
    }

    /// <summary>Design 74's window: <c>FindReusableAsync</c> has read the row as on and answered its authorization, a real revoke commits, and only then <c>TryRecordAsync</c> runs.</summary>
    [Theory]
    [InlineData("own")]
    [InlineData("shared")]
    public async Task TryRecord_answers_false_when_a_switch_off_lands_after_FindReusable_read_the_row_as_on(string tier)
    {
        var tenant = RevokeTenant(tier);
        var connection = await ConnectAsync(tenant);
        var manager = await ManagerTokenAsync(tenant);
        var authorization = connection.AuthorizationId.ToString();

        async Task<string?> FindReusableAsync() => await _h.InScopeAsync(tenant, services =>
            services.GetRequiredService<IAiConnectionRecorder>().FindReusableAsync(connection.User.Id, ClaudeClient, [authorization]));

        (await FindReusableAsync()).ShouldBe(authorization, "the control: the row is on and reused");

        using (var response = await RevokeAsync(manager, connection.RowId))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        }

        var before = (await RowsOfAsync(connection.User)).ShouldHaveSingleItem();

        (await TryRecordAsync(connection.User, authorization)).ShouldBeFalse();

        (await RowsOfAsync(connection.User)).ShouldHaveSingleItem().ShouldBe(before);
        (await FindReusableAsync()).ShouldBeNull("a row that is off is never reused");
    }

    /// <summary>
    /// Holds a switching off open in the tenant's database, its <c>RevokedAt</c> written and not committed, while <paramref name="action"/> runs. A plain read still sees the row as on and a write to the row
    /// waits for this transaction, so committing once that write waits lands the switch-off exactly between the action's read and its write. This is what the revoke endpoint's own transaction looks like to PostgreSQL.
    /// </summary>
    private async Task<T> WithASwitchOffLandingBetweenReadAndWriteAsync<T>(Guid tenant, Guid rowId, Func<Task<T>> action)
    {
        await using var connection = new NpgsqlConnection(HostIntegrationFixture.ConnectionStringFor(_h.DatabaseOf(tenant)));
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var switchOff = new NpgsqlCommand($"UPDATE {ConnectionsTable} SET \"RevokedAt\" = now() WHERE \"Id\" = @id", connection, transaction))
        {
            switchOff.Parameters.AddWithValue("id", rowId);
            (await switchOff.ExecuteNonQueryAsync()).ShouldBe(1);
        }

        var running = Task.Run(action);
        var waited = false;
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (!running.IsCompleted && !waited && DateTime.UtcNow < deadline)
            {
                waited = await _h.CountAsync(tenant, "SELECT count(*) FROM pg_stat_activity WHERE @blocker = ANY(pg_blocking_pids(pid))", ("blocker", connection.ProcessID)) > 0;
                if (!waited)
                {
                    await Task.Delay(25);
                }
            }
        }
        finally
        {
            await transaction.CommitAsync();
        }

        var result = await running;
        waited.ShouldBeTrue("the control: the write really waited for the switch-off, so the window was hit");
        return result;
    }

    /// <summary>The window inside <c>TryRecordAsync</c> itself: its read saw the row on, its write lands after the switch-off committed. Design 74: answers false, changes nothing.</summary>
    [Theory]
    [InlineData("own")]
    [InlineData("shared")]
    public async Task TryRecord_answers_false_and_changes_nothing_when_a_switch_off_lands_between_its_read_and_its_write(string tier)
    {
        var tenant = RevokeTenant(tier);
        var connection = await ConnectAsync(tenant);
        var before = (await RowsOfAsync(connection.User)).ShouldHaveSingleItem();
        before.RevokedAt.ShouldBeNull();

        var recorded = await WithASwitchOffLandingBetweenReadAndWriteAsync(tenant, connection.RowId, () => TryRecordAsync(connection.User, connection.AuthorizationId.ToString()));

        var after = (await RowsOfAsync(connection.User)).ShouldHaveSingleItem();
        after.RevokedAt.ShouldNotBeNull("the critical part: the write did not switch the row back on");
        await ShouldBeRefusedAsync(connection.Token, nameof(AiConnectorRefusal.ConnectionRevoked));
        recorded.ShouldSatisfyAllConditions(
            () => recorded.ShouldBeFalse("the row was off when the write landed, so the connect must go on with a new authorization"),
            () => after.ConnectedAt.ShouldBe(before.ConnectedAt, "false changes nothing"));
    }

    /// <summary>The same window over the real connect flow. Design 74: "the connect goes on with a new authorization", so the user ends up with a connection that works.</summary>
    [Theory]
    [InlineData("own")]
    [InlineData("shared")]
    public async Task Connecting_while_a_switch_off_lands_between_read_and_write_goes_on_with_a_new_authorization_that_works(string tier)
    {
        var tenant = RevokeTenant(tier);
        var connection = await ConnectAsync(tenant);

        var again = await WithASwitchOffLandingBetweenReadAndWriteAsync(tenant, connection.RowId, () => _h.ConnectAsync(connection.User));

        (await RowsOfAsync(connection.User)).Single(r => r.Id == connection.RowId).RevokedAt.ShouldNotBeNull("the old row stays off");
        await ShouldBeRefusedAsync(connection.Token, nameof(AiConnectorRefusal.ConnectionRevoked));

        using var mcp = await _h.PostMcpAsync(again.AccessToken, "tools/list");
        var authorization = AuthorizationIdOf(again.AccessToken!);
        authorization.ShouldSatisfyAllConditions(
            () => authorization.ShouldNotBe(connection.AuthorizationId.ToString(), "the row went off before the write, so this connect may not stay on its authorization"),
            () => mcp.StatusCode.ShouldBe(HttpStatusCode.OK, "the user just allowed the tool and gets a connection that works"));
    }

    /* ---------- The grid's history ---------- */

    private async Task<List<JsonElement>> GridRowsAsync(string token, string query = "")
    {
        using var response = await _h.RestAsync(token, HttpMethod.Get, Root + "/connections?StartRow=0&EndRow=200" + query);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("rows").EnumerateArray().ToList();
    }

    [Theory]
    [InlineData("own")]
    [InlineData("shared")]
    public async Task The_grid_shows_the_new_row_Live_first_and_the_old_row_SwitchedOff_for_the_user_and_for_a_manager_and_nothing_to_another_tenant(string tier)
    {
        var (tenant, other, _) = _h.Pair(tier);
        var (connection, manager, _) = await RevokeWithAHostFailureAsync(tenant, TokensTable, "AuthorizationId");
        var again = await _h.ConnectAsync(connection.User);
        var newRow = (await RowsOfAsync(connection.User)).Single(r => r.AuthorizationId == AuthorizationIdOf(again.AccessToken!)).Id;

        /* Oldest first by the column asked for: the old row would lead if Live did not sort before it. */
        var own = await _h.BrowserTokenAsync(connection.User);
        foreach (var query in new[] { string.Empty, "&SortBy=connectedAt&SortDir=asc" })
        {
            var rows = await GridRowsAsync(own, query);
            rows.Select(r => r.GetProperty("id").GetGuid()).ShouldBe([newRow, connection.RowId], query);
            rows[0].GetProperty("status").GetInt32().ShouldBe((int)AiConnectionStatus.Live);
            rows[0].GetProperty("revokedAt").ValueKind.ShouldBe(JsonValueKind.Null);
            rows[1].GetProperty("status").GetInt32().ShouldBe((int)AiConnectionStatus.SwitchedOff);
            rows[1].GetProperty("revokedAt").ValueKind.ShouldNotBe(JsonValueKind.Null);

            var all = await GridRowsAsync(manager, query);
            var ids = all.Select(r => r.GetProperty("id").GetGuid()).ToList();
            ids.IndexOf(newRow).ShouldBeGreaterThanOrEqualTo(0);
            ids.IndexOf(connection.RowId).ShouldBeGreaterThan(ids.IndexOf(newRow));
        }

        await _h.ReadyAsync(other);
        var foreignIds = (await GridRowsAsync(await ManagerTokenAsync(other))).Select(r => r.GetProperty("id").GetGuid()).ToList();
        foreignIds.ShouldNotContain(newRow);
        foreignIds.ShouldNotContain(connection.RowId);
    }

    /* ---------- While the connection is on ---------- */

    [Theory]
    [InlineData("own")]
    [InlineData("shared")]
    public async Task Connecting_again_while_the_connection_is_live_keeps_the_row_and_the_authorization_and_the_old_token_keeps_working(string tier)
    {
        var tenant = RevokeTenant(tier);
        var connection = await ConnectAsync(tenant);
        var before = (await RowsOfAsync(connection.User)).ShouldHaveSingleItem();
        var mark = _h.Host.Logs.Entries.Count;

        var again = await _h.ConnectAsync(connection.User);

        AuthorizationIdOf(again.AccessToken!).ShouldBe(connection.AuthorizationId.ToString(), "the same authorization");
        var after = (await RowsOfAsync(connection.User)).ShouldHaveSingleItem("the same row, no second one");
        after.Id.ShouldBe(connection.RowId);
        after.RevokedAt.ShouldBeNull();
        after.ConnectedAt.ShouldBeGreaterThan(before.ConnectedAt, "it was connected again");

        RevokerErrorsSince(mark).ShouldBeEmpty();
        await ShouldBeUntouchedInTheHostAsync(connection);
        await ShouldBeServedAsync(connection.Token);
        await ShouldBeServedAsync(again);

        var renewed = await _h.RefreshAsync(ClaudeClient, connection.Token.RefreshToken!);
        renewed.Status.ShouldBe(HttpStatusCode.OK, renewed.Body);
        await ShouldBeServedAsync(renewed);
    }

    /* ---------- Isolation ---------- */

    /// <summary>The recorder reads through the tenant filter: in the shared tier only that filter keeps the other tenant's row out, so its find must neither reuse the row nor revoke its authorization.</summary>
    [Theory]
    [InlineData("own")]
    [InlineData("shared")]
    public async Task Connecting_again_after_a_host_side_failure_touches_nothing_of_another_tenant_and_another_tenants_recorder_cannot_see_the_row(string tier)
    {
        var (mine, other, _) = _h.Pair(tier);
        var theirs = await ConnectAsync(other);
        var (connection, _, _) = await RevokeWithAHostFailureAsync(mine, TokensTable, "AuthorizationId");

        var seen = await _h.InScopeAsync(other, services => services.GetRequiredService<IAiConnectionRecorder>()
            .FindReusableAsync(connection.User.Id, ClaudeClient, [connection.AuthorizationId.ToString()]));
        seen.ShouldBeNull();
        await ShouldBeUntouchedInTheHostAsync(connection);

        var again = await _h.ConnectAsync(connection.User);
        await ShouldBeServedAsync(again);

        await ShouldBeUntouchedInTheHostAsync(theirs);
        await ShouldBeServedAsync(theirs.Token);
        (await RevokedAtAsync(other, theirs.RowId)).ShouldBeNull();
        (await RowsOfAsync(theirs.User)).ShouldHaveSingleItem();
    }

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
