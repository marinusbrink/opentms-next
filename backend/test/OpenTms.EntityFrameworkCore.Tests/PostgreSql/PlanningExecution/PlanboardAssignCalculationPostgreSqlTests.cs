using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.PlanningExecution;
using OpenTms.TransportRun;
using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.Planboard;
using OpenTms.Platform.DerivedValues;
using Shouldly;
using Volo.Abp.Uow;
using Xunit;

namespace OpenTms.PostgreSql.PlanningExecution;

/// <summary>
/// Design 75 risk analysis — "Routing/calculation failure" (High risk): a routing failure during the derived-value
/// job records Status = Failed in the state table and leaves the leg on its trip. The test uses <see cref="TestDoubles.TestRoutingWorld"/>
/// with RoutingDown = true so the routing adapter throws <c>RoutingUnavailableException</c>; the job catches it,
/// marks the state Failed (committed), then rethrows for Hangfire retry — the leg is never unassigned.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class PlanboardAssignCalculationPostgreSqlTests : PlanningPostgreSqlTestBase
{
    private static readonly AbpUnitOfWorkOptions Transactional = new() { IsTransactional = true };

    private IPlanboardAppService Planboard => GetRequiredService<IPlanboardAppService>();
    private IDerivedValueScheduler Scheduler => GetRequiredService<IDerivedValueScheduler>();
    private IDerivedValueStateStore Store => GetRequiredService<IDerivedValueStateStore>();
    private DerivedValueCalculationJob Job => GetRequiredService<DerivedValueCalculationJob>();

    [Fact]
    public async Task Routing_failure_during_calculation_leaves_leg_assigned_and_records_Failed()
    {
        var suffix = TransportRunFixture.NewSuffix();
        Guid tripId = default, legId = default;
        long version;

        // — committed scene: a confirmed leg assigned to a trip, plus a calculation request —
        using (CurrentTenant.Change(TenantA))
        using (var uow = UnitOfWorkManager.Begin(Transactional, requiresNew: true))
        {
            await OrdersMasterDataAsync();
            var a = await PlaceAsync("RFCA" + suffix, "RFC_A", 52.37, 4.90);
            var b = await PlaceAsync("RFCB" + suffix, "RFC_B", 51.92, 4.48);
            var typeId = await SeededTripTypeAsync();
            var trip = await CreateTripAsync(typeId);
            tripId = trip.Id;

            var order = await OrderAsync(a.RelationId,
                LineOf(1, 100,
                    StopAt(a.RelationId, a.AddressId, Load),
                    StopAt(b.RelationId, b.AddressId, Unload)));
            var legs = await ConfirmAsync(order.Id);
            legId = legs[0].Id;

            await Planboard.AssignLegsAsync(trip.Id, new PlanboardLegsDto
            {
                LegIds = [legId],
                ConcurrencyStamp = trip.ConcurrencyStamp
            });
            await FlushAsync();

            // Request a fresh version so we know the exact version the job must process.
            version = await Scheduler.RequestAsync(PlanningExecutionApplicationModule.LegTargetType, legId, "LegAssigned");
            await uow.CompleteAsync();
        }

        // — routing is down; job catches the exception, records Failed, rethrows —
        World.RoutingDown = true;
        try
        {
            using (AsBackgroundProcess())
            {
                await Should.ThrowAsync<Exception>(() =>
                    Job.ExecuteAsync(new DerivedValueCalculationArgs
                    {
                        TenantId = TenantA,
                        TargetType = PlanningExecutionApplicationModule.LegTargetType,
                        TargetId = legId,
                        Version = version
                    }));
            }
        }
        finally
        {
            World.RoutingDown = false;
        }

        // — derived-value state = Failed (committed by MarkFailedAsync inside the job) —
        DerivedValueState? state;
        using (CurrentTenant.Change(TenantA))
        using (var uow = UnitOfWorkManager.Begin(new AbpUnitOfWorkOptions(), requiresNew: true))
        {
            state = await Store.FindAsync(PlanningExecutionApplicationModule.LegTargetType, legId);
            await uow.CompleteAsync();
        }

        state.ShouldNotBeNull("DerivedValueState must exist after the job ran");
        state.Status.ShouldBe(DerivedValueStatus.Failed, "routing failure must set Status = Failed");

        // — routing failure must not unassign the leg —
        Leg leg;
        using (CurrentTenant.Change(TenantA))
        using (var uow = UnitOfWorkManager.Begin(new AbpUnitOfWorkOptions(), requiresNew: true))
        {
            leg = await LegRepository.GetAsync(legId);
            await uow.CompleteAsync();
        }

        leg.TripId.ShouldBe(tripId, "routing failure must not unassign the leg from its trip");
    }
}
