using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.MasterData.Reference;
using OpenTms.MasterData.Relations;
using OpenTms.MasterData.Transport;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

namespace OpenTms.MasterData.Published;

/// <summary>
/// See <see cref="IMasterDataMatchQuery"/> (design 72 "Resolution"). In-process only; every read is tenant-filtered by the
/// data filter, excludes archived relations and soft-deleted rows and is bounded. The strong keys use the existing
/// indexes: relation code, VAT and chamber of commerce number (unique per tenant), relation e-mail (TenantId, Email,
/// added by design 72) and contact-person e-mail, the postcode key of an address, the relation's search key (B-tree and
/// trigram). Two weak steps scan: the e-mail domain (only when no exact e-mail matched) and the similar names (only when
/// nothing stronger matched). Names, codes and the address fields that tell candidates apart leave the module; no
/// e-mail address does (constitution rule 5).
/// </summary>
// Published in-process reader: Integrations' pipeline submits intakes from a Hangfire job without a user, so it is anonymous; the HTTP-facing caller authorizes.
[RemoteService(IsEnabled = false)]
[AllowAnonymous]
public class MasterDataMatchQuery : MasterDataAppServiceBase, IMasterDataMatchQuery
{
    /// <summary>The rows a single key read takes before the candidates are ranked in memory.</summary>
    private const int ScanLimit = 50;

    private readonly IRepository<Relation, Guid> _relations;
    private readonly IRepository<RelationAddress, Guid> _addresses;
    private readonly IRepository<RelationContactPerson, Guid> _contactPersons;
    private readonly IRepository<Country, Guid> _countries;

    public MasterDataMatchQuery(
        IRepository<Relation, Guid> relations,
        IRepository<RelationAddress, Guid> addresses,
        IRepository<RelationContactPerson, Guid> contactPersons,
        IRepository<Country, Guid> countries)
    {
        _relations = relations;
        _addresses = addresses;
        _contactPersons = contactPersons;
        _countries = countries;
    }

    // ── relations ──────────────────────────────────────────────────────────────

    public virtual async Task<List<RelationMatchDto>> FindRelationsAsync(RelationMatchQuery query)
    {
        Check.NotNull(query, nameof(query));
        var found = new List<(Guid Id, RelationMatchKind Kind)>();
        var relations = await _relations.GetQueryableAsync();
        var active = relations.Where(r => !r.IsArchived);

        var code = MasterDataCodes.Normalize(Bounded(query.Code));
        if (code != null)
        {
            found.AddRange((await AsyncExecuter.ToListAsync(active.Where(r => r.Code == code).Select(r => r.Id).Take(ScanLimit))).Select(id => (id, RelationMatchKind.Code)));
        }

        var vat = MasterDataValues.NormalizeLegalNumber(Bounded(query.VatNumber));
        if (vat != null)
        {
            found.AddRange((await AsyncExecuter.ToListAsync(active.Where(r => r.VatNumber == vat).Select(r => r.Id).Take(ScanLimit))).Select(id => (id, RelationMatchKind.VatNumber)));
        }

        var coc = MasterDataValues.NormalizeLegalNumber(Bounded(query.ChamberOfCommerceNumber));
        if (coc != null)
        {
            found.AddRange((await AsyncExecuter.ToListAsync(active.Where(r => r.ChamberOfCommerceNumber == coc).Select(r => r.Id).Take(ScanLimit))).Select(id => (id, RelationMatchKind.ChamberOfCommerceNumber)));
        }

        var email = Bounded(query.EmailAddress)?.Trim();
        if (!string.IsNullOrEmpty(email) && email.Contains('@'))
        {
            var exact = await RelationIdsByEmailAsync(email);
            found.AddRange(exact.Select(id => (id, RelationMatchKind.EmailAddress)));
            if (exact.Count == 0)
            {
                /* The domain counts only when exactly one relation owns it (design 72): a shared mail provider owns many. */
                var byDomain = await RelationIdsByDomainAsync(email[(email.LastIndexOf('@') + 1)..].ToLowerInvariant());
                if (byDomain.Count == 1)
                {
                    found.Add((byDomain[0], RelationMatchKind.EmailDomain));
                }
            }
        }

        var nameKey = MasterDataValues.SearchKey(Bounded(query.Name));
        if (nameKey.Length > 0)
        {
            /* The search key is "name fullname code": a name match starts it; the exact check runs on the name parts. */
            var startsWith = await AsyncExecuter.ToListAsync(active.Where(r => r.SearchKey.StartsWith(nameKey))
                .Select(r => new { r.Id, r.Name, r.FullName }).Take(ScanLimit));
            var exactName = startsWith.Where(r => MasterDataValues.SearchKey(r.Name) == nameKey || MasterDataValues.SearchKey(r.FullName) == nameKey).Select(r => r.Id).ToList();
            found.AddRange(exactName.Select(id => (id, RelationMatchKind.Name)));
            if (found.Count == 0)
            {
                var similar = await AsyncExecuter.ToListAsync(active.Where(r => r.SearchKey.Contains(nameKey)).OrderBy(r => r.Name).Select(r => r.Id).Take(IMasterDataMatchQuery.MaxResults));
                found.AddRange(similar.Select(id => (id, RelationMatchKind.SimilarName)));
            }
        }

        var ranked = found.GroupBy(f => f.Id).Select(g => (Id: g.Key, Kind: g.Min(f => f.Kind))).OrderBy(f => f.Kind).Take(IMasterDataMatchQuery.MaxResults).ToList();
        return await DescribeRelationsAsync(ranked);
    }

