using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace OpenTms.MasterData.Published;

/// <summary>
/// The published, in-process lookup other modules use to validate and name Master Data references (designs 51 and 52,
/// module boundary rule 3: the stated synchronous call). Not exposed over HTTP. Any signed-in user may call it: it
/// returns only the id or code and the display name of rows of the caller's own tenant, so an Orders or Financial
/// administrator needs no Master Data permission to save a type that refers to a relation or a country. Inputs are
/// bounded to <see cref="MaxKeys"/>; unknown keys are simply absent from the result. Soft-deleted rows are absent too.
/// </summary>
public interface IMasterDataReferenceLookup : IApplicationService
{
    public const int MaxKeys = 500;

    Task<List<MasterDataReferenceDto>> GetRelationsAsync(IReadOnlyCollection<Guid> ids);

    Task<List<MasterDataReferenceDto>> GetRelationCategoriesAsync(IReadOnlyCollection<Guid> ids);

    Task<List<MasterDataReferenceDto>> GetGeographicalRegionsAsync(IReadOnlyCollection<Guid> ids);

    /// <summary>Countries by ISO 3166-1 alpha-2 code (assessment §3.7: other modules match on ISO codes).</summary>
    Task<List<MasterDataCodeReferenceDto>> GetCountriesAsync(IReadOnlyCollection<string> isoAlpha2Codes);

    /// <summary>Currencies by ISO 4217 code, with their active flag.</summary>
    Task<List<MasterDataCodeReferenceDto>> GetCurrenciesAsync(IReadOnlyCollection<string> isoCodes);
}

public class MasterDataReferenceDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsArchived { get; set; }
}

public class MasterDataCodeReferenceDto
{
    public Guid Id { get; set; }

    /// <summary>The ISO code, upper case.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}
