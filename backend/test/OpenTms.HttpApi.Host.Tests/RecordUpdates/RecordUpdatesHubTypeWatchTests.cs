using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using OpenTms.Platform.DerivedValues;
using OpenTms.Platform.RecordUpdates;
using Shouldly;
using Xunit;

namespace OpenTms.RecordUpdates;

/* Design 67, the hub's type watch: WatchType joins exactly the group the policy resolves (the server tenant's type group),
 * UnwatchType leaves it, and a refusal of the policy (unregistered type, missing permission) is a HubException with the
 * documented code and joins nothing. Risk class: Critical (authorization). */
public class RecordUpdatesHubTypeWatchTests
{
    private const string TripType = "PlanningExecution.TransportTrip";
    private static readonly Guid Tenant = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public async Task WatchType_joins_and_UnwatchType_leaves_the_type_group_the_policy_resolves()
    {
        var (hub, groups) = Hub(allowed: true);

        await hub.WatchType(TripType);
        await hub.UnwatchType(TripType);

        groups.Added.ShouldBe([("connection-1", RecordUpdate.TypeGroupName(Tenant, TripType))]);
        groups.Removed.ShouldBe([("connection-1", RecordUpdate.TypeGroupName(Tenant, TripType))]);
    }

    [Fact]
    public async Task A_refused_type_is_a_hub_exception_and_joins_nothing()
    {
        var (hub, groups) = Hub(allowed: false);

        (await Should.ThrowAsync<HubException>(() => hub.WatchType("PlanningExecution.Unknown"))).Message.ShouldBe(RecordUpdatesHub.NotAllowedCode);
        (await Should.ThrowAsync<HubException>(() => hub.UnwatchType(TripType))).Message.ShouldBe(RecordUpdatesHub.NotAllowedCode);
        groups.Added.ShouldBeEmpty();
        groups.Removed.ShouldBeEmpty();
    }

    private static (RecordUpdatesHub Hub, FakeGroups Groups) Hub(bool allowed)
    {
        var groups = new FakeGroups();
        var hub = new RecordUpdatesHub(new FakePolicy(allowed)) { Context = new FakeContext(), Groups = groups };
        return (hub, groups);
    }

    /// <summary>Answers as the real policy does for the server tenant <see cref="Tenant"/>; the policy's own rules are unit-tested in Application.Tests.</summary>
    private sealed class FakePolicy(bool allowed) : RecordUpdateWatchPolicy(null!, null!, Options.Create(new DerivedValueOptions()))
    {
        public override Task<string?> ResolveTypeGroupAsync(string? targetType)
            => Task.FromResult(allowed && targetType == TripType ? RecordUpdate.TypeGroupName(Tenant, targetType) : null);
    }

    private sealed class FakeGroups : IGroupManager
    {
        public List<(string Connection, string Group)> Added { get; } = new();

        public List<(string Connection, string Group)> Removed { get; } = new();

        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            Added.Add((connectionId, groupName));
            return Task.CompletedTask;
        }

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            Removed.Add((connectionId, groupName));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeContext : HubCallerContext
    {
        public override string ConnectionId => "connection-1";
        public override string? UserIdentifier => "user-1";
        public override ClaimsPrincipal? User => new(new ClaimsIdentity("test"));
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;

        public override void Abort()
        {
        }
    }
}