    private async Task<List<Guid>> RelationIdsByEmailAsync(string email)
    {
        /* Stored as entered: the address as given and in lower case both use the (TenantId, Email) indexes. */
        var lower = email.ToLowerInvariant();
        var own = await AsyncExecuter.ToListAsync((await _relations.GetQueryableAsync())
            .Where(r => !r.IsArchived && r.Email != null && (r.Email == email || r.Email == lower)).Select(r => r.Id).Take(ScanLimit));
        var viaContacts = await AsyncExecuter.ToListAsync((await _contactPersons.GetQueryableAsync())
            .Where(c => c.Email != null && (c.Email == email || c.Email == lower)).Select(c => c.RelationId).Distinct().Take(ScanLimit));
        return await ActiveAsync(own.Concat(viaContacts).Distinct().ToList());
    }

    private async Task<List<Guid>> RelationIdsByDomainAsync(string domain)
    {
        if (domain.Length == 0)
        {
            return [];
        }

        var suffix = "@" + domain;
        var own = await AsyncExecuter.ToListAsync((await _relations.GetQueryableAsync())
            .Where(r => !r.IsArchived && r.Email != null && r.Email.ToLower().EndsWith(suffix)).Select(r => r.Id).Distinct().Take(2));
        var viaContacts = await AsyncExecuter.ToListAsync((await _contactPersons.GetQueryableAsync())
            .Where(c => c.Email != null && c.Email.ToLower().EndsWith(suffix)).Select(c => c.RelationId).Distinct().Take(2));
        return await ActiveAsync(own.Concat(viaContacts).Distinct().ToList());
    }

    private async Task<List<Guid>> ActiveAsync(List<Guid> relationIds)
    {
        if (relationIds.Count == 0)
        {
            return [];
        }

        return await AsyncExecuter.ToListAsync((await _relations.GetQueryableAsync()).Where(r => relationIds.Contains(r.Id) && !r.IsArchived).Select(r => r.Id));
    }

