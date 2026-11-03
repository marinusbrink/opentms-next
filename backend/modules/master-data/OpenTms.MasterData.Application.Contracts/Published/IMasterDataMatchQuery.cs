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

    /* Design 72 "Resolution": transport units, products and temperatures have neither a code nor an id in a document,
     * only a name; and the AI's choice list holds the transport units' names. */

    /// <summary>The texts one name match may hold; the names of one choice list at most.</summary>
    public const int MaxNames = 100;

    public const int MaxChoices = 500;

    /// <summary>
    /// Transport units, products or temperatures (<see cref="ReferenceNameMatchQuery.Kind"/>) whose name equals one of the
    /// texts, ignoring case and surrounding spaces; archived and soft-deleted rows excluded. Each result says which text it
    /// matched. Other kinds are refused.
    /// </summary>
    Task<List<ReferenceNameMatchDto>> FindReferencesByNameAsync(ReferenceNameMatchQuery query);

    /// <summary>The names of the active transport units, products or temperatures, sorted, at most <see cref="MaxChoices"/>.</summary>
    Task<List<string>> GetReferenceNamesAsync(MasterDataReferenceKind kind);

    /* Design 72 build note 25 (PO decision 2026-11-03): fat-finger tolerance. The candidates a name is similar to, scored by
     * NameSimilarity (the higher of pg_trgm's trigram similarity and the edit similarity); the caller applies its threshold and margin. Candidates
     * are fetched through a bounded token prefilter, never by scanning a table. */

    /// <summary>Candidates below this similarity are never returned.</summary>
    public const double MinSimilarity = 0.3;

    /// <summary>
    /// Active relations whose name or full name is similar to <see cref="SimilarRelationQuery.Name"/>, best first, at most
    /// <see cref="MaxResults"/>; each says whether its VAT number, chamber of commerce number or e-mail domain conflicts
    /// with the ones the query gives, and whether it already has a default address (address type none).
    /// </summary>
    Task<List<SimilarRelationDto>> FindSimilarRelationsAsync(SimilarRelationQuery query);

    /// <summary>
    /// Addresses of active relations with the same postal code or in the same city as the query, scored by the similarity
    /// of their relation's name (or full name) to <see cref="SimilarAddressQuery.Name"/>, best first, at most
    /// <see cref="MaxResults"/>; <see cref="RelationAddressMatchDto.MatchKind"/> is SimilarName.
    /// </summary>
    Task<List<RelationAddressMatchDto>> FindSimilarRelationAddressesAsync(SimilarAddressQuery query);

    /// <summary>
    /// Per text, the active transport units, products or temperatures with a similar name, best first, at most
    /// <see cref="MaxResults"/> per text, each with its <see cref="ReferenceNameMatchDto.Similarity"/>.
    /// </summary>
    Task<List<ReferenceNameMatchDto>> FindSimilarReferencesAsync(ReferenceNameMatchQuery query);
}

/// <summary>A party's name as a document gives it, with the keys that rule a similar candidate out.</summary>
public class SimilarRelationQuery
{
    [MaxLength(IMasterDataMatchQuery.MaxInputLength)]
    public string? Name { get; set; }

    [MaxLength(IMasterDataMatchQuery.MaxInputLength)]
    public string? VatNumber { get; set; }

    [MaxLength(IMasterDataMatchQuery.MaxInputLength)]
    public string? ChamberOfCommerceNumber { get; set; }

    /// <summary>Only its domain is compared with the relation's own e-mail address.</summary>
    [MaxLength(IMasterDataMatchQuery.MaxInputLength)]
    public string? EmailAddress { get; set; }
}

public class SimilarRelationDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>The code and the city of the relation's default address.</summary>
    public string? Detail { get; set; }

    /// <summary>0–1, the best of name and full name.</summary>
    public double Similarity { get; set; }

    /// <summary>The relation has a VAT number, chamber of commerce number or e-mail domain that differs from the query's.</summary>
    public bool ConflictsWithKeys { get; set; }

    /// <summary>The relation has an address without an address type (its default; a relation has at most one).</summary>
    public bool HasDefaultAddress { get; set; }
}

/// <summary>A place as a document gives it: the name to score, and the postal code or city a candidate must share.</summary>
public class SimilarAddressQuery
{
    [MaxLength(IMasterDataMatchQuery.MaxInputLength)]
    public string? Name { get; set; }

    [MaxLength(IMasterDataMatchQuery.MaxInputLength)]
    public string? PostalCode { get; set; }

    [MaxLength(IMasterDataMatchQuery.MaxInputLength)]
    public string? City { get; set; }

    /// <summary>ISO 3166-1 alpha-2; when given, only addresses in that country.</summary>
    [MaxLength(IMasterDataMatchQuery.MaxInputLength)]
    public string? CountryCode { get; set; }
}

/// <summary>Names as a document gives them, matched against one kind of Master Data row.</summary>
public class ReferenceNameMatchQuery
{
    /// <summary>TransportUnit, Product or Temperature.</summary>
    public MasterDataReferenceKind Kind { get; set; }

    [MaxLength(IMasterDataMatchQuery.MaxNames)]
    public List<string> Names { get; set; } = new();
}

public class ReferenceNameMatchDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>The text of the query this row matched, as it was given.</summary>
    public string MatchedText { get; set; } = string.Empty;

    /// <summary>0–1 from <see cref="IMasterDataMatchQuery.FindSimilarReferencesAsync"/>; null from the exact name match.</summary>
    public double? Similarity { get; set; }
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

    /// <summary>0–1 from <see cref="IMasterDataMatchQuery.FindSimilarRelationAddressesAsync"/>; null from the other reads.</summary>
    public double? Similarity { get; set; }
}
