using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using OpenTms.Platform.DerivedValues;
using OpenTms.Platform.RecordUpdates;
using Shouldly;
using Xunit;

namespace OpenTms.RecordUpdates;

/* Design 67 (record-type channel), the host side: a notice goes to the record's own group AND the type group, both
 * named from the update's server-side tenant; the recordUpdated payload carries ids, dates and flags only (hints as
 * ISO dates and ids), and null hints for a plain notice. Risk class: Critical (a wrong group leaks another tenant's ids). */
public class SignalRRecordUpdateTransportTests
{
    private static readonly Guid TenantA = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid TenantB = Guid.Parse("99999999-8888-7777-6666-555555555555");

    private static JsonElement Serialise(RecordUpdate update)
        => JsonSerializer.SerializeToElement(SignalRRecordUpdateTransport.ToMessage(update), new JsonSerializerOptions(JsonSerializerDefaults.Web));

    [Fact]
    public void The_payload_carries_the_hints_as_ids_dates_and_flags()
    {
        var id = Guid.NewGuid();
        var unit = Guid.NewGuid();
        var vehicle = Guid.NewGuid();
        var driver = Guid.NewGuid();
        var update = new RecordUpdate(TenantA, "PlanningExecution.TransportTrip", id, 7, null,
            new RecordUpdateHints(new DateOnly(2026, 10, 26), new DateOnly(2026, 10, 27), unit, [vehicle, driver], Removed: true));

        var json = Serialise(update);

        json.GetProperty("targetType").GetString().ShouldBe("PlanningExecution.TransportTrip");
        json.GetProperty("targetId").GetGuid().ShouldBe(id);
        json.GetProperty("version").GetInt64().ShouldBe(7);
        json.GetProperty("status").ValueKind.ShouldBe(JsonValueKind.Null);
        var hints = json.GetProperty("hints");
        hints.GetProperty("from").GetString().ShouldBe("2026-10-26");
        hints.GetProperty("till").GetString().ShouldBe("2026-10-27");
        hints.GetProperty("organizationUnitId").GetGuid().ShouldBe(unit);
        hints.GetProperty("resourceIds").EnumerateArray().Select(e => e.GetGuid()).ShouldBe([vehicle, driver]);
        hints.GetProperty("removed").GetBoolean().ShouldBeTrue();
        hints.EnumerateObject().Select(p => p.Name).ShouldBe(["from", "till", "organizationUnitId", "resourceIds", "removed"], "ids and dates only, never data");
        json.EnumerateObject().Select(p => p.Name).ShouldBe(["targetType", "targetId", "version", "status", "hints"]);
        json.ToString().ShouldNotContain(TenantA.ToString("N"), Case.Insensitive, "the tenant id never travels in the payload");
    }

    [Fact]
    public void A_notice_without_hints_serialises_null_hints_and_empty_hint_parts_as_null()
    {
        var plain = Serialise(new RecordUpdate(TenantA, "Orders.TransportOrder", Guid.NewGuid(), null, DerivedValueStatus.Pending));
        plain.GetProperty("hints").ValueKind.ShouldBe(JsonValueKind.Null);
        plain.GetProperty("version").ValueKind.ShouldBe(JsonValueKind.Null);
        plain.GetProperty("status").ValueKind.ShouldNotBe(JsonValueKind.Null);

        var empty = Serialise(new RecordUpdate(TenantA, "PlanningExecution.Leg", Guid.NewGuid(), null, null, new RecordUpdateHints()));
        var hints = empty.GetProperty("hints");
        hints.GetProperty("from").ValueKind.ShouldBe(JsonValueKind.Null);
        hints.GetProperty("till").ValueKind.ShouldBe(JsonValueKind.Null);
        hints.GetProperty("organizationUnitId").ValueKind.ShouldBe(JsonValueKind.Null);
        hints.GetProperty("resourceIds").ValueKind.ShouldBe(JsonValueKind.Null);
        hints.GetProperty("removed").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task A_notice_is_sent_once_to_the_record_group_and_the_type_group_of_its_own_tenant()
    {
        var hub = new FakeHubContext();
        var transport = new SignalRRecordUpdateTransport(hub);
        var id = Guid.NewGuid();

        await transport.SendAsync(new RecordUpdate(TenantA, "PlanningExecution.TransportTrip", id, null, null, new RecordUpdateHints(Removed: false)));

        var call = hub.Calls.ShouldHaveSingleItem();
        call.Groups.ShouldBe([
            $"{TenantA:N}:PlanningExecution.TransportTrip:{id:N}",
            $"{TenantA:N}:PlanningExecution.TransportTrip:*"
        ]);
        call.Method.ShouldBe(RecordUpdatesHub.UpdatedMethod);
        call.Arguments.ShouldHaveSingleItem().ShouldNotBeNull();
    }

    [Fact]
    public async Task Negative_tenant_isolation_a_notice_of_tenant_A_never_reaches_a_group_of_tenant_B_or_the_host()
    {
        var hub = new FakeHubContext();
        var transport = new SignalRRecordUpdateTransport(hub);

        await transport.SendAsync(new RecordUpdate(TenantA, "PlanningExecution.TransportTrip", Guid.NewGuid(), null, null));
        await transport.SendAsync(new RecordUpdate(null, "PlanningExecution.TransportTrip", Guid.NewGuid(), null, null));

        hub.Calls[0].Groups.ShouldAllBe(g => g.StartsWith(TenantA.ToString("N") + ":", StringComparison.Ordinal));
        hub.Calls[0].Groups.ShouldNotContain(RecordUpdate.TypeGroupName(TenantB, "PlanningExecution.TransportTrip"));
        hub.Calls[0].Groups.ShouldNotContain(RecordUpdate.TypeGroupName(null, "PlanningExecution.TransportTrip"));
        hub.Calls[1].Groups.ShouldAllBe(g => g.StartsWith("host:", StringComparison.Ordinal), "a host notice stays in the host's groups");
    }

    private sealed record SentCall(IReadOnlyList<string> Groups, string Method, object?[] Arguments);

    private sealed class FakeHubContext : IHubContext<RecordUpdatesHub>
    {
        public List<SentCall> Calls { get; } = new();

        public IHubClients Clients => new FakeClients(this);

        public IGroupManager Groups => throw new NotSupportedException("The transport never manages groups.");

        private sealed class FakeClients(FakeHubContext owner) : IHubClients
        {
            public IClientProxy All => throw Unexpected();
            public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw Unexpected();
            public IClientProxy Client(string connectionId) => throw Unexpected();
            public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw Unexpected();
            public IClientProxy Group(string groupName) => new Proxy(owner, [groupName]);
            public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw Unexpected();
            public IClientProxy Groups(IReadOnlyList<string> groupNames) => new Proxy(owner, groupNames);
            public IClientProxy User(string userId) => throw Unexpected();
            public IClientProxy Users(IReadOnlyList<string> userIds) => throw Unexpected();

            private static Exception Unexpected() => new NotSupportedException("The transport sends to groups only.");
        }

        private sealed class Proxy(FakeHubContext owner, IReadOnlyList<string> groups) : IClientProxy
        {
            public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
            {
                owner.Calls.Add(new SentCall(groups.ToList(), method, args));
                return Task.CompletedTask;
            }
        }
    }
}
