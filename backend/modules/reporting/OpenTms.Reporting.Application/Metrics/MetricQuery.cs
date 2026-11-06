using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenTms.MasterData.Published;
using OpenTms.Orders.Published;
using OpenTms.Orders.TransportOrders;
using OpenTms.PlanningExecution.Published;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Platform.Published;
using OpenTms.Reporting.Facts;
using OpenTms.Reporting.Published;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Threading;
using Volo.Abp.Uow;
using Volo.Abp.Users;

namespace OpenTms.Reporting.Metrics;

/// <summary>
/// <see cref="IMetricQuery"/> (design 75): checks the request against the catalog, then this user's permissions and features per
/// metric, then whether the tenant's facts are ready, and only then reads, through the repositories, so the tenant filter
/// applies. Every read of a query shares one 2-second limit, and a query past it gives no partial answer. The log line has keys,
/// counts and the duration; never a filter's id or value (constitution rule 5).
/// </summary>
public class MetricQuery : IMetricQuery, ITransientDependency
{
    /// <summary>A group whose record has no such id, like an order without an organisation unit.</summary>
    private const string NoneName = "(none)";

    /// <summary>A group whose id no longer has a name: the record was deleted, or the person anonymised.</summary>
    private const string UnknownName = "(unknown)";

    private readonly IRepository<OrderFact, Guid> _orderFacts;
    private readonly IRepository<TripFact, Guid> _tripFacts;
    private readonly IOrderFactReader _orders;
    private readonly ITripFactReader _trips;
    private readonly IMasterDataReferenceLookup _masterData;
    private readonly IOrganizationUnitNameLookup _organizationUnits;
    private readonly ReportingFactState _state;
    private readonly IFeatureChecker _features;
    private readonly IPermissionChecker _permissions;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentUser _currentUser;
    private readonly ICancellationTokenProvider _cancellation;
    private readonly ReportingMetricsOptions _options;

    public ILogger<MetricQuery> Logger { get; set; } = NullLogger<MetricQuery>.Instance;

    public MetricQuery(
        IRepository<OrderFact, Guid> orderFacts,
        IRepository<TripFact, Guid> tripFacts,
        IOrderFactReader orders,
        ITripFactReader trips,
        IMasterDataReferenceLookup masterData,
        IOrganizationUnitNameLookup organizationUnits,
        ReportingFactState state,
        IFeatureChecker features,
        IPermissionChecker permissions,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        ICancellationTokenProvider cancellation,
        IOptions<ReportingMetricsOptions> options)
    {
        _orderFacts = orderFacts;
        _tripFacts = tripFacts;
        _orders = orders;
        _trips = trips;
        _masterData = masterData;
        _organizationUnits = organizationUnits;
        _state = state;
        _features = features;
        _permissions = permissions;
        _currentTenant = currentTenant;
        _currentUser = currentUser;
        _cancellation = cancellation;
        _options = options.Value;
    }

    [UnitOfWork(isTransactional: false)]
    public virtual async Task<MetricListDto> ListAsync(CancellationToken cancellationToken = default)
    {
        var result = new MetricListDto();
        foreach (var metric in MetricCatalog.All)
        {
            if (await IsAllowedAsync(metric))
            {
                result.Metrics.Add(new MetricListItemDto
                {
                    Key = metric.Key,
                    Unit = metric.Unit,
                    Description = metric.Description,
                    Definition = metric.Definition,
                    Breakdowns = MetricCatalog.BreakdownsOf(metric.Grain).ToList(),
                    Filters = MetricCatalog.FiltersOf(metric.Grain).ToList(),
                });
            }
        }

        result.FactsReady = await _state.GetBackfillCompletedAtAsync() != null;
        return result;
    }

