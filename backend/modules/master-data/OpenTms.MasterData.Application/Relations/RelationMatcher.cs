using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;

namespace OpenTms.MasterData.Relations;

/// <summary>
/// The one matching service of assessment §3.15. Exact keys first (code, VAT number, chamber-of-commerce
/// number), then normalised name and postcode in the country, then name only with at most ten candidates,
/// shortest search key first (the closest name). Archived relations never match; it never creates anything.
/// </summary>
public class RelationMatcher : IRelationMatcher, ITransientDependency
{
    private readonly IRepository<Relation, Guid> _relations;
    private readonly IRepository<RelationAddress, Guid> _addresses;
    private readonly IAsyncQueryableExecuter _executer;

    public RelationMatcher(IRepository<Relation, Guid> relations, IRepository<RelationAddress, Guid> addresses, IAsyncQueryableExecuter executer)
    {
        _relations = relations;
        _addresses = addresses;
        _executer = executer;
    }

    public async Task<RelationMatchResultDto> MatchAsync(RelationMatchRequestDto request)
    {
        var all = await _relations.GetQueryableAsync();
        var candidates = all.Where(r => !r.IsArchived);
        if (request.Roles is { } roles && roles != RelationRoles.None)
        {
            candidates = candidates.Where(r => (r.Roles & roles) != 0);
        }

        var code = MasterDataCodes.Normalize(request.Code);
        if (code != null)
        {
            var hit = await _executer.FirstOrDefaultAsync(candidates.Where(r => r.Code == code));
            if (hit != null)
            {
                return Result(hit, RelationMatchKind.Exact, 100);
            }
        }

        var vat = MasterDataValues.NormalizeLegalNumber(request.VatNumber);
        if (vat != null)
        {
            var hit = await _executer.FirstOrDefaultAsync(candidates.Where(r => r.VatNumber == vat));
            if (hit != null)
            {
                return Result(hit, RelationMatchKind.Exact, 100);
            }
        }

        var coc = MasterDataValues.NormalizeLegalNumber(request.ChamberOfCommerceNumber);
        if (coc != null)
        {
            var hit = await _executer.FirstOrDefaultAsync(candidates.Where(r => r.ChamberOfCommerceNumber == coc));
            if (hit != null)
            {
                return Result(hit, RelationMatchKind.Exact, 100);
            }
        }

        var nameKey = MasterDataValues.SearchKey(request.Name);
        if (nameKey.Length == 0)
        {
            return new RelationMatchResultDto();
        }

        var postalKey = MasterDataCodes.NormalizePostalCode(request.PostalCode);
        if (postalKey.Length > 0)
        {
            var addresses = await _addresses.GetQueryableAsync();
            var byPostal = addresses.Where(a => a.PostalCodeKey == postalKey);
            if (request.CountryId != null)
            {
                byPostal = byPostal.Where(a => a.CountryId == request.CountryId.Value);
            }

            var relationIds = byPostal.Select(a => a.RelationId);
            var hits = await _executer.ToListAsync(candidates
                .Where(r => relationIds.Contains(r.Id) && r.SearchKey.Contains(nameKey))
                .OrderBy(r => r.SearchKey.Length)
                .ThenBy(r => r.Name)
                .Take(MasterDataConsts.MaxMatchCandidates));
            if (hits.Count > 0)
            {
                return new RelationMatchResultDto { Matches = hits.Select(h => Match(h, RelationMatchKind.NameAndPostalCode, 80)).ToList() };
            }
        }

        var byName = await _executer.ToListAsync(candidates
            .Where(r => r.SearchKey.Contains(nameKey))
            .OrderBy(r => r.SearchKey.Length)
            .ThenBy(r => r.Name)
            .Take(MasterDataConsts.MaxMatchCandidates));

        return new RelationMatchResultDto
        {
            Matches = byName.Select(h => Match(h, RelationMatchKind.NameOnly, NameScore(nameKey, h.SearchKey))).ToList()
        };
    }

    /// <summary>40 for a weak containment, up to 60 when the searched name is most of the stored key.</summary>
    private static int NameScore(string nameKey, string searchKey)
        => searchKey.Length == 0 ? 40 : 40 + (int)Math.Round(20.0 * nameKey.Length / searchKey.Length);

    private static RelationMatchResultDto Result(Relation hit, RelationMatchKind kind, int score)
        => new() { Matches = [Match(hit, kind, score)] };

    private static RelationMatchDto Match(Relation r, RelationMatchKind kind, int score)
        => new() { RelationId = r.Id, Code = r.Code, Name = r.Name, Kind = kind, Score = score };
}
