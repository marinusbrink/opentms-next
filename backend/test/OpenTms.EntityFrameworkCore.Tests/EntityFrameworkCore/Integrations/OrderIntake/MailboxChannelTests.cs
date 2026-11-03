using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using OpenTms.Features;
using OpenTms.Integrations.Features;
using OpenTms.Integrations.OrderIntake;
using OpenTms.Integrations.OrderIntake.Channels;
using Shouldly;
using Volo.Abp;
using Volo.Abp.DistributedLocking;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Security.Encryption;
using Volo.Abp.Timing;
using Volo.Abp.UI.Navigation.Urls;
using Volo.Abp.Uow;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Integrations.Intake;

using IntegrationsErrorCodes = OpenTms.Integrations.IntegrationsErrorCodes;
using OpenTms.Platform.Features;

/// <summary>A scripted mail provider: what the OAuth endpoints, the change feed and the raw fetch answer.</summary>
public sealed class FakeMailProvider(MailProvider provider) : IMailProviderClient
{
    public MailProvider Provider { get; } = provider;

    public Func<string, MailTokenResult> Exchange { get; set; } = _ => new MailTokenResult("at", "rt-secret-1");

    public Func<string, MailTokenResult> Refresh { get; set; } = _ => new MailTokenResult("at", null);

    public string Address { get; set; } = "Orders@ACME.nl";

    public List<MailChange> Changes { get; set; } = new();

    public Func<MailMessageRef, MailRawMessage?> Fetch { get; set; } = m => new MailRawMessage(Raw(m.ExternalId), DateTime.UtcNow);

    public List<string> Calls { get; } = new();

    public static byte[] Raw(string messageId) => Encoding.UTF8.GetBytes(
        "From: orders@acme.nl\r\nTo: planning@tenant.nl\r\nSubject: Order " + messageId + "\r\nMessage-ID: <" + messageId + "@acme.nl>\r\n" +
        "Content-Type: text/plain; charset=utf-8\r\n\r\nGraag drie pallets ophalen in Utrecht en morgen leveren in Rotterdam, ref " + messageId + ".\r\n");

    public string BuildAuthorizeUrl(string state, string redirectUri) => "https://provider.test/auth?state=" + Uri.EscapeDataString(state);

    public Task<MailTokenResult> ExchangeCodeAsync(string code, string redirectUri, CancellationToken cancellationToken = default)
    {
        Calls.Add("exchange:" + code);
        return Task.FromResult(Exchange(code));
    }

    public Task<MailTokenResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        Calls.Add("refresh:" + refreshToken);
        return Task.FromResult(Refresh(refreshToken));
    }

    public Task<string> GetAccountAddressAsync(string accessToken, CancellationToken cancellationToken = default) => Task.FromResult(Address);

    public Task<IReadOnlyList<MailFolder>> ListFoldersAsync(string accessToken, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<MailFolder>>([new MailFolder("Label_1", "Orders")]);

    /// <summary>The provider's current cursor of a folder (the activation job's call); may throw or change the world meanwhile.</summary>
    public Func<string, string> Cursor { get; set; } = _ => "cursor-0";

    public Task<string> GetCurrentCursorAsync(string accessToken, string folderId, CancellationToken cancellationToken = default)
    {
        Calls.Add("cursor:" + folderId);
        return Task.FromResult(Cursor(folderId));
    }

    public async IAsyncEnumerable<MailChange> GetChangesAsync(string accessToken, string folderId, string cursor, DateTime? lastSyncAt, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Calls.Add("changes:" + cursor);
        foreach (var change in Changes)
        {
            await Task.Yield();
            yield return change;
        }
    }

    public Task<MailRawMessage?> FetchRawAsync(string accessToken, MailMessageRef message, CancellationToken cancellationToken = default)
    {
        Calls.Add("fetch:" + message.ProviderId);
        return Task.FromResult(Fetch(message));
    }
}