    [UnitOfWork(isTransactional: false)]
    public virtual async Task<MetricAnswerDto> QueryAsync(MetricQueryRequestDto request, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        MetricPlan? plan = null;
        MetricAnswerDto answer;
        try
        {
            answer = Check(request, out plan);
            if (answer.Status == MetricAnswerStatus.Ok)
            {
                answer = await AnswerAsync(plan!, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !_cancellation.Token.IsCancellationRequested)
        {
            answer = Refuse(MetricAnswerStatus.Timeout, "The question took too long and was stopped; nothing partial is returned.", "Ask for a shorter period, or add a filter.");
        }

        /* Keys from the plan only: they come from the catalog, so no text of the caller reaches the log. */
        Logger.LogInformation(
            "Metric query by user {UserId} (tenant {TenantId}): metrics {MetricKeys}, breakdowns {BreakdownKeys}: {Status}, {RowCount} rows in {DurationMs} ms",
            _currentUser.Id, _currentTenant.Id, plan?.Metrics.Select(m => m.Key).ToList(), plan?.GroupBy, answer.Status, answer.Rows?.Count ?? 0, stopwatch.ElapsedMilliseconds);
        return answer;
    }

    private async Task<MetricAnswerDto> AnswerAsync(MetricPlan plan, CancellationToken cancellationToken)
    {
        foreach (var metric in plan.Metrics)
        {
            if (!await IsAllowedAsync(metric))
            {
                return Refuse(
                    MetricAnswerStatus.NotAllowed,
                    "This user may not ask for " + metric.Key + ", or the organisation has switched it off.",
                    "list_metrics shows what this user may ask.");
            }
        }

        if (await _state.GetBackfillCompletedAtAsync() == null)
        {
            return Refuse(
                MetricAnswerStatus.NotReady,
                "The numbers of this organisation are still being prepared.",
                await ProgressAsync(plan.Grain, cancellationToken));
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cancellation.Token);
        limit.CancelAfter(_options.QueryTimeout);
        var answer = plan.Grain == MetricGrain.Order
            ? await RunAsync(await _orderFacts.GetQueryableAsync(), OrderFactShape.Instance, _orderFacts, plan, limit.Token)
            : await RunAsync(await _tripFacts.GetQueryableAsync(), TripFactShape.Instance, _tripFacts, plan, limit.Token);

        /* The promise is honesty (design 75): a backfill that ran out of sweeps answers from what exists and says what it lacks. */
        var missing = await _state.GetFactsMissingAsync();
        var missingOfGrain = missing == null ? 0 : plan.Grain == MetricGrain.Order ? missing.Orders : missing.Trips;
        if (missingOfGrain > 0)
        {
            answer.Warnings = [.. answer.Warnings ?? [], MetricWarnings.FactsIncomplete];
            answer.FactsMissing = missingOfGrain;
        }

        return answer;
    }

    private async Task<MetricAnswerDto> RunAsync<TFact>(IQueryable<TFact> facts, IFactShape<TFact> shape, IRepository<TFact, Guid> repository, MetricPlan plan, CancellationToken cancellationToken)
        where TFact : class, Volo.Abp.Domain.Entities.IEntity<Guid>
    {
        var metricKeys = plan.Metrics.Select(m => m.Key).ToList();
        var splitByCurrency = plan.Metrics.Any(m => m.SplitsByCurrency);
        var scoped = shape.Filtered(shape.InPeriod(facts, plan.From, plan.To), plan.Filters);
        var counted = shape.WithStatus(scoped, plan.Statuses);
        var executer = repository.AsyncExecuter;

        var rows = await executer.ToListAsync(
            MetricSqlBuilder.Groups(counted, shape, plan.GroupBy, metricKeys, splitByCurrency, plan.Sort).Take(plan.Top + 1),
            cancellationToken);
        var truncated = rows.Count > plan.Top;
        var totalGroups = truncated
            ? await executer.LongCountAsync(MetricSqlBuilder.GroupCount(counted, shape, plan.GroupBy, splitByCurrency), cancellationToken)
            : rows.Count;
        rows = rows.Take(plan.Top).ToList();

        var coverage = await executer.ToListAsync(MetricSqlBuilder.Coverage(scoped, shape, metricKeys), cancellationToken);
        var included = coverage.Where(c => plan.Statuses.Contains(c.Status)).ToList();
        var warnings = await WarningsAsync(plan, cancellationToken);
        var names = await NamesAsync(plan, shape, rows, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return new MetricAnswerDto
        {
            Status = MetricAnswerStatus.Ok,
            Period = new MetricPeriodDto { DateFrom = plan.From, DateTo = plan.To },
            Definitions = plan.Metrics.Select(m => new MetricDefinitionTextDto { Metric = m.Key, Text = m.Definition }).ToList(),
            Rows = rows.Select(r => ToRow(r, plan, shape, names)).ToList(),
            TotalGroups = totalGroups,
            Truncated = truncated,
            Coverage = new MetricCoverageDto
            {
                RecordsIncluded = included.Sum(c => c.All),
                RecordsWithoutValue = included.Sum(c => c.Missing),
                ExcludedByStatus = coverage.Where(c => !plan.Statuses.Contains(c.Status)).Sum(c => c.All),
            },
            LastFactUpdateAt = coverage.Count == 0 ? null : coverage.Max(c => c.LastUpdate),
            Warnings = warnings,
        };
    }

    /// <summary>
    /// The money a calculator combined may mix currencies (design 75 "Currency"): such a metric warns when the period holds orders
    /// in more than one currency. Which orders count: the query's statuses on the order grain, the default statuses on the trip grain.
    /// </summary>
    private async Task<List<string>?> WarningsAsync(MetricPlan plan, CancellationToken cancellationToken)
    {
        if (!plan.Metrics.Any(m => m.CanMixCurrencies))
        {
            return null;
        }

        var statuses = plan.Grain == MetricGrain.Order
            ? plan.Statuses
            : MetricCatalog.DefaultOrderStatuses.Select(s => (int)s).ToList();
        var shape = OrderFactShape.Instance;
        var query = shape.WithStatus(shape.InPeriod(await _orderFacts.GetQueryableAsync(), plan.From, plan.To), statuses);
        var currencies = await _orderFacts.AsyncExecuter.ToListAsync(query.Select(f => f.CurrencyCode).Distinct().Take(2), cancellationToken);
        return currencies.Count > 1 ? [MetricWarnings.MixedCurrencies] : null;
    }

    private MetricRowDto ToRow<TFact>(MetricGroupRow row, MetricPlan plan, IFactShape<TFact> shape, IReadOnlyDictionary<string, IReadOnlyDictionary<Guid, string>> names)
    {
        var result = new MetricRowDto { CurrencyCode = row.Currency };
        for (var i = 0; i < plan.GroupBy.Count; i++)
        {
            var breakdown = plan.GroupBy[i];
            result.Groups.Add(shape.Breakdowns[breakdown].Kind switch
            {
                MetricSlotKind.Id => ToIdGroup(breakdown, i == 0 ? row.IdA : row.IdB, names),
                MetricSlotKind.Date => new MetricGroupDto { Breakdown = breakdown, Name = ToPeriodName(breakdown, i == 0 ? row.DateA : row.DateB) },
                _ => new MetricGroupDto { Breakdown = breakdown, Name = ToCodeName(plan.Grain, breakdown, i == 0 ? row.CodeA : row.CodeB) },
            });
        }

        for (var i = 0; i < plan.Metrics.Count; i++)
        {
            var value = i switch { 0 => row.V0, 1 => row.V1, 2 => row.V2, 3 => row.V3, _ => row.V4 };
            result.Values[plan.Metrics[i].Key] = value == null ? null : Math.Round(value.Value, plan.Metrics[i].Decimals, MidpointRounding.AwayFromZero);
        }

        return result;
    }

    private static MetricGroupDto ToIdGroup(string breakdown, Guid? id, IReadOnlyDictionary<string, IReadOnlyDictionary<Guid, string>> names)
    {
        if (id == null)
        {
            return new MetricGroupDto { Breakdown = breakdown, Name = NoneName };
        }

        return new MetricGroupDto
        {
            Breakdown = breakdown,
            Id = id,
            Name = names.TryGetValue(breakdown, out var known) && known.TryGetValue(id.Value, out var name) ? name : UnknownName,
        };
    }

    private static string ToPeriodName(string breakdown, DateOnly? date)
        => date?.ToString(breakdown == MetricBreakdowns.Month ? "yyyy-MM" : "yyyy-MM-dd", CultureInfo.InvariantCulture) ?? NoneName;

    private static string ToCodeName(MetricGrain grain, string breakdown, int? code)
    {
        if (code == null)
        {
            return NoneName;
        }

        return breakdown == MetricBreakdowns.Execution
            ? ((TripExecution)code.Value).ToString()
            : grain == MetricGrain.Order ? ((TransportOrderStatus)code.Value).ToString() : ((TransportTripStatus)code.Value).ToString();
    }

    /// <summary>The names per breakdown, looked up per request for at most 100 rows: a renamed customer is right at once.</summary>
    private async Task<IReadOnlyDictionary<string, IReadOnlyDictionary<Guid, string>>> NamesAsync<TFact>(MetricPlan plan, IFactShape<TFact> shape, List<MetricGroupRow> rows, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<Guid, string>>();
        for (var i = 0; i < plan.GroupBy.Count; i++)
        {
            var breakdown = plan.GroupBy[i];
            if (shape.Breakdowns[breakdown].Kind != MetricSlotKind.Id)
            {
                continue;
            }

            var ids = rows.Select(r => i == 0 ? r.IdA : r.IdB).OfType<Guid>().Distinct().ToList();
            result[breakdown] = breakdown switch
            {
                MetricBreakdowns.Customer or MetricBreakdowns.Carrier => ToNames(await _masterData.GetRelationsAsync(ids)),
                MetricBreakdowns.Vehicle => ToNames(await _masterData.GetReferencesAsync(MasterDataReferenceKind.Vehicle, ids)),
                MetricBreakdowns.OrderType => await _orders.GetTypeNamesAsync(ids, cancellationToken),
                MetricBreakdowns.TripType => await _trips.GetTypeNamesAsync(ids, cancellationToken),
                _ => await _organizationUnits.GetAsync(ids),
            };
        }

        return result;
    }

    private static IReadOnlyDictionary<Guid, string> ToNames(IEnumerable<MasterDataReferenceDto> references)
        => references.ToDictionary(r => r.Id, r => r.Name);

    private async Task<string> ProgressAsync(MetricGrain grain, CancellationToken cancellationToken)
    {
        var (done, total, noun) = grain == MetricGrain.Order
            ? (await _orderFacts.LongCountAsync(cancellationToken), await _orders.GetCountAsync(cancellationToken), "orders")
            : (await _tripFacts.LongCountAsync(cancellationToken), await _trips.GetCountAsync(cancellationToken), "trips");
        return string.Create(CultureInfo.InvariantCulture, $"{done} of {total} {noun} prepared. Try again later.");
    }

    private async Task<bool> IsAllowedAsync(MetricDefinition metric)
    {
        foreach (var requirement in MetricAccessRules.FeaturesOf(metric))
        {
            var anyOn = false;
            foreach (var feature in requirement)
            {
                if (await _features.IsEnabledAsync(feature))
                {
                    anyOn = true;
                    break;
                }
            }

            if (!anyOn)
            {
                return false;
            }
        }

        foreach (var permission in MetricAccessRules.PermissionsOf(metric))
        {
            if (!await _permissions.IsGrantedAsync(permission))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Everything that needs no read: only catalog keys and typed values pass into the plan.</summary>
    private static MetricAnswerDto Check(MetricQueryRequestDto request, out MetricPlan? plan)
    {
        plan = null;
        var keys = (request.Metrics ?? []).Distinct(StringComparer.Ordinal).ToList();
        if (keys.Count is 0 or > MetricCatalog.MaxMetrics)
        {
            return Refuse(MetricAnswerStatus.UnsupportedCombination, "Ask for 1 to " + MetricCatalog.MaxMetrics + " metrics.", "list_metrics shows the metrics.");
        }

        var metrics = keys.Select(MetricCatalog.Find).ToList();
        if (metrics.Any(m => m == null))
        {
            return Refuse(MetricAnswerStatus.UnknownMetric, "That metric is not in the list, so this cannot be answered.", "Available metrics: " + string.Join(", ", MetricCatalog.All.Select(m => m.Key)) + ".");
        }

        var known = metrics.Select(m => m!).ToList();
        var grain = known[0].Grain;
        if (known.Any(m => m.Grain != grain))
        {
            return Refuse(MetricAnswerStatus.UnsupportedCombination, "Order metrics and trip metrics cannot be mixed in one question.", "Ask twice: once for the order metrics, once for the trip metrics.");
        }

        var groupBy = (request.GroupBy ?? []).Distinct(StringComparer.Ordinal).ToList();
        var allowedBreakdowns = MetricCatalog.BreakdownsOf(grain);
        if (groupBy.Count > MetricCatalog.MaxBreakdowns || groupBy.Any(b => !allowedBreakdowns.Contains(b)))
        {
            return Refuse(MetricAnswerStatus.UnsupportedCombination, "At most " + MetricCatalog.MaxBreakdowns + " breakdowns, of those this kind of metric has.", "Breakdowns here: " + string.Join(", ", allowedBreakdowns) + ".");
        }

        var filters = request.Filters ?? new MetricFiltersDto();
        var otherGrainFilter = grain == MetricGrain.Order
            ? filters.CarrierRelationId != null || filters.VehicleId != null || filters.TransportTripTypeId != null || filters.Execution != null
            : filters.CustomerRelationId != null || filters.TransportOrderTypeId != null;
        if (otherGrainFilter)
        {
            return Refuse(MetricAnswerStatus.UnsupportedCombination, "That filter belongs to the other kind of metric.", "Filters here: " + string.Join(", ", MetricCatalog.FiltersOf(grain)) + ".");
        }

        TripExecution? execution = null;
        if (filters.Execution != null)
        {
            if (!TryParseName<TripExecution>(filters.Execution, out var parsed))
            {
                return Refuse(MetricAnswerStatus.UnsupportedCombination, "That execution does not exist.", "Executions: " + string.Join(", ", Enum.GetNames<TripExecution>()) + ".");
            }

            execution = parsed;
        }

        if (!TryParseStatuses(grain, filters.Status, out var statuses))
        {
            var names = grain == MetricGrain.Order ? Enum.GetNames<TransportOrderStatus>() : Enum.GetNames<TransportTripStatus>();
            return Refuse(MetricAnswerStatus.UnsupportedCombination, "That status does not exist for this kind of metric.", "Statuses here: " + string.Join(", ", names) + ".");
        }

        var sortable = request.Sort == null
            || keys.Contains(request.Sort)
            || (MetricBreakdowns.IsPeriod(request.Sort) && groupBy.Contains(request.Sort));
        if (!sortable)
        {
            return Refuse(MetricAnswerStatus.UnsupportedCombination, "Sort by a chosen metric, or by a period among the breakdowns.", "Leave sort out for the first metric, largest first.");
        }

        if (request.DateFrom is not { } from || request.DateTo is not { } to)
        {
            return Refuse(MetricAnswerStatus.InvalidPeriod, "Give both dateFrom and dateTo.", "At most " + MetricCatalog.MaxPeriodYears + " years.");
        }

        if (to < from || to > from.AddYears(MetricCatalog.MaxPeriodYears))
        {
            return Refuse(MetricAnswerStatus.InvalidPeriod, "dateTo has to be on or after dateFrom, at most " + MetricCatalog.MaxPeriodYears + " years later.", "Ask for a shorter period, or several.");
        }

        plan = new MetricPlan(
            grain,
            known,
            from,
            to,
            groupBy,
            new ParsedMetricFilters(filters.CustomerRelationId, filters.TransportOrderTypeId, filters.OrganizationUnitId, filters.CarrierRelationId, filters.VehicleId, filters.TransportTripTypeId, execution),
            statuses,
            request.Sort,
            Math.Clamp(request.Top ?? MetricCatalog.DefaultTop, 1, MetricCatalog.MaxTop));
        return new MetricAnswerDto();
    }

    private static bool TryParseStatuses(MetricGrain grain, List<string>? names, out List<int> statuses)
    {
        statuses = [];
        if (names == null || names.Count == 0)
        {
            statuses = grain == MetricGrain.Order
                ? MetricCatalog.DefaultOrderStatuses.Select(s => (int)s).ToList()
                : MetricCatalog.DefaultTripStatuses.Select(s => (int)s).ToList();
            return true;
        }

        foreach (var name in names)
        {
            if (grain == MetricGrain.Order && TryParseName<TransportOrderStatus>(name, out var order))
            {
                statuses.Add((int)order);
            }
            else if (grain == MetricGrain.Trip && TryParseName<TransportTripStatus>(name, out var trip))
            {
                statuses.Add((int)trip);
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>By name only: <see cref="Enum.TryParse{TEnum}(string, out TEnum)"/> alone also takes any number.</summary>
    private static bool TryParseName<TEnum>(string name, out TEnum value)
        where TEnum : struct, Enum
        => Enum.TryParse(name, ignoreCase: true, out value) && Enum.GetNames<TEnum>().Contains(name, StringComparer.OrdinalIgnoreCase);

    private static MetricAnswerDto Refuse(string status, string message, string hint)
        => new() { Status = status, Message = message, Hint = hint };

    private sealed record MetricPlan(
        MetricGrain Grain,
        IReadOnlyList<MetricDefinition> Metrics,
        DateOnly From,
        DateOnly To,
        IReadOnlyList<string> GroupBy,
        ParsedMetricFilters Filters,
        IReadOnlyList<int> Statuses,
        string? Sort,
        int Top);
}
