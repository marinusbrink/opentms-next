using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace OpenTms.PlanningExecution.Published;

/// <summary>See <see cref="ITripFactReader"/>: projections only, so no trip is tracked; every query runs under the tenant filter.</summary>
// Published in-process reader: a derived-value job and two workers call it without a user, so it is anonymous; IMetricQuery authorizes for the one user-facing path.
[RemoteService(IsEnabled = false)]
[AllowAnonymous]
public class TripFactReader : PlanningExecutionAppServiceBase, ITripFactReader
{
    private readonly IRepository<TransportTrip, Guid> _trips;
    private readonly IRepository<TransportTripType, Guid> _types;

    public TripFactReader(IRepository<TransportTrip, Guid> trips, IRepository<TransportTripType, Guid> types)
    {
        _trips = trips;
        _types = types;
    }

    [UnitOfWork(isTransactional: false)]
    public virtual async Task<TripFactSourceDto?> GetAsync(Guid tripId, CancellationToken cancellationToken = default)
    {
        var query = (await _trips.GetQueryableAsync())
            .Where(t => t.Id == tripId)
            .Select(t => new TripFactSourceDto
            {
                Id = t.Id,
                TripDate = t.TripDate,
                Status = t.Status,
                Execution = t.Execution,
                CarrierRelationId = t.CarrierRelationId,
                PrimaryVehicleId = t.Vehicles.OrderBy(v => v.Position).Select(v => (Guid?)v.VehicleId).FirstOrDefault(),
                OrganizationUnitId = t.OrganizationUnitId,
                TransportTripTypeId = t.TransportTripTypeId,
                CostAmount = t.CostAmount,
                RevenueAmount = t.RevenueAmount,
                Co2Kg = t.Co2Kg,
                LoadFactor = t.LoadFactor,
                PlannedDistanceM = t.PlannedDistanceM,
                ActualDistanceM = t.ActualDistanceM,
                Stops = t.Stops.Select(s => new TripFactStopDto { PlannedArrival = s.PlannedArrival, ActualArrival = s.ActualArrival }).ToList(),
            });
        return await AsyncExecuter.FirstOrDefaultAsync(query, cancellationToken);
    }

    [UnitOfWork(isTransactional: false)]
    public virtual async Task<long> GetCountAsync(CancellationToken cancellationToken = default)
        => await AsyncExecuter.LongCountAsync(await _trips.GetQueryableAsync(), cancellationToken);

    [UnitOfWork(isTransactional: false)]
    public virtual async Task<List<Guid>> GetIdsAfterAsync(Guid? afterId, int take, CancellationToken cancellationToken = default)
    {
        var query = await _trips.GetQueryableAsync();
        if (afterId != null)
        {
            var after = afterId.Value;
            query = query.Where(t => t.Id.CompareTo(after) > 0);
        }

        return await AsyncExecuter.ToListAsync(query.OrderBy(t => t.Id).Select(t => t.Id).Take(take), cancellationToken);
    }

    [UnitOfWork(isTransactional: false)]
    public virtual async Task<List<Guid>> GetIdsModifiedSinceAsync(DateTime since, Guid? afterId, int take, CancellationToken cancellationToken = default)
    {
        /* Only the soft-delete filter is off, on purpose (design 75): a lost delete has to reach the calculator, and a soft delete moves
         * LastModificationTime (measured at build). A record never saved again has only its CreationTime. The tenant filter stays on. */
        using (DataFilter.Disable<ISoftDelete>())
        {
            var query = (await _trips.GetQueryableAsync()).Where(t => t.LastModificationTime >= since || t.CreationTime >= since);
            if (afterId != null)
            {
                var after = afterId.Value;
                query = query.Where(t => t.Id.CompareTo(after) > 0);
            }

            return await AsyncExecuter.ToListAsync(query.OrderBy(t => t.Id).Select(t => t.Id).Take(take), cancellationToken);
        }
    }

    [UnitOfWork(isTransactional: false)]
    public virtual async Task<IReadOnlyDictionary<Guid, string>> GetTypeNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        var keys = (ids ?? []).Where(id => id != Guid.Empty).Distinct().ToList();
        if (keys.Count > ITripFactReader.MaxKeys)
        {
            throw new BusinessException(ReferenceDataErrorCodes.TooManyRows).WithData("max", ITripFactReader.MaxKeys);
        }

        if (keys.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var types = await _types.GetListAsync(t => keys.Contains(t.Id), includeDetails: false, cancellationToken);
        return types.ToDictionary(t => t.Id, t => t.Name.Resolve(CultureInfo.CurrentUICulture));
    }
}
