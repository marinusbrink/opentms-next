using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using OpenTms.Features;
using OpenTms.Integrations.Features;
using OpenTms.Integrations.OrderIntake;
using OpenTms.Integrations.OrderIntake.Channels;
using OpenTms.TestDoubles;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.DistributedLocking;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Security.Encryption;
using Volo.Abp.Tracing;
using Volo.Abp.UI.Navigation.Urls;
using Volo.Abp.Uow;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Integrations.Intake;

using IntegrationsErrorCodes = OpenTms.Integrations.IntegrationsErrorCodes;
using OpenTms.Platform.Features;

/// <summary>
/// The mailbox fixes of the design 72 review on SQLite with scripted providers: the OAuth consent completes only in the
/// browser of the user and tenant that started it (build note 17, review M7: no session, another user, another tenant, the
/// host, a state without user → <c>session_required</c> before any code exchange); choosing a folder sets the connection
/// Activating and enqueues <see cref="MailboxActivationJob"/> after commit (build note 18), which takes the cursor and
/// activates only a connection still Activating on the same folder, puts it in Reconnect on invalid_grant, keeps it
/// Activating and rethrows on a transient failure, waits for the sync's lock, runs in its own tenant only and is idempotent;
/// the sync skips Activating connections; deleting forgets the refresh token and cursor (build note 21, review M6); a
/// missing <c>App:SelfUrl</c> is a configuration gap, never a 500 (review minor 6).
/// Risk class: Critical (OAuth binding, tenant isolation, secrets) and High (external integration, jobs).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class MailboxReviewFixesTests : OpenTmsEntityFrameworkCoreTestBase
{
    private const string Spa = "http://localhost:5173";
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly Guid _user = Guid.NewGuid();
    private readonly ICurrentTenant _currentTenant;
    private readonly FakeMailProvider _gmail = new(MailProvider.Gmail);
    private readonly FakeMailProvider _microsoft = new(MailProvider.Microsoft365);
    private readonly SettableClock _clock = new(DateTime.UtcNow);

    public MailboxReviewFixesTests()
    {
        _currentTenant = GetRequiredService<ICurrentTenant>();
        var urls = GetRequiredService<IOptions<AppUrlOptions>>().Value;
        urls.Applications["MVC"].RootUrl = "http://localhost:44301";
        urls.RedirectAllowedUrls.Clear();
        urls.RedirectAllowedUrls.Add(Spa);
        GetRequiredService<TestMailProviders>().Clients = [_gmail, _microsoft];
        GetRequiredService<IOptions<MailOAuthOptions>>().Value.Google = new MailOAuthClientOptions { ClientId = "gid", ClientSecret = "gsecret" };
    }

    private IRepository<MailboxConnection, Guid> Connections => GetRequiredService<IRepository<MailboxConnection, Guid>>();

    private IStringEncryptionService Encryption => GetRequiredService<IStringEncryptionService>();

    private MailProviderClientResolver Resolver => new([_gmail, _microsoft]);

    private MailboxTokenService Tokens => new(Encryption, Resolver);

    private MailboxOAuthStateProtector States => GetRequiredService<MailboxOAuthStateProtector>();

    private RecordingBackgroundJobManager Jobs => GetRequiredService<RecordingBackgroundJobManager>();

    private IMailboxConnectionAppService Service => GetRequiredService<IMailboxConnectionAppService>();

    private MailboxOAuthCallbackProcessor Callback() => new(
        States, _currentTenant, GetRequiredService<IUnitOfWorkManager>(), Connections, Resolver, Tokens, GetRequiredService<MailboxOAuthUrls>(),
        GetRequiredService<IFeatureChecker>(), _clock);

    private MailboxActivationJob Job(IAbpDistributedLock? distributedLock = null) => new(
        Connections, Resolver, Tokens, distributedLock ?? GetRequiredService<IAbpDistributedLock>(), GetRequiredService<IUnitOfWorkManager>(),
        GetRequiredService<IFeatureChecker>(), _currentTenant, GetRequiredService<ICorrelationIdProvider>(), _clock);

    private MailboxSynchronizer Synchronizer() => new(
        Connections, Resolver, Tokens, GetRequiredService<IMailMessageRecorder>(), GetRequiredService<IUnitOfWorkManager>(),
        GetRequiredService<IAbpDistributedLock>(), GetRequiredService<IFeatureChecker>(), _clock, GetRequiredService<IAsyncQueryableExecuter>());

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

    private Task<MailboxConnection> ConsentedAsync(MailProvider provider = MailProvider.Gmail, string refreshToken = "rt-1")
        => InsertAsync(c => c.CompleteConsent("orders@acme.nl", Encryption.Encrypt(refreshToken)!, DateTime.UtcNow), provider);

    private Task<MailboxConnection> ActivatingAsync(MailProvider provider = MailProvider.Gmail, string folder = "Label_1")
        => InsertAsync(c =>
        {
            c.CompleteConsent("orders@acme.nl", Encryption.Encrypt("rt-1")!, DateTime.UtcNow);
            c.ChooseFolder(folder, "Orders");
        }, provider);

    private Task<MailboxConnection> ReadAsync(Guid id) => WithUnitOfWorkAsync(() => Connections.GetAsync(id));

    private Task ChangeAsync(Guid id, Action<MailboxConnection> change) => WithUnitOfWorkAsync(async () =>
    {
        var c = await Connections.GetAsync(id);
        change(c);
        await Connections.UpdateAsync(c, autoSave: true);
    });

    private MailboxActivationArgs Args(Guid connectionId, Guid? tenant = null) => new() { TenantId = tenant ?? _tenantA, ConnectionId = connectionId };

    /* ---------- The OAuth consent is bound to the signed-in user (build note 17) ---------- */

    public static IEnumerable<object?[]> ForeignSessions()
    {
        yield return ["none"];
        yield return ["other user"];
        yield return ["other tenant"];
        yield return ["host"];
    }

    [Theory]
    [MemberData(nameof(ForeignSessions))]
    public async Task A_callback_without_the_starting_users_session_is_refused_before_any_code_exchange(string session)
    {
        using var features = IntakeOn();
        using var tenant = _currentTenant.Change(_tenantA);
        var connection = await InsertAsync();
        var callbackSession = session switch
        {
            "none" => null,
            "other user" => new MailboxOAuthCallbackSession(Guid.NewGuid(), _tenantA),
            "other tenant" => new MailboxOAuthCallbackSession(_user, _tenantB),
            _ => new MailboxOAuthCallbackSession(_user, null)
        };

        var result = await Callback().ProcessAsync("the-code", States.Protect(_tenantA, connection.Id, _user), null, callbackSession);

        result.RedirectUrl.ShouldBe(Spa + "/integrations/mailboxes?connectError=session_required");
        _gmail.Calls.ShouldBeEmpty("refused before the code is exchanged");
        var stored = await ReadAsync(connection.Id);
        stored.Status.ShouldBe(MailboxStatus.AwaitingConsent);
        stored.RefreshTokenEncrypted.ShouldBeNull();
        stored.EmailAddress.ShouldBeNullOrEmpty();
    }

    [Fact]
    public async Task A_state_that_recorded_no_user_never_completes_even_with_a_session()
    {
        using var features = IntakeOn();
        using var tenant = _currentTenant.Change(_tenantA);
        var connection = await InsertAsync();

        (await Callback().ProcessAsync("the-code", States.Protect(_tenantA, connection.Id, null), null, new MailboxOAuthCallbackSession(_user, _tenantA)))
            .RedirectUrl.ShouldEndWith("connectError=session_required");
        _gmail.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_session_check_comes_before_the_feature_and_consent_checks_and_a_matching_session_completes()
    {
        using var tenant = _currentTenant.Change(_tenantA);
        var connection = await InsertAsync();
        using (TestFeatureValueProvider.Override(IntegrationsFeatures.OrderIntake, "false"))
        {
            (await Callback().ProcessAsync(null, States.Protect(_tenantA, connection.Id, _user), "access_denied", null))
                .RedirectUrl.ShouldEndWith("connectError=session_required", customMessage: "nothing about the tenant is revealed to a foreign browser");
        }

        using (IntakeOn())
        {
            (await Callback().ProcessAsync("the-code", States.Protect(_tenantA, connection.Id, _user), null, new MailboxOAuthCallbackSession(_user, _tenantA)))
                .RedirectUrl.ShouldBe(Spa + "/integrations/mailboxes?connected=" + connection.Id);
        }

        (await ReadAsync(connection.Id)).Status.ShouldBe(MailboxStatus.AwaitingFolder);
    }

    [Fact]
    public async Task An_invalid_state_stays_a_plain_refusal_whatever_the_session()
    {
        using var features = IntakeOn();
        (await Callback().ProcessAsync("code", "tampered", null, null)).IsRefused.ShouldBeTrue();
        (await Callback().ProcessAsync("code", "tampered", null, new MailboxOAuthCallbackSession(_user, _tenantA))).IsRefused.ShouldBeTrue();
        _gmail.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_matching_session_completes_in_whichever_tenant_the_state_names()
    {
        using var features = IntakeOn();
        using var tenant = _currentTenant.Change(_tenantB);
        var connection = await InsertAsync();

        (await Callback().ProcessAsync("the-code", States.Protect(_tenantB, connection.Id, _user), null, new MailboxOAuthCallbackSession(_user, _tenantB)))
            .RedirectUrl.ShouldEndWith("connected=" + connection.Id);
    }

    /* ---------- A missing App:SelfUrl (review minor 6) ---------- */

    [Theory]
    [InlineData("empty")]
    [InlineData("missing")]
    public async Task Without_a_self_url_the_callback_answers_not_configured_and_exchanges_nothing(string how)
    {
        using var features = IntakeOn();
        using var tenant = _currentTenant.Change(_tenantA);
        var connection = await InsertAsync();
        var urls = GetRequiredService<IOptions<AppUrlOptions>>().Value;
        if (how == "empty")
        {
            urls.Applications["MVC"].RootUrl = "";
        }
        else
        {
            urls.Applications["MVC"].RootUrl = null;
        }

        var result = await Callback().ProcessAsync("the-code", States.Protect(_tenantA, connection.Id, _user), null, new MailboxOAuthCallbackSession(_user, _tenantA));

        result.RedirectUrl.ShouldBe(Spa + "/integrations/mailboxes?connectError=not_configured");
        _gmail.Calls.ShouldBeEmpty();
        (await ReadAsync(connection.Id)).Status.ShouldBe(MailboxStatus.AwaitingConsent);
    }

    [Fact]
    public async Task Without_a_self_url_connecting_and_reconnecting_answer_provider_not_configured()
    {
        using var features = IntakeOn();
        using var tenant = _currentTenant.Change(_tenantA);
        var reconnecting = await ConsentedAsync();
        await ChangeAsync(reconnecting.Id, c => c.MarkReconnect("invalid_grant", DateTime.UtcNow));
        GetRequiredService<IOptions<AppUrlOptions>>().Value.Applications["MVC"].RootUrl = "  ";

        (await Should.ThrowAsync<BusinessException>(() => Service.CreateAsync(new MailboxConnectionCreateDto { Provider = MailProvider.Gmail, DisplayName = "New" })))
            .Code.ShouldBe(IntegrationsErrorCodes.ProviderNotConfigured);
        (await Should.ThrowAsync<BusinessException>(() => Service.ReconnectAsync(reconnecting.Id)))
            .Code.ShouldBe(IntegrationsErrorCodes.ProviderNotConfigured);

        (await WithUnitOfWorkAsync(() => Connections.CountAsync())).ShouldBe(1, "the failed connect stored nothing");
        IntegrationsErrorCodes.ConflictCodes.ShouldContain(IntegrationsErrorCodes.ProviderNotConfigured, "travels as 409 (see the host's mapping test)");
    }

    /* ---------- Choosing a folder: Activating, the job after commit (build note 18) ---------- */

    [Fact]
    public async Task Choosing_a_folder_sets_the_connection_activating_and_enqueues_the_activation_without_taking_the_cursor()
    {
        using var features = IntakeOn();
        using var tenant = _currentTenant.Change(_tenantA);
        var connection = await ConsentedAsync();
        Jobs.Clear();

        var dto = await Service.UpdateAsync(connection.Id, new MailboxConnectionUpdateDto { DisplayName = "Orders", FolderId = "Label_1", ConcurrencyStamp = connection.ConcurrencyStamp });

        dto.Status.ShouldBe(MailboxStatus.Activating);
        var stored = await ReadAsync(connection.Id);
        (stored.Status, stored.FolderId, stored.FolderName, stored.SyncCursor).ShouldBe((MailboxStatus.Activating, "Label_1", "Orders", (string?)null));
        _gmail.Calls.ShouldNotContain(c => c.StartsWith("cursor:"), "the request never takes the cursor (Graph pages the whole folder)");
        var job = Jobs.Of<MailboxActivationArgs>().ShouldHaveSingleItem("enqueued once the unit of work committed");
        (job.TenantId, job.ConnectionId).ShouldBe(((Guid?)_tenantA, connection.Id));
    }

    [Fact]
    public async Task An_unknown_folder_changes_nothing_and_enqueues_nothing()
    {
        using var features = IntakeOn();
        using var tenant = _currentTenant.Change(_tenantA);
        var connection = await ConsentedAsync();
        Jobs.Clear();

        await Should.ThrowAsync<UserFriendlyException>(() => Service.UpdateAsync(connection.Id,
            new MailboxConnectionUpdateDto { DisplayName = "Orders", FolderId = "Label_404", ConcurrencyStamp = connection.ConcurrencyStamp }));

        (await ReadAsync(connection.Id)).Status.ShouldBe(MailboxStatus.AwaitingFolder);
        Jobs.Of<MailboxActivationArgs>().ShouldBeEmpty();
    }

    [Fact]
    public async Task The_sync_skips_an_activating_connection()
    {
        using var features = IntakeOn();
        using var tenant = _currentTenant.Change(_tenantA);
        var connection = await ActivatingAsync();
        _gmail.Changes = [new MailChange(new MailMessageRef("m1", "m1"), null), MailChange.Checkpoint("c1")];

        (await Synchronizer().SyncCurrentTenantAsync()).ShouldBe(0);
        (await Synchronizer().SyncAsync(connection.Id)).ShouldBe(0);

        _gmail.Calls.ShouldBeEmpty();
        (await ReadAsync(connection.Id)).Status.ShouldBe(MailboxStatus.Activating);
    }

    /* ---------- MailboxActivationJob (review M3) ---------- */

    [Fact]
    public async Task The_job_takes_the_providers_cursor_and_activates_the_connection_once()
    {
        using var features = IntakeOn();
        using var tenant = _currentTenant.Change(_tenantA);
        var connection = await ActivatingAsync();
        await ChangeAsync(connection.Id, c => c.RecordError("gmail_http_500", DateTime.UtcNow));
        _gmail.Cursor = folder => "history-" + folder;

        await Job().ExecuteAsync(Args(connection.Id));

        var active = await ReadAsync(connection.Id);
        (active.Status, active.SyncCursor, active.FolderId).ShouldBe((MailboxStatus.Active, "history-Label_1", "Label_1"));
        active.LastErrorCode.ShouldBeNull("activation clears the last error");
        _gmail.Calls.ShouldBe(["refresh:rt-1", "cursor:Label_1"]);

        /* Run twice (Hangfire's at-least-once): one effect. */
        _gmail.Cursor = _ => "history-later";
        await Job().ExecuteAsync(Args(connection.Id));
        (await ReadAsync(connection.Id)).SyncCursor.ShouldBe("history-Label_1");
        _gmail.Calls.Count.ShouldBe(2, "a connection that is no longer Activating calls no provider");
    }

    [Fact]
    public async Task A_connection_moved_to_another_folder_meanwhile_is_not_activated_with_the_old_cursor()
    {
        using var features = IntakeOn();
        using var tenant = _currentTenant.Change(_tenantA);
        var connection = await ActivatingAsync(folder: "Label_1");
        _gmail.Cursor = folder =>
        {
            /* While the provider pages the old folder, the user picks another one. */
            ChangeAsync(connection.Id, c => c.ChooseFolder("Label_2", "Other")).GetAwaiter().GetResult();
            return "cursor-of-" + folder;
        };

        await Job().ExecuteAsync(Args(connection.Id));

        var stored = await ReadAsync(connection.Id);
        (stored.Status, stored.FolderId, stored.SyncCursor).ShouldBe((MailboxStatus.Activating, "Label_2", (string?)null), "the new folder's own job activates it");
    }

    [Theory]
    [InlineData(MailboxStatus.AwaitingFolder)]
    [InlineData(MailboxStatus.Active)]
    [InlineData(MailboxStatus.Paused)]
    [InlineData(MailboxStatus.Reconnect)]
    public async Task A_connection_that_is_not_activating_is_left_alone(MailboxStatus status)
    {
        using var features = IntakeOn();
        using var tenant = _currentTenant.Change(_tenantA);
        var connection = await ConsentedAsync();
        if (status != MailboxStatus.AwaitingFolder)
        {
            await ChangeAsync(connection.Id, c =>
            {
                c.ChooseFolder("Label_1", "Orders");
                c.Activate("cursor-old", DateTime.UtcNow);
                if (status == MailboxStatus.Paused)
                {
                    c.Pause();
                }

                if (status == MailboxStatus.Reconnect)
                {
                    c.MarkReconnect("invalid_grant", DateTime.UtcNow);
                }
            });
        }

        await Job().ExecuteAsync(Args(connection.Id));

        _gmail.Calls.ShouldBeEmpty();
        (await ReadAsync(connection.Id)).Status.ShouldBe(status);
    }

    [Fact]
    public async Task A_deleted_connection_is_left_alone()
    {
        using var features = IntakeOn();
        using var tenant = _currentTenant.Change(_tenantA);
        var connection = await ActivatingAsync();
        await WithUnitOfWorkAsync(() => Connections.DeleteAsync(connection.Id, autoSave: true));

        await Job().ExecuteAsync(Args(connection.Id));

        _gmail.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Invalid_grant_puts_the_connection_in_reconnect_without_a_retry()
    {
        using var features = IntakeOn();
        using var tenant = _currentTenant.Change(_tenantA);
        var connection = await ActivatingAsync();
        _gmail.Refresh = _ => throw new MailboxReconnectRequiredException(MailProvider.Gmail);

        await Job().ExecuteAsync(Args(connection.Id)); /* does not throw: Hangfire must not retry a revoked consent */

        var stored = await ReadAsync(connection.Id);
        (stored.Status, stored.LastErrorCode, stored.FolderId).ShouldBe((MailboxStatus.Reconnect, "invalid_grant", "Label_1"));
        _gmail.Calls.ShouldNotContain(c => c.StartsWith("cursor:"));
    }

    [Fact]
    public async Task A_transient_failure_keeps_the_connection_activating_records_the_error_and_rethrows_for_a_retry()
    {
        using var features = IntakeOn();
        using var tenant = _currentTenant.Change(_tenantA);
        var connection = await ActivatingAsync(MailProvider.Microsoft365);
        _microsoft.Refresh = _ => new MailTokenResult("at", "rt-rotated");
        _microsoft.Cursor = _ => throw new MailProviderException("graph_http_503", "Service Unavailable");

        (await Should.ThrowAsync<MailProviderException>(() => Job().ExecuteAsync(Args(connection.Id)))).ErrorCode.ShouldBe("graph_http_503");

        var stored = await ReadAsync(connection.Id);
        (stored.Status, stored.LastErrorCode, stored.SyncCursor).ShouldBe((MailboxStatus.Activating, "graph_http_503", (string?)null));
        Encryption.Decrypt(stored.RefreshTokenEncrypted!).ShouldBe("rt-rotated", "Microsoft's rotated token was committed before the cursor call");

        /* Hangfire's retry. */
        _microsoft.Cursor = _ => "delta-1";
        await Job().ExecuteAsync(Args(connection.Id));
        var active = await ReadAsync(connection.Id);
        (active.Status, active.SyncCursor, active.LastErrorCode).ShouldBe((MailboxStatus.Active, "delta-1", (string?)null));
    }

    [Fact]
    public async Task An_unexpected_failure_is_recorded_as_activation_failed_and_rethrown()
    {
        using var features = IntakeOn();
        using var tenant = _currentTenant.Change(_tenantA);
        var connection = await ActivatingAsync();
        _gmail.Cursor = _ => throw new InvalidOperationException("boom with acme@x details");

        await Should.ThrowAsync<InvalidOperationException>(() => Job().ExecuteAsync(Args(connection.Id)));

        var stored = await ReadAsync(connection.Id);
        (stored.Status, stored.LastErrorCode).ShouldBe((MailboxStatus.Activating, "activation_failed"));
    }

    [Fact]
    public async Task A_connection_busy_with_a_sync_is_retried_later_and_untouched()
    {
        using var features = IntakeOn();
        using var tenant = _currentTenant.Change(_tenantA);
        var connection = await ActivatingAsync();
        var busy = new BusyLock();

        await Should.ThrowAsync<MailboxActivationRetryException>(() => Job(busy).ExecuteAsync(Args(connection.Id)));

        busy.Requested.ShouldBe(["mailbox-sync:" + connection.Id], "the sync's own lock");
        _gmail.Calls.ShouldBeEmpty();
        (await ReadAsync(connection.Id)).Status.ShouldBe(MailboxStatus.Activating);
    }

    [Fact]
    public async Task The_job_does_nothing_while_the_intake_feature_is_off()
    {
        using var tenant = _currentTenant.Change(_tenantA);
        var connection = await ActivatingAsync();

        using (TestFeatureValueProvider.Override(IntegrationsFeatures.OrderIntake, "false"))
        {
            await Job().ExecuteAsync(Args(connection.Id));
        }

        _gmail.Calls.ShouldBeEmpty();
        (await ReadAsync(connection.Id)).Status.ShouldBe(MailboxStatus.Activating);
    }

    [Fact]
    public async Task A_job_for_tenant_B_never_touches_tenant_As_connection()
    {
        using var features = IntakeOn();
        Guid id;
        using (_currentTenant.Change(_tenantA))
        {
            id = (await ActivatingAsync()).Id;
        }

        using (_currentTenant.Change(null))
        {
            await Job().ExecuteAsync(Args(id, tenant: _tenantB));
        }

        _gmail.Calls.ShouldBeEmpty();
        using (_currentTenant.Change(_tenantA))
        {
            (await ReadAsync(id)).Status.ShouldBe(MailboxStatus.Activating);
        }
    }

    /* ---------- Delete forgets the refresh token and cursor (build note 21, review M6) ---------- */

    [Fact]
    public async Task Deleting_a_connection_forgets_its_refresh_token_and_cursor()
    {
        using var features = IntakeOn();
        using var tenant = _currentTenant.Change(_tenantA);
        var connection = await ActivatingAsync();
        await Job().ExecuteAsync(Args(connection.Id));
        (await ReadAsync(connection.Id)).SyncCursor.ShouldNotBeNull();

        await Service.DeleteAsync(connection.Id);

        using (GetRequiredService<IDataFilter>().Disable<Volo.Abp.ISoftDelete>())
        {
            var deleted = await ReadAsync(connection.Id);
            deleted.IsDeleted.ShouldBeTrue("a soft delete: the row and its trace stay");
            deleted.RefreshTokenEncrypted.ShouldBeNull();
            deleted.SyncCursor.ShouldBeNull();
        }

        (await WithUnitOfWorkAsync(() => Connections.FindAsync(connection.Id))).ShouldBeNull();
        await Service.DeleteAsync(connection.Id); /* again: silent */
        await Service.DeleteAsync(Guid.NewGuid());
    }

    [Fact]
    public async Task Tenant_B_cannot_delete_or_forget_tenant_As_connection()
    {
        using var features = IntakeOn();
        Guid id;
        using (_currentTenant.Change(_tenantA))
        {
            id = (await ActivatingAsync()).Id;
        }

        using (_currentTenant.Change(_tenantB))
        {
            await Service.DeleteAsync(id);
        }

        using (_currentTenant.Change(_tenantA))
        {
            var stored = await ReadAsync(id);
            stored.IsDeleted.ShouldBeFalse();
            stored.RefreshTokenEncrypted.ShouldNotBeNull();
        }
    }

    /// <summary>A distributed lock somebody else holds: every acquisition fails at once.</summary>
    private sealed class BusyLock : IAbpDistributedLock
    {
        public List<string> Requested { get; } = new();

        public Task<IAbpDistributedLockHandle?> TryAcquireAsync(string name, TimeSpan timeout = default, CancellationToken cancellationToken = default)
        {
            Requested.Add(name);
            return Task.FromResult<IAbpDistributedLockHandle?>(null);
        }
    }
}