public sealed class SettableClock(DateTime now) : IClock
{
    public DateTime Now { get; set; } = now;

    public DateTimeKind Kind => DateTimeKind.Utc;

    public bool SupportsMultipleTimezone => true;

    public DateTime Normalize(DateTime dateTime) => dateTime;

    public DateTime ConvertToUserTime(DateTime dateTime) => dateTime;

    public DateTimeOffset ConvertToUserTime(DateTimeOffset dateTime) => dateTime;

    public DateTime ConvertToUtc(DateTime dateTime) => dateTime;
}

/// <summary>
/// The mailbox channel (design 72 "Connect", "Sync", "Privacy note") on SQLite with a scripted provider: the OAuth callback
/// takes the tenant only from the protected state and refuses a tampered, expired or foreign state and a connection that
/// awaits no consent (no redirect with data); a success stores the refresh token encrypted (never in a DTO) and redirects
/// to the SPA; the token service stores Microsoft's rotated token; the synchroniser stores each message with the cursor
/// that follows it, dedupes, marks invalid_grant as Reconnect without losing the cursor and never lets one failing
/// connection stop the others; and tenant B reaches none of tenant A's connections.
/// Risk class: Critical (OAuth state, tenant isolation, secrets) and High (external integration).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class MailboxChannelTests : OpenTmsEntityFrameworkCoreTestBase
{
    private const string Spa = "http://localhost:5173";
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly ICurrentTenant _currentTenant;
    private readonly FakeMailProvider _gmail = new(MailProvider.Gmail);
    private readonly FakeMailProvider _microsoft = new(MailProvider.Microsoft365);
    private readonly SettableClock _clock = new(DateTime.UtcNow);

    /// <summary>The user who started the connect; the callback completes only in a browser signed in as them (build note 17).</summary>
    private readonly Guid _user = Guid.NewGuid();

    public MailboxChannelTests()
    {
        _currentTenant = GetRequiredService<ICurrentTenant>();
        var urls = GetRequiredService<IOptions<AppUrlOptions>>().Value;
        urls.Applications["MVC"].RootUrl = "http://localhost:44301";
        urls.RedirectAllowedUrls.Clear();
        urls.RedirectAllowedUrls.Add(Spa);
    }

    private IRepository<MailboxConnection, Guid> Connections => GetRequiredService<IRepository<MailboxConnection, Guid>>();

    private IStringEncryptionService Encryption => GetRequiredService<IStringEncryptionService>();

    private MailProviderClientResolver Resolver => new([_gmail, _microsoft]);

    private MailboxTokenService Tokens => new(Encryption, Resolver);

    private MailboxOAuthStateProtector States => GetRequiredService<MailboxOAuthStateProtector>();

    private MailboxOAuthCallbackProcessor Callback(MailboxOAuthStateProtector? states = null) => new(
        states ?? States, _currentTenant, GetRequiredService<IUnitOfWorkManager>(), Connections, Resolver, Tokens, GetRequiredService<MailboxOAuthUrls>(),
        GetRequiredService<IFeatureChecker>(), _clock);

    private MailboxSynchronizer Synchronizer() => new(
        Connections, Resolver, Tokens, GetRequiredService<IMailMessageRecorder>(), GetRequiredService<IUnitOfWorkManager>(),
        GetRequiredService<IAbpDistributedLock>(), GetRequiredService<IFeatureChecker>(), _clock, GetRequiredService<IAsyncQueryableExecuter>());

    /// <summary>The auth server's sign-in of <see cref="_user"/> in tenant A, as the callback controller reads it.</summary>
    private MailboxOAuthCallbackSession Session => new(_user, _tenantA);

    /* The Mailboxes screen sits in the Integrations app (PO 2026-11-03), which is off by default: switch it on too. */
    private static IDisposable IntakeOn() =>
        new FeatureOverrides(TestFeatureValueProvider.Override(IntegrationsFeatures.OrderIntake, "true"), TestFeatureValueProvider.Override(AppFeatures.Integrations, "true"));

    private sealed class FeatureOverrides(params IDisposable[] all) : IDisposable
    {
        public void Dispose()
        {
            foreach (var one in all)
            {
                one.Dispose();
            }
        }
    }

    private Task<MailboxConnection> InsertAsync(Action<MailboxConnection>? arrange = null, MailProvider provider = MailProvider.Gmail)
        => WithUnitOfWorkAsync(async () =>
        {
            var connection = new MailboxConnection(Guid.NewGuid(), _currentTenant.Id, provider, "Orders");
            arrange?.Invoke(connection);
            return await Connections.InsertAsync(connection, autoSave: true);
        });

    /// <summary>A connection that consented, chose its folder and is Active at <paramref name="cursor"/>.</summary>
    private Task<MailboxConnection> ActiveAsync(string cursor = "cursor-0", MailProvider provider = MailProvider.Gmail, string address = "orders@acme.nl", string refreshToken = "rt-1")
        => InsertAsync(c =>
        {
            c.CompleteConsent(address, Encryption.Encrypt(refreshToken)!, DateTime.UtcNow);
            c.ChooseFolder("Label_1", "Orders");
            c.Activate(cursor, DateTime.UtcNow.AddHours(-1));
        }, provider);

    private Task<MailboxConnection> ReadAsync(Guid id) => WithUnitOfWorkAsync(() => Connections.GetAsync(id));

    private Task<List<InboundMessage>> MessagesAsync() => WithUnitOfWorkAsync(() => GetRequiredService<IRepository<InboundMessage, Guid>>().GetListAsync());

    /* ---------- The OAuth callback ---------- */

    [Fact]
    public async Task A_valid_callback_stores_the_encrypted_refresh_token_and_redirects_to_the_spa()
    {
        using var features = IntakeOn();
        Guid id;
        string state;
        using (_currentTenant.Change(_tenantA))
        {
            id = (await InsertAsync()).Id;
            state = States.Protect(_tenantA, id, _user);
        }

        /* The callback is anonymous and arrives without any tenant: the state alone decides. */
        using (_currentTenant.Change(null))
        {
            var result = await Callback().ProcessAsync("the-code", state, null, Session);
            result.RedirectUrl.ShouldBe(Spa + "/integrations/mailboxes?connected=" + id);
        }

        using (_currentTenant.Change(_tenantA))
        {
            var stored = await ReadAsync(id);
            stored.Status.ShouldBe(MailboxStatus.AwaitingFolder);
            stored.EmailAddress.ShouldBe("orders@acme.nl");
            stored.RefreshTokenEncrypted.ShouldNotBeNull();
            stored.RefreshTokenEncrypted.ShouldNotContain("rt-secret-1");
            Encryption.Decrypt(stored.RefreshTokenEncrypted).ShouldBe("rt-secret-1");

            var dto = await GetRequiredService<IMailboxConnectionAppService>().GetAsync(id);
            var json = JsonSerializer.Serialize(dto) + JsonSerializer.Serialize(await GetRequiredService<IMailboxConnectionAppService>().GetListAsync(new MailboxConnectionListRequestDto()));
            json.ShouldNotContain("rt-secret-1");
            json.ShouldNotContain(stored.RefreshTokenEncrypted);
            typeof(MailboxConnectionDto).GetProperties().Concat(typeof(MailboxConnectionRowDto).GetProperties())
                .ShouldNotContain(p => p.Name.Contains("Token", StringComparison.OrdinalIgnoreCase), "the refresh token never leaves the backend");
        }

        _gmail.Calls.ShouldBe(["exchange:the-code"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("tampered")]
    public async Task A_missing_or_tampered_state_is_refused_without_calling_the_provider(string? state)
    {
        using var features = IntakeOn();
        (await Callback().ProcessAsync("code", state, null)).IsRefused.ShouldBeTrue();
        _gmail.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_state_whose_payload_was_altered_is_refused()
    {
        using var features = IntakeOn();
        using var tenant = _currentTenant.Change(_tenantA);
        var id = (await InsertAsync()).Id;
        var state = States.Protect(_tenantA, id, null);
        var chars = state.ToCharArray();
        chars[^10] = chars[^10] == 'x' ? 'y' : 'x';

        (await Callback().ProcessAsync("code", new string(chars), null)).IsRefused.ShouldBeTrue();
        (await ReadAsync(id)).Status.ShouldBe(MailboxStatus.AwaitingConsent);
    }

    [Fact]
    public async Task An_expired_state_is_refused()
    {
        using var features = IntakeOn();
        using var tenant = _currentTenant.Change(_tenantA);
        var id = (await InsertAsync()).Id;
        var clock = new SettableClock(DateTime.UtcNow);
        var states = new MailboxOAuthStateProtector(GetRequiredService<IDataProtectionProvider>(), clock);
        var state = states.Protect(_tenantA, id, null);

        clock.Now = clock.Now.Add(OrderIntakeConsts.OAuthStateLifetime).AddSeconds(1);

        (await Callback(states).ProcessAsync("code", state, null)).IsRefused.ShouldBeTrue();
        _gmail.Calls.ShouldBeEmpty();
        (await ReadAsync(id)).RefreshTokenEncrypted.ShouldBeNull();
    }

    [Fact]
    public async Task A_state_naming_another_tenants_connection_is_refused_and_that_connection_is_untouched()
    {
        using var features = IntakeOn();
        Guid connectionOfB;
        using (_currentTenant.Change(_tenantB))
        {
            connectionOfB = (await InsertAsync()).Id;
        }

        /* A state tenant A could legitimately obtain, pointing at B's connection id. */
        var forged = States.Protect(_tenantA, connectionOfB, null);
        using (_currentTenant.Change(_tenantB))
        {
            (await Callback().ProcessAsync("code", forged, null)).IsRefused.ShouldBeTrue("the connection is looked up in the state's tenant only");
            var untouched = await ReadAsync(connectionOfB);
            untouched.Status.ShouldBe(MailboxStatus.AwaitingConsent);
            untouched.RefreshTokenEncrypted.ShouldBeNull();
        }

        _gmail.Calls.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(MailboxStatus.AwaitingFolder)]
    [InlineData(MailboxStatus.Active)]
    [InlineData(MailboxStatus.Paused)]
    public async Task A_connection_that_awaits_no_consent_is_refused(MailboxStatus status)
    {
        using var features = IntakeOn();
        using var tenant = _currentTenant.Change(_tenantA);
        var connection = await InsertAsync(c =>
        {
            c.CompleteConsent("orders@acme.nl", "enc", DateTime.UtcNow);
            if (status != MailboxStatus.AwaitingFolder)
            {
                c.ChooseFolder("Label_1", "Orders");
                c.Activate("cursor", DateTime.UtcNow);
            }

            if (status == MailboxStatus.Paused)
            {
                c.Pause();
            }
        });

        (await Callback().ProcessAsync("code", States.Protect(_tenantA, connection.Id, _user), null, Session)).IsRefused.ShouldBeTrue();
        (await ReadAsync(connection.Id)).RefreshTokenEncrypted.ShouldBe("enc");
    }

    [Fact]
    public async Task A_state_for_a_deleted_connection_is_refused()
    {
        using var features = IntakeOn();
        using var tenant = _currentTenant.Change(_tenantA);
        var id = (await InsertAsync()).Id;
        var state = States.Protect(_tenantA, id, null);
        await WithUnitOfWorkAsync(() => Connections.DeleteAsync(id, autoSave: true));

        (await Callback().ProcessAsync("code", state, null)).IsRefused.ShouldBeTrue();
    }

    [Fact]
    public async Task Consent_problems_redirect_with_a_code_and_never_with_provider_text()
    {
        using var features = IntakeOn();
        using var tenant = _currentTenant.Change(_tenantA);

        var denied = await InsertAsync();
        (await Callback().ProcessAsync(null, States.Protect(_tenantA, denied.Id, _user), "access_denied", Session)).RedirectUrl
            .ShouldBe(Spa + "/integrations/mailboxes?connectError=consent_denied");

        var noRefresh = await InsertAsync();
        _gmail.Exchange = _ => new MailTokenResult("at", null);
        (await Callback().ProcessAsync("c", States.Protect(_tenantA, noRefresh.Id, _user), null, Session)).RedirectUrl.ShouldEndWith("connectError=no_refresh_token");

        var failing = await InsertAsync();
        _gmail.Exchange = _ => throw new MailProviderException("gmail_token_invalid_client", "Token endpoint said: client acme@x unknown");
        var result = await Callback().ProcessAsync("c", States.Protect(_tenantA, failing.Id, _user), null, Session);
        result.RedirectUrl.ShouldEndWith("connectError=provider_error");
        result.RedirectUrl!.ShouldNotContain("acme");

        (await ReadAsync(failing.Id)).Status.ShouldBe(MailboxStatus.AwaitingConsent);
    }

    [Fact]
    public async Task Without_the_intake_feature_the_callback_completes_nothing()
    {
        using var off = TestFeatureValueProvider.Override(IntegrationsFeatures.OrderIntake, "false");
        using var tenant = _currentTenant.Change(_tenantA);
        var connection = await InsertAsync();

        (await Callback().ProcessAsync("c", States.Protect(_tenantA, connection.Id, _user), null, Session)).RedirectUrl.ShouldEndWith("connectError=feature_disabled");
        _gmail.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_same_mailbox_and_folder_cannot_be_connected_twice()
    {
        using var features = IntakeOn();
        using var tenant = _currentTenant.Change(_tenantA);
        await InsertAsync(c => c.CompleteConsent("orders@acme.nl", "enc", DateTime.UtcNow));
        var second = await InsertAsync();

        (await Callback().ProcessAsync("c", States.Protect(_tenantA, second.Id, _user), null, Session)).RedirectUrl.ShouldEndWith("connectError=already_connected");
    }

    [Fact]
    public async Task A_reconnect_resumes_on_its_folder_and_cursor_and_must_be_the_same_account()
    {
        using var features = IntakeOn();
        using var tenant = _currentTenant.Change(_tenantA);
        var connection = await ActiveAsync(cursor: "cursor-77");
        await WithUnitOfWorkAsync(async () =>
        {
            var c = await Connections.GetAsync(connection.Id);
            c.MarkReconnect("invalid_grant", DateTime.UtcNow);
            await Connections.UpdateAsync(c, autoSave: true);
        });

        _gmail.Address = "someone.else@acme.nl";
        (await Callback().ProcessAsync("c", States.Protect(_tenantA, connection.Id, _user), null, Session)).RedirectUrl.ShouldEndWith("connectError=account_mismatch");
        (await ReadAsync(connection.Id)).Status.ShouldBe(MailboxStatus.Reconnect);

        _gmail.Address = "ORDERS@acme.nl";
        _gmail.Exchange = _ => new MailTokenResult("at", "rt-new");
        (await Callback().ProcessAsync("c", States.Protect(_tenantA, connection.Id, _user), null, Session)).RedirectUrl.ShouldEndWith("connected=" + connection.Id);
        var resumed = await ReadAsync(connection.Id);
        resumed.Status.ShouldBe(MailboxStatus.Active);
        resumed.SyncCursor.ShouldBe("cursor-77", "nothing is lost");
        Encryption.Decrypt(resumed.RefreshTokenEncrypted!).ShouldBe("rt-new");
    }

    /* ---------- Tokens ---------- */

    [Fact]
    public async Task A_rotated_microsoft_refresh_token_is_stored_encrypted()
    {
        using var tenant = _currentTenant.Change(_tenantA);
        var connection = await ActiveAsync(provider: MailProvider.Microsoft365, refreshToken: "rt-1");
        _microsoft.Refresh = _ => new MailTokenResult("at-2", "rt-2");

        (await Tokens.GetAccessTokenAsync(connection)).ShouldBe("at-2");

        _microsoft.Calls.ShouldBe(["refresh:rt-1"]);
        Encryption.Decrypt(connection.RefreshTokenEncrypted!).ShouldBe("rt-2");
    }

    [Fact]
    public async Task A_token_that_no_longer_decrypts_requires_a_reconnect()
    {
        using var tenant = _currentTenant.Change(_tenantA);
        var connection = await InsertAsync(c => c.CompleteConsent("orders@acme.nl", "bm90IGVuY3J5cHRlZCB3aXRoIG91ciBwYXNz", DateTime.UtcNow));

        await Should.ThrowAsync<Exception>(() => Tokens.GetAccessTokenAsync(connection));
        _gmail.Calls.ShouldBeEmpty();
    }

    /* ---------- Synchronisation ---------- */

    [Fact]
    public async Task A_sync_stores_new_mail_advances_the_cursor_and_a_second_sync_stores_nothing_twice()
    {
        using var features = IntakeOn();
        using var tenant = _currentTenant.Change(_tenantA);
        var connection = await ActiveAsync();
        _gmail.Changes =
        [
            new MailChange(new MailMessageRef("m1", "m1"), null), MailChange.Checkpoint("c1"),
            new MailChange(new MailMessageRef("m2", "m2"), null), new MailChange(new MailMessageRef("m2", "m2"), null), MailChange.Checkpoint("c2")
        ];

        (await Synchronizer().SyncAsync(connection.Id)).ShouldBe(2);

        var stored = await ReadAsync(connection.Id);
        stored.SyncCursor.ShouldBe("c2");
        stored.LastErrorCode.ShouldBeNull();
        var messages = await MessagesAsync();
        messages.Select(m => m.ExternalId).ShouldBe(["m1", "m2"], ignoreOrder: true);
        messages.ShouldAllBe(m => m.Channel == InboundChannel.Gmail && m.SourceId == connection.Id && m.From == "orders@acme.nl");
        _gmail.Calls.Count(c => c == "fetch:m2").ShouldBe(1, "a message listed twice in one sync is fetched once");

        /* Gmail delivers the same mail again (label removed and added): stored once (design 72 edge case). */
        (await Synchronizer().SyncAsync(connection.Id)).ShouldBe(0);
        (await MessagesAsync()).Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_failure_halfway_keeps_the_cursor_after_the_last_stored_message_and_records_the_error()
    {
        using var features = IntakeOn();
        using var tenant = _currentTenant.Change(_tenantA);
        var connection = await ActiveAsync();
        _gmail.Changes =
        [
            new MailChange(new MailMessageRef("m1", "m1"), null), MailChange.Checkpoint("c1"),
            new MailChange(new MailMessageRef("m2", "m2"), null), MailChange.Checkpoint("c2")
        ];
        _gmail.Fetch = m => m.ProviderId == "m2" ? throw new MailProviderException("gmail_http_500", "Gmail answered HTTP 500.") : new MailRawMessage(FakeMailProvider.Raw(m.ExternalId), DateTime.UtcNow);

        (await Synchronizer().SyncAsync(connection.Id)).ShouldBe(0, "SyncAsync never throws; the stored count is lost with the failure");

        var stored = await ReadAsync(connection.Id);
        stored.SyncCursor.ShouldBe("c1", "the cursor advanced with the stored message and stops before the failed one");
        stored.LastErrorCode.ShouldBe("gmail_http_500");
        stored.Status.ShouldBe(MailboxStatus.Active, "retried next period");
        (await MessagesAsync()).Select(m => m.ExternalId).ShouldBe(["m1"]);
    }

    [Fact]
    public async Task Invalid_grant_puts_the_connection_in_reconnect_and_keeps_its_cursor()
    {
        using var features = IntakeOn();
        using var tenant = _currentTenant.Change(_tenantA);
        var connection = await ActiveAsync(cursor: "cursor-9");
        _gmail.Refresh = _ => throw new MailboxReconnectRequiredException(MailProvider.Gmail);

        (await Synchronizer().SyncAsync(connection.Id)).ShouldBe(0);

        var stored = await ReadAsync(connection.Id);
        stored.Status.ShouldBe(MailboxStatus.Reconnect);
        stored.SyncCursor.ShouldBe("cursor-9");
        stored.LastErrorCode.ShouldBe("invalid_grant");
    }

    [Fact]
    public async Task One_failing_connection_never_stops_the_others_and_a_rotated_token_is_kept_whatever_follows()
    {
        using var features = IntakeOn();
        using var tenant = _currentTenant.Change(_tenantA);
        var broken = await ActiveAsync(provider: MailProvider.Microsoft365, address: "a@acme.nl");
        var healthy = await ActiveAsync(provider: MailProvider.Gmail, address: "b@acme.nl");
        await WithUnitOfWorkAsync(async () =>
        {
            /* The broken one is visited first (oldest LastSyncAt). */
            var b = await Connections.GetAsync(broken.Id);
            b.RecordSync("cursor-0", DateTime.UtcNow.AddDays(-1));
            await Connections.UpdateAsync(b, autoSave: true);
        });
        _microsoft.Refresh = _ => new MailTokenResult("at", "rt-rotated");
        _microsoft.Changes = [new MailChange(new MailMessageRef("x1", "x1"), null), MailChange.Checkpoint("cx")];
        _microsoft.Fetch = _ => throw new InvalidOperationException("unexpected");
        _gmail.Changes = [new MailChange(new MailMessageRef("g1", "g1"), null), MailChange.Checkpoint("cg")];

        (await Synchronizer().SyncCurrentTenantAsync()).ShouldBe(1);

        var b2 = await ReadAsync(broken.Id);
        b2.LastErrorCode.ShouldBe("sync_failed");
        Encryption.Decrypt(b2.RefreshTokenEncrypted!).ShouldBe("rt-rotated", "Microsoft rotated the token: it was committed at once");
        (await ReadAsync(healthy.Id)).SyncCursor.ShouldBe("cg");
        (await MessagesAsync()).Select(m => m.ExternalId).ShouldBe(["g1"]);
    }

    [Fact]
    public async Task Paused_connections_and_tenants_without_the_feature_are_not_synchronised()
    {
        using var tenant = _currentTenant.Change(_tenantA);
        var connection = await ActiveAsync();
        _gmail.Changes = [new MailChange(new MailMessageRef("m1", "m1"), null), MailChange.Checkpoint("c1")];

        using (TestFeatureValueProvider.Override(IntegrationsFeatures.OrderIntake, "false"))
        {
            (await Synchronizer().SyncCurrentTenantAsync()).ShouldBe(0);
        }

        using (IntakeOn())
        {
            await WithUnitOfWorkAsync(async () =>
            {
                var c = await Connections.GetAsync(connection.Id);
                c.Pause();
                await Connections.UpdateAsync(c, autoSave: true);
            });
            (await Synchronizer().SyncCurrentTenantAsync()).ShouldBe(0);
        }

        _gmail.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_sync_in_tenant_B_never_touches_tenant_As_connection()
    {
        using var features = IntakeOn();
        Guid id;
        using (_currentTenant.Change(_tenantA))
        {
            id = (await ActiveAsync()).Id;
        }

        _gmail.Changes = [new MailChange(new MailMessageRef("m1", "m1"), null), MailChange.Checkpoint("c1")];
        using (_currentTenant.Change(_tenantB))
        {
            (await Synchronizer().SyncCurrentTenantAsync()).ShouldBe(0);
            (await Synchronizer().SyncAsync(id)).ShouldBe(0, "by id too: B's filter does not see A's row");
        }

        _gmail.Calls.ShouldBeEmpty();
    }

    /* ---------- The mailbox service ---------- */

    [Fact]
    public async Task Tenant_B_reaches_none_of_tenant_As_mailbox_connections()
    {
        using var features = IntakeOn();
        MailboxConnection a;
        using (_currentTenant.Change(_tenantA))
        {
            a = await ActiveAsync();
        }

        using (_currentTenant.Change(_tenantB))
        {
            var service = GetRequiredService<IMailboxConnectionAppService>();
            (await service.GetListAsync(new MailboxConnectionListRequestDto())).TotalCount.ShouldBe(0);
            (await service.GetListAsync(new MailboxConnectionListRequestDto { WildcardSearch = "acme" })).Rows.ShouldBeEmpty();
            await Should.ThrowAsync<EntityNotFoundException>(() => service.GetAsync(a.Id));
            await Should.ThrowAsync<EntityNotFoundException>(() => service.GetFoldersAsync(a.Id));
            await Should.ThrowAsync<EntityNotFoundException>(() => service.ReconnectAsync(a.Id));
            await Should.ThrowAsync<EntityNotFoundException>(() => service.PauseAsync(a.Id));
            await Should.ThrowAsync<EntityNotFoundException>(() => service.ResumeAsync(a.Id));
            await Should.ThrowAsync<EntityNotFoundException>(() => service.UpdateAsync(a.Id, new MailboxConnectionUpdateDto { DisplayName = "Stolen", ConcurrencyStamp = a.ConcurrencyStamp }));
            await service.DeleteAsync(a.Id); /* ABP's delete by id is silent for a row it cannot see */
        }

        using (_currentTenant.Change(_tenantA))
        {
            (await ReadAsync(a.Id)).DisplayName.ShouldBe("Orders", "B's delete did not reach A's row");
        }
    }

    [Fact]
    public async Task Connecting_an_unconfigured_provider_stores_nothing()
    {
        using var features = IntakeOn();
        using var tenant = _currentTenant.Change(_tenantA);
        var options = GetRequiredService<IOptions<MailOAuthOptions>>().Value;
        options.Google = new MailOAuthClientOptions();

        (await Should.ThrowAsync<BusinessException>(() => GetRequiredService<IMailboxConnectionAppService>().CreateAsync(
            new MailboxConnectionCreateDto { Provider = MailProvider.Gmail, DisplayName = "Orders" }))).Code.ShouldBe(IntegrationsErrorCodes.ProviderNotConfigured);
        (await WithUnitOfWorkAsync(() => Connections.CountAsync())).ShouldBe(0);
    }

    [Fact]
    public async Task Connecting_returns_the_providers_authorize_url_with_a_state_for_this_tenant_and_connection()
    {
        using var features = IntakeOn();
        using var tenant = _currentTenant.Change(_tenantA);
        var options = GetRequiredService<IOptions<MailOAuthOptions>>().Value;
        options.Google = new MailOAuthClientOptions { ClientId = "gid", ClientSecret = "gsecret" };

        var created = await GetRequiredService<IMailboxConnectionAppService>().CreateAsync(new MailboxConnectionCreateDto { Provider = MailProvider.Gmail, DisplayName = "Orders" });

        var query = System.Web.HttpUtility.ParseQueryString(new Uri(created.AuthorizeUrl).Query);
        query["redirect_uri"].ShouldBe("http://localhost:44301/api/integrations/mailbox-oauth/callback");
        var state = States.Unprotect(query["state"]).ShouldNotBeNull();
        (state.TenantId, state.ConnectionId).ShouldBe(((Guid?)_tenantA, created.Id));
        (await ReadAsync(created.Id)).Status.ShouldBe(MailboxStatus.AwaitingConsent);
    }
}