    private async Task<List<RelationMatchDto>> DescribeRelationsAsync(List<(Guid Id, RelationMatchKind Kind)> ranked)
    {
        if (ranked.Count == 0)
        {
            return [];
        }

        var ids = ranked.Select(r => r.Id).ToList();
        var relations = (await AsyncExecuter.ToListAsync((await _relations.GetQueryableAsync())
            .Where(r => ids.Contains(r.Id)).Select(r => new { r.Id, r.Name, r.Code }))).ToDictionary(r => r.Id);
        var cities = (await AsyncExecuter.ToListAsync((await _addresses.GetQueryableAsync())
                .Where(a => ids.Contains(a.RelationId)).Select(a => new { a.RelationId, a.AddressTypeId, a.City })))
            .GroupBy(a => a.RelationId)
            .ToDictionary(g => g.Key, g => (g.FirstOrDefault(a => a.AddressTypeId == null) ?? g.OrderBy(a => a.City).First()).City);

        return ranked.Where(r => relations.ContainsKey(r.Id)).Select(r =>
        {
            var relation = relations[r.Id];
            var city = cities.GetValueOrDefault(r.Id);
            return new RelationMatchDto
            {
                Id = r.Id,
                Name = relation.Name,
                Detail = string.IsNullOrEmpty(city) ? relation.Code : relation.Code + ", " + city,
                MatchKind = r.Kind
            };
        }).ToList();
    }

    // ── addresses ──────────────────────────────────────────────────────────────

    public virtual async Task<List<RelationAddressMatchDto>> FindRelationAddressesAsync(AddressMatchQuery query)
    {
        Check.NotNull(query, nameof(query));
        var found = new List<(RelationAddress Address, AddressMatchKind Kind)>();
        var addresses = await _addresses.GetQueryableAsync();
        if (query.RelationId != null)
        {
            var scope = query.RelationId.Value;
            addresses = addresses.Where(a => a.RelationId == scope);
        }

        Guid? countryId = null;
        var countryCode = MasterDataCodes.Normalize(Bounded(query.CountryCode));
        if (countryCode != null)
        {
            countryId = (await AsyncExecuter.ToListAsync((await _countries.GetQueryableAsync()).Where(c => c.IsoAlpha2 == countryCode).Select(c => (Guid?)c.Id))).FirstOrDefault();
        }

        var postalKey = MasterDataCodes.NormalizePostalCode(Bounded(query.PostalCode));
        if (postalKey.Length > 0)
        {
            var byPostal = await AsyncExecuter.ToListAsync(addresses.Where(a => a.PostalCodeKey == postalKey).Take(ScanLimit));
            if (countryCode != null)
            {
                /* A country that is not in the tenant's list matches nothing rather than everything. */
                byPostal = byPostal.Where(a => a.CountryId == countryId).ToList();
            }

            var houseNumber = HouseNumberKey(Bounded(query.HouseNumber));
            foreach (var address in byPostal)
            {
                var same = houseNumber.Length > 0 && HouseNumberKey(address.HouseNumber + address.HouseNumberSuffix) == houseNumber;
                found.Add((address, same ? AddressMatchKind.PostalCodeAndHouseNumber : AddressMatchKind.PostalCode));
            }
        }

        var nameKey = MasterDataValues.SearchKey(Bounded(query.Name));
        var cityKey = MasterDataValues.SearchKey(Bounded(query.City));
        if (nameKey.Length > 0 && cityKey.Length > 0)
        {
            var relationIds = await AsyncExecuter.ToListAsync((await _relations.GetQueryableAsync())
                .Where(r => !r.IsArchived && r.SearchKey.StartsWith(nameKey)).Select(r => new { r.Id, r.Name, r.FullName }).Take(ScanLimit));
            var named = relationIds.Where(r => MasterDataValues.SearchKey(r.Name) == nameKey || MasterDataValues.SearchKey(r.FullName) == nameKey).Select(r => r.Id).ToList();
            if (named.Count > 0)
            {
                var ofNamed = await AsyncExecuter.ToListAsync(addresses.Where(a => named.Contains(a.RelationId)).Take(ScanLimit));
                found.AddRange(ofNamed.Where(a => MasterDataValues.SearchKey(a.City) == cityKey).Select(a => (a, AddressMatchKind.NameAndCity)));
            }
        }

        var streetKey = MasterDataValues.SearchKey(Bounded(query.Street));
        if (query.RelationId != null && streetKey.Length > 0 && cityKey.Length > 0)
        {
            /* Within one relation only: its handful of addresses, never a scan of every street. */
            var ofRelation = await AsyncExecuter.ToListAsync(addresses.Take(ScanLimit));
            found.AddRange(ofRelation.Where(a => MasterDataValues.SearchKey(a.Street) == streetKey && MasterDataValues.SearchKey(a.City) == cityKey).Select(a => (a, AddressMatchKind.StreetAndCity)));
        }

        if (found.Count == 0 && nameKey.Length > 0)
        {
            var similar = await AsyncExecuter.ToListAsync((await _relations.GetQueryableAsync())
                .Where(r => !r.IsArchived && r.SearchKey.Contains(nameKey)).OrderBy(r => r.Name).Select(r => r.Id).Take(IMasterDataMatchQuery.MaxResults));
            if (similar.Count > 0)
            {
                var ofSimilar = await AsyncExecuter.ToListAsync(addresses.Where(a => similar.Contains(a.RelationId)).Take(ScanLimit));
                if (cityKey.Length > 0)
                {
                    ofSimilar = ofSimilar.Where(a => MasterDataValues.SearchKey(a.City) == cityKey).ToList();
                }

                found.AddRange(ofSimilar.Select(a => (a, AddressMatchKind.SimilarName)));
            }
        }

        var ranked = found.GroupBy(f => f.Address.Id).Select(g => (g.First().Address, Kind: g.Min(f => f.Kind))).OrderBy(f => f.Kind).ToList();
        return await DescribeAddressesAsync(ranked);
    }

