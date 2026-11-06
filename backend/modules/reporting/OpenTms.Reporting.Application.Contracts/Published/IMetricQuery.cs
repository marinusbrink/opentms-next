using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OpenTms.Reporting.Published;

/// <summary>
/// Open questions on the prepared numbers (design 75, AD-23), in-process: the AI connector's tools call it and it has no HTTP
/// route. It carries no ABP attributes for permissions, so it checks every metric's permissions and features itself, for the
/// current user in the current tenant. Everything it refuses comes back as an answer with a status, never as an exception.
/// </summary>
public interface IMetricQuery
{
    /// <summary>The catalog as far as this user may ask it.</summary>
    Task<MetricListDto> ListAsync(CancellationToken cancellationToken = default);

    Task<MetricAnswerDto> QueryAsync(MetricQueryRequestDto request, CancellationToken cancellationToken = default);
}

public static class MetricAnswerStatus
{
    public const string Ok = "ok";
    public const string UnknownMetric = "unknown_metric";
    public const string UnsupportedCombination = "unsupported_combination";
    public const string InvalidPeriod = "invalid_period";
    public const string NotAllowed = "not_allowed";
    public const string NotReady = "not_ready";
    public const string Timeout = "timeout";
}

public static class MetricWarnings
{
    public const string MixedCurrencies = "mixed_currencies";

    /// <summary>The backfill stopped with records of this grain differing from their facts: <see cref="MetricAnswerDto.FactsMissing"/> says how many.</summary>
    public const string FactsIncomplete = "facts_incomplete";
}

public class MetricListDto
{
    public List<MetricListItemDto> Metrics { get; set; } = new();

    /// <summary>Whether this tenant's backfill has finished; until then every query answers not_ready.</summary>
    public bool FactsReady { get; set; }
}

public class MetricListItemDto
{
    public string Key { get; set; } = string.Empty;

    public string Unit { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Definition { get; set; } = string.Empty;

    public List<string> Breakdowns { get; set; } = new();

    public List<string> Filters { get; set; } = new();
}

public class MetricQueryRequestDto
{
    public List<string>? Metrics { get; set; }

    public DateOnly? DateFrom { get; set; }

    public DateOnly? DateTo { get; set; }

    public List<string>? GroupBy { get; set; }

    public MetricFiltersDto? Filters { get; set; }

    public string? Sort { get; set; }

    public int? Top { get; set; }
}

public class MetricFiltersDto
{
    /// <summary>Replaces the default statuses; names of the grain's status.</summary>
    public List<string>? Status { get; set; }

    public Guid? CustomerRelationId { get; set; }

    public Guid? TransportOrderTypeId { get; set; }

    public Guid? OrganizationUnitId { get; set; }

    public Guid? CarrierRelationId { get; set; }

    public Guid? VehicleId { get; set; }

    public Guid? TransportTripTypeId { get; set; }

    public string? Execution { get; set; }
}

/// <summary>
/// With <see cref="Status"/> <c>ok</c> everything but <see cref="Message"/> and <see cref="Hint"/> is filled (<see cref="Warnings"/>
/// when there are any); a refusal has only the status, the message and the hint, and never a value.
/// </summary>
public class MetricAnswerDto
{
    public string Status { get; set; } = MetricAnswerStatus.Ok;

    public string? Message { get; set; }

    public string? Hint { get; set; }

    public MetricPeriodDto? Period { get; set; }

    public List<MetricDefinitionTextDto>? Definitions { get; set; }

    public List<MetricRowDto>? Rows { get; set; }

    public long? TotalGroups { get; set; }

    public bool? Truncated { get; set; }

    public MetricCoverageDto? Coverage { get; set; }

    public DateTime? LastFactUpdateAt { get; set; }

    public List<string>? Warnings { get; set; }

    /// <summary>With warning <see cref="MetricWarnings.FactsIncomplete"/>: how many records of this grain, in the whole tenant, differ from their facts.</summary>
    public long? FactsMissing { get; set; }
}

public class MetricPeriodDto
{
    public DateOnly DateFrom { get; set; }

    public DateOnly DateTo { get; set; }
}

public class MetricDefinitionTextDto
{
    public string Metric { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
}

public class MetricRowDto
{
    public List<MetricGroupDto> Groups { get; set; } = new();

    public string? CurrencyCode { get; set; }

    /// <summary>Per chosen metric; null when no record of the group has a value, never zero in its place.</summary>
    public Dictionary<string, decimal?> Values { get; set; } = new();
}

public class MetricGroupDto
{
    public string Breakdown { get; set; } = string.Empty;

    /// <summary>The record's id for a breakdown over records; absent for a period, a status or an execution.</summary>
    public Guid? Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

public class MetricCoverageDto
{
    /// <summary>Records of the period and filters with a counted status.</summary>
    public long RecordsIncluded { get; set; }

    /// <summary>Of those, the records without a value for at least one chosen metric.</summary>
    public long RecordsWithoutValue { get; set; }

    /// <summary>Records of the period and filters left out by the status rule.</summary>
    public long ExcludedByStatus { get; set; }
}
