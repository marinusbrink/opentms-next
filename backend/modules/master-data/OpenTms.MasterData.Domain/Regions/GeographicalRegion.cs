using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace OpenTms.MasterData.Regions;

public class GeographicalRegion : MasterDataAggregateRoot
{
    public string Name { get; private set; } = default!;

    public virtual ICollection<GeographicalRegionPostalCodeRange> PostalCodeRanges { get; private set; } = new List<GeographicalRegionPostalCodeRange>();

    protected GeographicalRegion()
    {
    }

    public GeographicalRegion(Guid id, Guid? tenantId, string name)
        : base(id, tenantId)
    {
        SetName(name);
    }

    public void SetName(string name) => Name = NormalizeName(name);

    /// <summary>Replaces the ranges: rows with a known id are updated, new rows added, the rest removed.</summary>
    public void SetPostalCodeRanges(IEnumerable<(Guid? Id, Guid CountryId, int? FromNumeric, int? TillNumeric, string? Prefix)> ranges, Func<Guid> newId)
    {
        var keep = new HashSet<Guid>();
        foreach (var range in ranges)
        {
            var existing = range.Id != null ? PostalCodeRanges.FirstOrDefault(r => r.Id == range.Id.Value) : null;
            if (existing == null)
            {
                existing = new GeographicalRegionPostalCodeRange(newId(), TenantId, Id);
                PostalCodeRanges.Add(existing);
            }

            existing.Set(range.CountryId, range.FromNumeric, range.TillNumeric, range.Prefix);
            keep.Add(existing.Id);
        }

        foreach (var stale in PostalCodeRanges.Where(r => !keep.Contains(r.Id)).ToList())
        {
            PostalCodeRanges.Remove(stale);
        }
    }
}

/// <summary>
/// One postal-code range of a region in one country: either a numeric range (from–till on the leading
/// digits of a postcode) or a prefix. Exactly one of the two forms (database check constraint).
/// </summary>
public class GeographicalRegionPostalCodeRange : Entity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public Guid GeographicalRegionId { get; private set; }

    public Guid CountryId { get; private set; }

    public int? FromNumeric { get; private set; }

    public int? TillNumeric { get; private set; }

    /// <summary>Stored normalised: upper-case, no spaces.</summary>
    public string? Prefix { get; private set; }

    protected GeographicalRegionPostalCodeRange()
    {
    }

    public GeographicalRegionPostalCodeRange(Guid id, Guid? tenantId, Guid geographicalRegionId)
        : base(id)
    {
        TenantId = tenantId;
        GeographicalRegionId = geographicalRegionId;
    }

    public void Set(Guid countryId, int? fromNumeric, int? tillNumeric, string? prefix)
    {
        var normalizedPrefix = MasterDataCodes.NormalizePostalCode(prefix);
        var hasPrefix = normalizedPrefix.Length > 0;
        var hasNumeric = fromNumeric != null || tillNumeric != null;

        var valid = hasPrefix
            ? !hasNumeric && normalizedPrefix.Length <= MasterDataConsts.MaxPostalCodePrefixLength
            : fromNumeric != null && tillNumeric != null && fromNumeric >= 0 && fromNumeric <= tillNumeric;

        if (!valid)
        {
            throw new BusinessException(MasterDataErrorCodes.InvalidPostalCodeRange)
                .WithData("from", fromNumeric?.ToString() ?? string.Empty)
                .WithData("till", tillNumeric?.ToString() ?? string.Empty)
                .WithData("prefix", prefix ?? string.Empty);
        }

        CountryId = countryId;
        FromNumeric = hasPrefix ? null : fromNumeric;
        TillNumeric = hasPrefix ? null : tillNumeric;
        Prefix = hasPrefix ? normalizedPrefix : null;
    }
}