    private async Task<List<RelationAddressMatchDto>> DescribeAddressesAsync(List<(RelationAddress Address, AddressMatchKind Kind)> ranked, IReadOnlyDictionary<Guid, double>? scores = null)
    {
        if (ranked.Count == 0)
        {
            return [];
        }

        var relationIds = ranked.Select(r => r.Address.RelationId).Distinct().ToList();
        var relations = (await AsyncExecuter.ToListAsync((await _relations.GetQueryableAsync())
            .Where(r => relationIds.Contains(r.Id) && !r.IsArchived).Select(r => new { r.Id, r.Name }))).ToDictionary(r => r.Id, r => r.Name);
        var countryIds = ranked.Select(r => r.Address.CountryId).Distinct().ToList();
        var countries = (await AsyncExecuter.ToListAsync((await _countries.GetQueryableAsync())
            .Where(c => countryIds.Contains(c.Id)).Select(c => new { c.Id, c.IsoAlpha2 }))).ToDictionary(c => c.Id, c => c.IsoAlpha2);

        return ranked
            .Where(r => relations.ContainsKey(r.Address.RelationId))
            .Take(IMasterDataMatchQuery.MaxResults)
            .Select(r =>
            {
                var a = r.Address;
                var houseNumber = string.IsNullOrEmpty(a.HouseNumber) ? a.HouseNumberSuffix : a.HouseNumber + a.HouseNumberSuffix;
                var street = string.IsNullOrEmpty(houseNumber) ? a.Street : a.Street + " " + houseNumber;
                return new RelationAddressMatchDto
                {
                    Id = a.Id,
                    RelationId = a.RelationId,
                    Name = relations[a.RelationId],
                    Detail = street + ", " + a.PostalCode + " " + a.City,
                    PostalCode = a.PostalCode,
                    HouseNumber = houseNumber,
                    City = a.City,
                    CountryCode = countries.GetValueOrDefault(a.CountryId),
                    MatchKind = r.Kind,
                    Similarity = scores != null && scores.TryGetValue(a.Id, out var score) ? score : null
                };
            })
            .ToList();
    }

    // ── transport units, products, temperatures ────────────────────────────────

    public virtual async Task<List<ReferenceNameMatchDto>> FindReferencesByNameAsync(ReferenceNameMatchQuery query)
    {
        Check.NotNull(query, nameof(query));
        var texts = (query.Names ?? []).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).Distinct(StringComparer.Ordinal).ToList();
        if (texts.Count > IMasterDataMatchQuery.MaxNames)
        {
            throw new BusinessException(MasterDataErrorCodes.TooManyRows).WithData("max", IMasterDataMatchQuery.MaxNames);
        }

        if (texts.Count == 0)
        {
            return [];
        }

