using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace OpenTms.MasterData.Published;

/// <summary>
/// The published, in-process match query of design 72 (module boundary rule 3: the stated synchronous call from Orders'
/// intake resolver): finds relations and relation addresses by what a document says. Not exposed over HTTP; the
/// HTTP-facing caller authorizes. Both reads are tenant-filtered, exclude archived and soft-deleted rows, take bounded
/// inputs and return at most <see cref="MaxResults"/> candidates, best match first, each with the kind of match. Only
/// ids, names and the address fields that tell candidates apart leave the module (constitution rule 5).
/// </summary>
public interface IMasterDataMatchQuery : IApplicationService
{
    public const int MaxResults = 10;

    /// <summary>The longest text a match input may hold.</summary>
    public const int MaxInputLength = 320;

    /// <summary>Relations by code, VAT number, chamber of commerce number, e-mail address (or its domain) or name.</summary>
    Task<List<RelationMatchDto>> FindRelationsAsync(RelationMatchQuery query);

    /// <summary>Relation addresses by postal code and house number (and country), or name and city, optionally within one relation.</summary>
    Task<List<RelationAddressMatchDto>> FindRelationAddressesAsync(AddressMatchQuery query);
}

/// <summary>What a document says about a party; every field optional, empty fields are not matched.</summary>
public class RelationMatchQuery
{
    [MaxLength(IMasterDataMatchQuery.MaxInputLength)]
    public string? Code { get; set; }

    [MaxLength(IMasterDataMatchQuery.MaxInputLength)]
    public string? VatNumber { get; set; }

    [MaxLength(IMasterDataMatchQuery.MaxInputLength)]
    public string? ChamberOfCommerceNumber { get; set; }

    /// <summary>Matched exactly against the relation's and its contact persons' e-mail addresses, then by domain.</summary>
    [MaxLength(IMasterDataMatchQuery.MaxInputLength)]
    public string? EmailAddress { get; set; }

    /// <summary>Matched on the normalised name; similar names are returned as weaker candidates.</summary>
    [MaxLength(IMasterDataMatchQuery.MaxInputLength)]
    public string? Name { get; set; }
}

/// <summary>What a document says about a place; every field optional, empty fields are not matched.</summary>
public class AddressMatchQuery
{
    /// <summary>Only addresses of this relation; null searches every relation.</summary>
    public Guid? RelationId { get; set; }

    [MaxLength(IMasterDataMatchQuery.MaxInputLength)]
    public string? PostalCode { get; set; }

    [MaxLength(IMasterDataMatchQuery.MaxInputLength)]
    public string? HouseNumber { get; set; }

    /// <summary>ISO 3166-1 alpha-2.</summary>
    [MaxLength(IMasterDataMatchQuery.MaxInputLength)]
    public string? CountryCode { get; set; }

    /// <summary>The relation's name or the address's name.</summary>
    [MaxLength(IMasterDataMatchQuery.MaxInputLength)]
    public string? Name { get; set; }

    [MaxLength(IMasterDataMatchQuery.MaxInputLength)]
    public string? City { get; set; }

    [MaxLength(IMasterDataMatchQuery.MaxInputLength)]
    public string? Street { get; set; }
}

/// <summary>How a relation candidate matched, strongest first.</summary>
public enum RelationMatchKind
{
    Code = 1,
    VatNumber = 2,
    ChamberOfCommerceNumber = 3,
    EmailAddress = 4,
    EmailDomain = 5,
    Name = 6,
    SimilarName = 7
}

/// <summary>How an address candidate matched, strongest first.</summary>
public enum AddressMatchKind
{
    PostalCodeAndHouseNumber = 1,
    NameAndCity = 2,
    PostalCode = 3,
    StreetAndCity = 4,
    SimilarName = 5
}

public class RelationMatchDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>What tells candidates apart: the code and the city of the relation's default address.</summary>
    public string? Detail { get; set; }

    public RelationMatchKind MatchKind { get; set; }
}

public class RelationAddressMatchDto
{
    public Guid Id { get; set; }

    public Guid RelationId { get; set; }

    /// <summary>The relation's name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The address as one line, e.g. "Stationsplein 7A, 1012AB Amsterdam".</summary>
    public string? Detail { get; set; }

    public string PostalCode { get; set; } = string.Empty;

    public string? HouseNumber { get; set; }

    public string City { get; set; } = string.Empty;

    /// <summary>ISO 3166-1 alpha-2.</summary>
    public string? CountryCode { get; set; }

    public AddressMatchKind MatchKind { get; set; }
}
