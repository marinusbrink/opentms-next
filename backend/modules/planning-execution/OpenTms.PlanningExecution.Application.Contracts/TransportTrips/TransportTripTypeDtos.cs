using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.MasterData.Fleet;
using OpenTms.Platform.Grid;
using OpenTms.Platform.ReferenceData;
using Volo.Abp.Application.Services;

namespace OpenTms.PlanningExecution.TransportTrips;

public class TransportTripTypeRowDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public TripExecution DefaultExecution { get; set; }
    public bool SendToFleetManagementSystem { get; set; }
    /// <summary>The format applied to counter 1 today, e.g. "TR-2026-00001".</summary>
    public string NumberingExample { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsArchived { get; set; }
    public string? Description { get; set; }
}

public class TransportTripTypeDto : ReferenceDataDetailDtoBase
{
    public string Code { get; set; } = string.Empty;
    /// <summary>The name in the request culture.</summary>
    public string Name { get; set; } = string.Empty;
    public LocalizedNameDto Names { get; set; } = new();
    public TripExecution DefaultExecution { get; set; }
    public bool SendToFleetManagementSystem { get; set; }
    public NumberSequenceFormatDto Numbering { get; set; } = new();
    /// <summary>The counter the next trip of this type takes.</summary>
    public long NextNumber { get; set; }
    /// <summary>The number the next trip of this type gets, e.g. "TR-2026-00042".</summary>
    public string NextNumberPreview { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsArchived { get; set; }

    /* Design 61 "Trip type defaults", with the display names the screens preset their pickers with (one batched Master
     * Data lookup per kind; a row the lookup no longer returns is named "(not found)"). */

    public Guid? DefaultCarrierRelationId { get; set; }
    public string? DefaultCarrierRelationName { get; set; }
    public Guid? DefaultCarrierContactPersonId { get; set; }
    public string? DefaultCarrierContactPersonName { get; set; }
    public Guid? DefaultVehicleCombinationId { get; set; }
    public string? DefaultVehicleCombinationName { get; set; }
    public List<Guid> DefaultVehicleIds { get; set; } = new();
    /// <summary>The default vehicles in order (the shape of the vehicle picker's option).</summary>
    public List<TripTypeDefaultVehicleDto> DefaultVehicles { get; set; } = new();
    public List<Guid> DefaultDriverIds { get; set; } = new();
    /// <summary>The default drivers in order.</summary>
    public List<TripTypeDefaultDriverDto> DefaultDrivers { get; set; } = new();
    public Guid? DefaultDepartureRelationId { get; set; }
    public string? DefaultDepartureRelationName { get; set; }
    public Guid? DefaultDepartureAddressId { get; set; }
    /// <summary>"Street 12, 1234 AB City".</summary>
    public string? DefaultDepartureAddressLabel { get; set; }
    public TimeOnly? DefaultDepartureTime { get; set; }
    public Guid? DefaultArrivalRelationId { get; set; }
    public string? DefaultArrivalRelationName { get; set; }
    public Guid? DefaultArrivalAddressId { get; set; }
    /// <summary>"Street 12, 1234 AB City".</summary>
    public string? DefaultArrivalAddressLabel { get; set; }
    public StopCompositionMode DefaultStopCompositionMode { get; set; }
    public FirstLastStopRule DefaultFirstLastStopRule { get; set; }
}

/// <summary>A trip type's default vehicle as the vehicle picker shows it (design 61 "Trip type defaults").</summary>
public class TripTypeDefaultVehicleDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? LicencePlate { get; set; }
    /// <summary>Null when the vehicle is no longer found.</summary>
    public VehicleTypeKind? Kind { get; set; }
    public string? VehicleTypeName { get; set; }
}

/// <summary>A trip type's default driver (design 61 "Trip type defaults"); the published lookup names drivers only.</summary>
public class TripTypeDefaultDriverDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class TransportTripTypeCreateDto
{
    [Required, MaxLength(ReferenceDataConsts.MaxCodeLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    public LocalizedNameDto Names { get; set; } = new();

    [MaxLength(ReferenceDataConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    public TripExecution DefaultExecution { get; set; } = TripExecution.OwnFleet;

    public bool SendToFleetManagementSystem { get; set; } = true;

    [Required]
    public NumberSequenceFormatDto Numbering { get; set; } = new();

    /// <summary>Continue numbering at this counter (only upwards); null leaves it.</summary>
    [Range(1, long.MaxValue)]
    public long? NextNumber { get; set; }

    public bool IsArchived { get; set; }

    /* Design 61 "Trip type defaults": all optional; Master Data ids validated in the tenant. */

    public Guid? DefaultCarrierRelationId { get; set; }

    /// <summary>Needs <see cref="DefaultCarrierRelationId"/>.</summary>
    public Guid? DefaultCarrierContactPersonId { get; set; }

    /// <summary>A combination or <see cref="DefaultVehicleIds"/>, never both.</summary>
    public Guid? DefaultVehicleCombinationId { get; set; }

    /// <summary>Ordered; duplicates are dropped.</summary>
    [MaxLength(PlanningExecutionConsts.MaxVehiclesPerTrip)]
    public List<Guid> DefaultVehicleIds { get; set; } = new();

    /// <summary>Ordered; duplicates are dropped.</summary>
    [MaxLength(PlanningExecutionConsts.MaxDriversPerTrip)]
    public List<Guid> DefaultDriverIds { get; set; } = new();

    public Guid? DefaultDepartureRelationId { get; set; }

    /// <summary>Needs <see cref="DefaultDepartureRelationId"/> and must be one of its addresses.</summary>
    public Guid? DefaultDepartureAddressId { get; set; }

    /// <summary>Local time at the departure address.</summary>
    public TimeOnly? DefaultDepartureTime { get; set; }

    public Guid? DefaultArrivalRelationId { get; set; }

    /// <summary>Needs <see cref="DefaultArrivalRelationId"/> and must be one of its addresses.</summary>
    public Guid? DefaultArrivalAddressId { get; set; }

    public StopCompositionMode DefaultStopCompositionMode { get; set; } = StopCompositionMode.MergeByLocationAndActivity;

    public FirstLastStopRule DefaultFirstLastStopRule { get; set; } = FirstLastStopRule.None;
}

public class TransportTripTypeUpdateDto : TransportTripTypeCreateDto, IReferenceDataUpdateDto
{
    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

public interface ITransportTripTypeAppService : IApplicationService
{
    Task<GridResponse<TransportTripTypeRowDto>> GetListAsync(ArchivableListRequestDto input);
    Task<TransportTripTypeDto> GetAsync(Guid id);
    Task<List<ReferenceDataLookupDto>> GetLookupAsync(string? term = null, int take = ReferenceDataConsts.MaxLookupTake);
    Task<TransportTripTypeDto> CreateAsync(TransportTripTypeCreateDto input);
    Task<TransportTripTypeDto> UpdateAsync(Guid id, TransportTripTypeUpdateDto input);
    Task DeleteAsync(Guid id);
    Task<ReferenceDataBulkDeleteResultDto> BulkDeleteAsync(ArchivableBulkDeleteRequestDto input);
    Task SetDefaultAsync(Guid id);
}