        var lowered = texts.Select(t => Bounded(t)!.ToLowerInvariant()).Distinct().ToList();
        /* Case-insensitive on the stored name; the kinds hold tens to a few thousand rows per tenant. */
        var rows = query.Kind switch
        {
            MasterDataReferenceKind.TransportUnit => await AsyncExecuter.ToListAsync((await LazyServiceProvider.LazyGetRequiredService<IRepository<TransportUnit, Guid>>().GetQueryableAsync())
                .Where(u => !u.IsArchived && lowered.Contains(u.Name.ToLower())).Select(u => new { u.Id, u.Name }).Take(ScanLimit)),
            MasterDataReferenceKind.Product => await AsyncExecuter.ToListAsync((await LazyServiceProvider.LazyGetRequiredService<IRepository<Product, Guid>>().GetQueryableAsync())
                .Where(p => !p.IsArchived && lowered.Contains(p.Name.ToLower())).Select(p => new { p.Id, p.Name }).Take(ScanLimit)),
            MasterDataReferenceKind.Temperature => await AsyncExecuter.ToListAsync((await LazyServiceProvider.LazyGetRequiredService<IRepository<Temperature, Guid>>().GetQueryableAsync())
                .Where(t => !t.IsArchived && lowered.Contains(t.Name.ToLower())).Select(t => new { t.Id, t.Name }).Take(ScanLimit)),
            _ => throw new BusinessException(MasterDataErrorCodes.InvalidCode).WithData("field", "kind").WithData("value", query.Kind.ToString())
        };

