using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace OpenTms.Platform.DerivedValues;

/// <summary>The calculation state of one record, for a grid's or a form's "calculating" indicator (design 49).</summary>
public class DerivedValueStateDto
{
    public Guid TargetId { get; set; }

    public long RequestedVersion { get; set; }

    public long CalculatedVersion { get; set; }

    public DerivedValueStatus Status { get; set; }

    public string? LastErrorCode { get; set; }

    public DateTime? CalculatedAt { get; set; }
}

public class DerivedValueStatesRequestDto
{
    [Required, MaxLength(DerivedValueConsts.MaxTargetTypeLength)]
    public string TargetType { get; set; } = string.Empty;

    [Required, MaxLength(DerivedValueConsts.MaxStatesPerRead)]
    public List<Guid> TargetIds { get; set; } = new();
}

public class RecalculateDerivedValuesDto
{
    [Required, MaxLength(DerivedValueConsts.MaxTargetTypeLength)]
    public string TargetType { get; set; } = string.Empty;

    [Required, MaxLength(DerivedValueConsts.MaxTargetsPerRequest)]
    public List<Guid> TargetIds { get; set; } = new();
}

public interface IDerivedValueAppService : IApplicationService
{
    /// <summary>The states of the given records; records never calculated are absent. Needs the type's watch permission.</summary>
    Task<List<DerivedValueStateDto>> GetStatesAsync(DerivedValueStatesRequestDto input);

    /// <summary>Requests a recalculation of the given records; returns how many were requested.</summary>
    Task<int> RecalculateAsync(RecalculateDerivedValuesDto input);
}
