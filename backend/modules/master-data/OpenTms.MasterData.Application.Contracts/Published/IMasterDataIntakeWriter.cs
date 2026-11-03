using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.MasterData.Relations;
using Volo.Abp.Application.Services;

namespace OpenTms.MasterData.Published;

/// <summary>
/// The published, in-process writer of design 72 build note 27 (PO decision 2026-11-03; module boundary rule 3: the stated
/// synchronous call from Orders' order intake, which needs the new ids for the order it creates). Creates what an order
/// document names and Master Data does not know: a relation (a customer or the place of a stop) with its address, an
/// address on an existing relation, a transport unit, product or temperature. Runs in the caller's unit of work and
/// tenant (the data filter scopes every read), with the validation and uniqueness rules of the normal create services;
/// a value those rules refuse is left out where it is optional (VAT, chamber of commerce number, e-mail address) and
/// refuses the write where it is required (<see cref="IntakeWriteResultDto.RefusedCode"/>), never throws for received
/// data. Every row written carries the "created by order intake" marker: a relation the external identifier
/// <see cref="SourceName"/> with the caller's reference, every row the description text <c>MasterData:CreatedByOrderIntake</c>.
/// Not exposed over HTTP; anonymous like the match query (the pipeline runs without a user).
/// </summary>
public interface IMasterDataIntakeWriter : IApplicationService
{
    /// <summary>The external-identifier source of a relation created by the order intake.</summary>
    public const string SourceName = "OrderIntake";

    /// <summary>The refusal codes of <see cref="IntakeWriteResultDto.RefusedCode"/>.</summary>
    public const string RefusedInvalid = "invalid";

    public const string RefusedCountryUnknown = "countryUnknown";

    public const string RefusedArchived = "archived";

    public const string RefusedDefaultAddressTaken = "defaultAddressTaken";

    public const string RefusedNotFound = "notFound";

    /// <summary>
    /// A new relation with the given roles, name and keys, and its default address when given; the relation code is
    /// derived from the name (letters and digits, upper case, at most 24; "-2", "-3", … when taken). Idempotent on
    /// <see cref="IntakeRelationCreateDto.SourceReference"/>: a relation that already carries it is returned unchanged.
    /// </summary>
    Task<IntakeWriteResultDto> CreateRelationAsync(IntakeRelationCreateDto input);

    /// <summary>The address as the relation's default address; refused when the relation has a default address already.</summary>
    Task<IntakeWriteResultDto> AddRelationAddressAsync(IntakeRelationAddressAddDto input);

    /// <summary>
    /// A transport unit, product or temperature with the name (unique per tenant; these rows have no code) and safe
    /// defaults (nothing else set: not stackable, not exchangeable, no ADR, no dimensions, no range). An active row with
    /// the same name (ignoring case) is returned instead (Created = false); an archived one refuses.
    /// </summary>
    Task<IntakeWriteResultDto> CreateReferenceAsync(IntakeReferenceCreateDto input);
}

/// <summary>An address as a document gives it.</summary>
public class IntakeAddressInputDto
{
    [MaxLength(IMasterDataMatchQuery.MaxInputLength)]
    public string? Street { get; set; }

    [MaxLength(IMasterDataMatchQuery.MaxInputLength)]
    public string? HouseNumber { get; set; }

    [MaxLength(IMasterDataMatchQuery.MaxInputLength)]
    public string? PostalCode { get; set; }

    [MaxLength(IMasterDataMatchQuery.MaxInputLength)]
    public string? City { get; set; }

    /// <summary>ISO 3166-1 alpha-2; empty takes the tenant's default country, else the Netherlands.</summary>
    [MaxLength(IMasterDataMatchQuery.MaxInputLength)]
    public string? CountryCode { get; set; }
}

public class IntakeRelationCreateDto
{
    [Required, MaxLength(IMasterDataMatchQuery.MaxInputLength)]
    public string Name { get; set; } = string.Empty;

    public RelationRoles Roles { get; set; } = RelationRoles.Customer;

    [MaxLength(IMasterDataMatchQuery.MaxInputLength)]
    public string? EmailAddress { get; set; }

    [MaxLength(IMasterDataMatchQuery.MaxInputLength)]
    public string? VatNumber { get; set; }

    [MaxLength(IMasterDataMatchQuery.MaxInputLength)]
    public string? ChamberOfCommerceNumber { get; set; }

    public IntakeAddressInputDto? Address { get; set; }

    /// <summary>The caller's reference (the intake and its field), kept as the relation's external identifier.</summary>
    [Required, MaxLength(128)]
    public string SourceReference { get; set; } = string.Empty;
}

public class IntakeRelationAddressAddDto
{
    public Guid RelationId { get; set; }

    [Required]
    public IntakeAddressInputDto Address { get; set; } = new();
}

public class IntakeReferenceCreateDto
{
    /// <summary>TransportUnit, Product or Temperature.</summary>
    public MasterDataReferenceKind Kind { get; set; }

    [Required, MaxLength(IMasterDataMatchQuery.MaxInputLength)]
    public string Name { get; set; } = string.Empty;
}

/// <summary>What the writer did: the row's id (and the address's), its name and detail, or why it wrote nothing.</summary>
public class IntakeWriteResultDto
{
    /// <summary>A new row was written; false when an existing one was returned or the write was refused.</summary>
    public bool Created { get; set; }

    /// <summary>The relation, transport unit, product or temperature; null when refused.</summary>
    public Guid? Id { get; set; }

    public Guid? RelationAddressId { get; set; }

    public string? Name { get; set; }

    /// <summary>The address as one line, e.g. "Stationsplein 7A, 1012AB Amsterdam".</summary>
    public string? Detail { get; set; }

    /// <summary>One of the <c>Refused…</c> codes of <see cref="IMasterDataIntakeWriter"/>; null when written or found.</summary>
    public string? RefusedCode { get; set; }

    public bool Succeeded => Id != null;
}