        return rows
            .SelectMany(r => texts.Where(t => string.Equals(t, r.Name.Trim(), StringComparison.OrdinalIgnoreCase))
                .Select(t => new ReferenceNameMatchDto { Id = r.Id, Name = r.Name, MatchedText = t }))
            .ToList();
    }

    public virtual async Task<List<string>> GetReferenceNamesAsync(MasterDataReferenceKind kind)
    {
        var take = IMasterDataMatchQuery.MaxChoices;
        return kind switch
        {
            MasterDataReferenceKind.TransportUnit => await AsyncExecuter.ToListAsync((await LazyServiceProvider.LazyGetRequiredService<IRepository<TransportUnit, Guid>>().GetQueryableAsync())
                .Where(u => !u.IsArchived).OrderBy(u => u.Name).Select(u => u.Name).Take(take)),
            MasterDataReferenceKind.Product => await AsyncExecuter.ToListAsync((await LazyServiceProvider.LazyGetRequiredService<IRepository<Product, Guid>>().GetQueryableAsync())
                .Where(p => !p.IsArchived).OrderBy(p => p.Name).Select(p => p.Name).Take(take)),
            MasterDataReferenceKind.Temperature => await AsyncExecuter.ToListAsync((await LazyServiceProvider.LazyGetRequiredService<IRepository<Temperature, Guid>>().GetQueryableAsync())
                .Where(t => !t.IsArchived).OrderBy(t => t.Name).Select(t => t.Name).Take(take)),
            _ => throw new BusinessException(MasterDataErrorCodes.InvalidCode).WithData("field", "kind").WithData("value", kind.ToString())
        };
    }

    // ── similar names (design 72 build note 25) ────────────────────────────────

    /// <summary>The rows one prefilter read takes (per read; two reads per name).</summary>
    private const int SimilarScanLimit = 100;

    /// <summary>The words of a name the prefilter searches by, longest first.</summary>
    private const int MaxTokens = 6;

    /// <summary>The transport units, products or temperatures one similar-name read scores at most.</summary>
    private const int ReferenceScanLimit = 2000;

    private sealed record SimilarRelationRow(Guid Id, string Name, string? FullName, string Code, string? VatNumber, string? ChamberOfCommerceNumber, string? Email);

    public virtual async Task<List<SimilarRelationDto>> FindSimilarRelationsAsync(SimilarRelationQuery query)
    {
        Check.NotNull(query, nameof(query));
        var name = Bounded(query.Name);
        if (name == null)
        {
            return [];
        }

        var vat = MasterDataValues.NormalizeLegalNumber(Bounded(query.VatNumber));
        var coc = MasterDataValues.NormalizeLegalNumber(Bounded(query.ChamberOfCommerceNumber));
        var domain = EmailDomain(Bounded(query.EmailAddress));
        var scored = (await SimilarRelationRowsAsync(name))
            .Select(r => (Row: r, Score: NameSimilarity.Best(name, r.Name, r.FullName)))
            .Where(r => r.Score >= IMasterDataMatchQuery.MinSimilarity)
            .OrderByDescending(r => r.Score).ThenBy(r => r.Row.Name, StringComparer.Ordinal).ThenBy(r => r.Row.Id)
            .Take(IMasterDataMatchQuery.MaxResults)
            .ToList();
        if (scored.Count == 0)
        {
            return [];
        }

        var ids = scored.Select(s => s.Row.Id).ToList();
        var addresses = await AsyncExecuter.ToListAsync((await _addresses.GetQueryableAsync())
            .Where(a => ids.Contains(a.RelationId)).Select(a => new { a.RelationId, a.AddressTypeId, a.City }));
        var byRelation = addresses.GroupBy(a => a.RelationId).ToDictionary(g => g.Key, g => g.ToList());

        return scored.Select(s =>
        {
            var own = byRelation.GetValueOrDefault(s.Row.Id) ?? [];
            var city = (own.FirstOrDefault(a => a.AddressTypeId == null) ?? own.OrderBy(a => a.City, StringComparer.Ordinal).FirstOrDefault())?.City;
            /* A key the document gives that the relation holds differently rules the candidate out (PO decision 2026-11-03). */
            var conflicts = (vat != null && s.Row.VatNumber != null && s.Row.VatNumber != vat)
                            || (coc != null && s.Row.ChamberOfCommerceNumber != null && s.Row.ChamberOfCommerceNumber != coc)
                            || (domain != null && EmailDomain(s.Row.Email) is { } relationDomain && relationDomain != domain);
            return new SimilarRelationDto
            {
                Id = s.Row.Id,
                Name = s.Row.Name,
                Detail = string.IsNullOrEmpty(city) ? s.Row.Code : s.Row.Code + ", " + city,
                Similarity = s.Score,
                ConflictsWithKeys = conflicts,
                HasDefaultAddress = own.Any(a => a.AddressTypeId == null)
            };
        }).ToList();
    }

    public virtual async Task<List<RelationAddressMatchDto>> FindSimilarRelationAddressesAsync(SimilarAddressQuery query)
    {
        Check.NotNull(query, nameof(query));
        var name = Bounded(query.Name);
        var postalKey = MasterDataCodes.NormalizePostalCode(Bounded(query.PostalCode));
        var cityKey = MasterDataValues.SearchKey(Bounded(query.City));
        if (name == null || (postalKey.Length == 0 && cityKey.Length == 0))
        {
            return [];
        }

        Guid? countryId = null;
        var countryCode = MasterDataCodes.Normalize(Bounded(query.CountryCode));
        if (countryCode != null)
        {
            countryId = (await AsyncExecuter.ToListAsync((await _countries.GetQueryableAsync()).Where(c => c.IsoAlpha2 == countryCode).Select(c => (Guid?)c.Id))).FirstOrDefault();
            if (countryId == null)
            {
                return []; /* a country that is not in the tenant's list matches nothing rather than everything */
            }
        }

        /* Two bounded reads: the addresses at the postal code (indexed), and the addresses of the relations with a similar
         * name (the relation prefilter, then the relations' own addresses). A candidate must share the postal code or the city. */
        var addresses = await _addresses.GetQueryableAsync();
        var found = new Dictionary<Guid, RelationAddress>();
        if (postalKey.Length > 0)
        {
            foreach (var address in await AsyncExecuter.ToListAsync(addresses.Where(a => a.PostalCodeKey == postalKey).OrderBy(a => a.Id).Take(ScanLimit)))
            {
                found[address.Id] = address;
            }
        }

        var similarIds = (await SimilarRelationRowsAsync(name))
            .Select(r => (r.Id, Score: NameSimilarity.Best(name, r.Name, r.FullName)))
            .Where(r => r.Score >= IMasterDataMatchQuery.MinSimilarity)
            .OrderByDescending(r => r.Score).ThenBy(r => r.Id)
            .Take(ScanLimit)
            .Select(r => r.Id)
            .ToList();
        if (similarIds.Count > 0)
        {
            foreach (var address in await AsyncExecuter.ToListAsync(addresses.Where(a => similarIds.Contains(a.RelationId)).OrderBy(a => a.Id).Take(ScanLimit * 2)))
            {
                found[address.Id] = address;
            }
        }

        var places = found.Values
            .Where(a => countryId == null || a.CountryId == countryId)
            .Where(a => (postalKey.Length > 0 && a.PostalCodeKey == postalKey) || (cityKey.Length > 0 && MasterDataValues.SearchKey(a.City) == cityKey))
            .ToList();
        if (places.Count == 0)
        {
            return [];
        }

        var relationIds = places.Select(a => a.RelationId).Distinct().ToList();
        var names = (await AsyncExecuter.ToListAsync((await _relations.GetQueryableAsync())
            .Where(r => relationIds.Contains(r.Id) && !r.IsArchived).Select(r => new { r.Id, r.Name, r.FullName }))).ToDictionary(r => r.Id);
        var scores = new Dictionary<Guid, double>();
        foreach (var address in places.Where(a => names.ContainsKey(a.RelationId)))
        {
            var relation = names[address.RelationId];
            var score = NameSimilarity.Best(name, relation.Name, relation.FullName);
            if (score >= IMasterDataMatchQuery.MinSimilarity)
            {
                scores[address.Id] = score;
            }
        }

        var ranked = places
            .Where(a => scores.ContainsKey(a.Id))
            .OrderByDescending(a => scores[a.Id]).ThenBy(a => a.Id)
            .Take(IMasterDataMatchQuery.MaxResults)
            .Select(a => (a, AddressMatchKind.SimilarName))
            .ToList();
        return await DescribeAddressesAsync(ranked, scores);
    }

    public virtual async Task<List<ReferenceNameMatchDto>> FindSimilarReferencesAsync(ReferenceNameMatchQuery query)
    {
        Check.NotNull(query, nameof(query));
        var texts = (query.Names ?? []).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => Bounded(n)!).Distinct(StringComparer.Ordinal).ToList();
        if (texts.Count > IMasterDataMatchQuery.MaxNames)
        {
            throw new BusinessException(MasterDataErrorCodes.TooManyRows).WithData("max", IMasterDataMatchQuery.MaxNames);
        }

        if (texts.Count == 0)
        {
            return [];
        }

        /* These kinds hold tens to a few thousand rows per tenant: one bounded read of id and name, scored in memory. */
        var take = ReferenceScanLimit;
        var rows = query.Kind switch
        {
            MasterDataReferenceKind.TransportUnit => await AsyncExecuter.ToListAsync((await LazyServiceProvider.LazyGetRequiredService<IRepository<TransportUnit, Guid>>().GetQueryableAsync())
                .Where(u => !u.IsArchived).OrderBy(u => u.Name).Select(u => new { u.Id, u.Name }).Take(take)),
            MasterDataReferenceKind.Product => await AsyncExecuter.ToListAsync((await LazyServiceProvider.LazyGetRequiredService<IRepository<Product, Guid>>().GetQueryableAsync())
                .Where(p => !p.IsArchived).OrderBy(p => p.Name).Select(p => new { p.Id, p.Name }).Take(take)),
            MasterDataReferenceKind.Temperature => await AsyncExecuter.ToListAsync((await LazyServiceProvider.LazyGetRequiredService<IRepository<Temperature, Guid>>().GetQueryableAsync())
                .Where(t => !t.IsArchived).OrderBy(t => t.Name).Select(t => new { t.Id, t.Name }).Take(take)),
            _ => throw new BusinessException(MasterDataErrorCodes.InvalidCode).WithData("field", "kind").WithData("value", query.Kind.ToString())
        };

        return texts.SelectMany(text => rows
                .Select(r => (Row: r, Score: NameSimilarity.Score(text, r.Name)))
                .Where(r => r.Score >= IMasterDataMatchQuery.MinSimilarity)
                .OrderByDescending(r => r.Score).ThenBy(r => r.Row.Name, StringComparer.Ordinal).ThenBy(r => r.Row.Id)
                .Take(IMasterDataMatchQuery.MaxResults)
                .Select(r => new ReferenceNameMatchDto { Id = r.Row.Id, Name = r.Row.Name, MatchedText = text, Similarity = r.Score }))
            .ToList();
    }

    /// <summary>
    /// The active relations a similar name may be: the words of the name (three characters or more, longest first, at most
    /// <see cref="MaxTokens"/>) contained in the search key, and — for a misspelt word — the search key's words starting
    /// with each word's first three characters. Two reads of at most <see cref="SimilarScanLimit"/> rows each, in name
    /// order, served by the search key's trigram index on PostgreSQL.
    /// </summary>
    private async Task<List<SimilarRelationRow>> SimilarRelationRowsAsync(string name)
    {
        var key = MasterDataValues.SearchKey(name);
        if (key.Length == 0)
        {
            return [];
        }

        var words = key.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var tokens = words.Where(t => t.Length >= 3).Distinct(StringComparer.Ordinal).OrderByDescending(t => t.Length).ThenBy(t => t, StringComparer.Ordinal).Take(MaxTokens).ToList();
        if (tokens.Count == 0)
        {
            tokens = [key];
        }

        var active = (await _relations.GetQueryableAsync()).Where(r => !r.IsArchived);
        var rows = new Dictionary<Guid, SimilarRelationRow>();
        foreach (var predicate in new[] { AnyContains(tokens, wordStart: false), AnyContains(tokens.Select(t => t[..Math.Min(3, t.Length)]).Distinct(StringComparer.Ordinal).ToList(), wordStart: true) })
        {
            var read = await AsyncExecuter.ToListAsync(active.Where(predicate).OrderBy(r => r.Name).ThenBy(r => r.Id)
                .Select(r => new SimilarRelationRow(r.Id, r.Name, r.FullName, r.Code, r.VatNumber, r.ChamberOfCommerceNumber, r.Email)).Take(SimilarScanLimit));
            foreach (var row in read)
            {
                rows[row.Id] = row;
            }
        }

        return rows.Values.ToList();
    }

    /// <summary><c>r.SearchKey</c> contains any of the terms (or, with <paramref name="wordStart"/>, has a word starting with one).</summary>
    private static Expression<Func<Relation, bool>> AnyContains(IReadOnlyList<string> terms, bool wordStart)
    {
        var parameter = Expression.Parameter(typeof(Relation), "r");
        var searchKey = Expression.Property(parameter, nameof(Relation.SearchKey));
        var contains = typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;
        var startsWith = typeof(string).GetMethod(nameof(string.StartsWith), [typeof(string)])!;
        Expression? body = null;
        foreach (var term in terms)
        {
            Expression test = wordStart
                ? Expression.OrElse(Expression.Call(searchKey, startsWith, Expression.Constant(term)), Expression.Call(searchKey, contains, Expression.Constant(" " + term)))
                : Expression.Call(searchKey, contains, Expression.Constant(term));
            body = body == null ? test : Expression.OrElse(body, test);
        }

        return Expression.Lambda<Func<Relation, bool>>(body ?? Expression.Constant(false), parameter);
    }

    /// <summary>The lower-case domain of an e-mail address; null without one.</summary>
    private static string? EmailDomain(string? email)
    {
        var text = email?.Trim();
        var at = text?.LastIndexOf('@') ?? -1;
        return at < 0 || at == text!.Length - 1 ? null : text[(at + 1)..].ToLowerInvariant();
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    /// <summary>The inputs are bounded by the contract's annotations; an in-process caller that skips validation is cut, never trusted.</summary>
    private static string? Bounded(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        return text.Length > IMasterDataMatchQuery.MaxInputLength ? text[..IMasterDataMatchQuery.MaxInputLength] : text;
    }

    /// <summary>"7 a", "7A" and "7-a" are the same house number.</summary>
    private static string HouseNumberKey(string? value)
        => new string((value ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}
