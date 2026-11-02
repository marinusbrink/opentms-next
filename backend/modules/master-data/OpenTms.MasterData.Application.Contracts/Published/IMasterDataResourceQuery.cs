using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OpenTms.MasterData.Fleet;
using Volo.Abp.Application.Services;

namespace OpenTms.MasterData.Published;

/// <summary>
/// The published, in-process resource query of design 69 (PO decision D6, module boundary rule 3: the stated
/// synchronous call): pages of vehicles and drivers for Planning's Scheduler endpoint and Resources pane, so Planning
/// composes its rows without a cross-schema join. Not exposed over HTTP; the HTTP-facing caller authorizes. Only ids,
/// names, codes and the licence plate leave the module (constitution rule 5); the tenant filter scopes every read.
/// Vehicles are sorted by normalised name, drivers by code; <see cref="MaxTake"/> rows per page at most.
/// </summary>
public interface IMasterDataResourceQuery : IApplicationService
{
    /// <summary>The largest page and the largest explicit id set.</summary>
    public const int MaxTake = 500;

    Task<MasterDataResourcePageDto<MasterDataVehicleResourceDto>> GetVehiclesAsync(MasterDataVehicleResourceQuery query);

    Task<MasterDataResourcePageDto<MasterDataDriverResourceDto>> GetDriversAsync(MasterDataDriverResourceQuery query);
}

public class MasterDataVehicleResourceQuery
{
    /// <summary>The vehicle type kinds to return; required and non-empty.</summary>
    public List<VehicleTypeKind> Kinds { get; set; } = new();

    /// <summary>Vehicles linked to any of these organisation units; empty means all.</summary>
    public List<Guid> OrganizationUnitIds { get; set; } = new();

    /// <summary>An optional explicit set of vehicles (at most <see cref="IMasterDataResourceQuery.MaxTake"/>); empty means no id filter.</summary>
    public List<Guid> Ids { get; set; } = new();

    /// <summary>Searched over the name and the licence plate (separators ignored).</summary>
    public string? Search { get; set; }

    public bool IncludeArchived { get; set; }

    public int Skip { get; set; }

    /// <summary>Clamped to 1..<see cref="IMasterDataResourceQuery.MaxTake"/>.</summary>
    public int Take { get; set; } = IMasterDataResourceQuery.MaxTake;
}

public class MasterDataVehicleResourceDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? LicencePlate { get; set; }

    public VehicleTypeKind Kind { get; set; }

    public string VehicleTypeName { get; set; } = string.Empty;

    public Guid? HomeDepotRelationId { get; set; }

    public Guid OwnerRelationId { get; set; }

    /// <summary>The default drivers in their configured order.</summary>
    public List<Guid> DefaultDriverIds { get; set; } = new();

    public List<Guid> OrganizationUnitIds { get; set; } = new();

    public bool IsArchived { get; set; }
}

public class MasterDataDriverResourceQuery
{
    /// <summary>An optional explicit set of drivers (at most <see cref="IMasterDataResourceQuery.MaxTake"/>); empty means no id filter.</summary>
    public List<Guid> Ids { get; set; } = new();

    /// <summary>Searched over the code and the name.</summary>
    public string? Search { get; set; }

    public bool IncludeArchived { get; set; }

    public int Skip { get; set; }

    /// <summary>Clamped to 1..<see cref="IMasterDataResourceQuery.MaxTake"/>.</summary>
    public int Take { get; set; } = IMasterDataResourceQuery.MaxTake;
}

/// <summary>A driver as the planboard shows it: no phone, e-mail or qualifications (rule 5).</summary>
public class MasterDataDriverResourceDto
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    /// <summary>The full name.</summary>
    public string Name { get; set; } = string.Empty;

    public Guid? EmployerRelationId { get; set; }

    public bool IsArchived { get; set; }
}

public class MasterDataResourcePageDto<T>
{
    public List<T> Items { get; set; } = new();

    public long TotalCount { get; set; }
}
