using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Integrations.Routing;

/// <summary>
/// One cached answer of the routing provider (design 60, "Route cache"): integrations."RouteCache", unique per
/// (tenant, key). The key holds the rounded coordinates, the vehicle class, the provider and the option flags, so a
/// different question is a different row. Rows carry rounded coordinates only, never a relation or order id, and expire
/// after <c>Integrations:Routing:CacheDays</c>; the purge worker removes expired rows once a day. Instants are UTC
/// <see cref="DateTime"/>s as Platform's own technical tables keep them (comparable on every provider under test).
/// </summary>
public class RouteCacheEntry : Entity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public string Key { get; private set; } = default!;

    public string Provider { get; private set; } = default!;

    public int DistanceM { get; private set; }

    public int DurationS { get; private set; }

    public int? TollDistanceM { get; private set; }

    public decimal? TollAmount { get; private set; }

    public string? TollCurrencyCode { get; private set; }

    public decimal? Co2Kg { get; private set; }

    /// <summary>The encoded polyline, stored only when the caller asked for geometry.</summary>
    public string? Polyline { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime ExpiresAt { get; private set; }

    protected RouteCacheEntry()
    {
    }

    public RouteCacheEntry(
        Guid id,
        Guid? tenantId,
        string key,
        string provider,
        int distanceM,
        int durationS,
        int? tollDistanceM,
        decimal? tollAmount,
        string? tollCurrencyCode,
        decimal? co2Kg,
        string? polyline,
        DateTime createdAt,
        DateTime expiresAt)
        : base(id)
    {
        TenantId = tenantId;
        Key = Check.NotNullOrWhiteSpace(key, nameof(key), RoutingConsts.MaxKeyLength);
        Provider = Check.NotNullOrWhiteSpace(provider, nameof(provider), RoutingConsts.MaxProviderLength);
        DistanceM = distanceM;
        DurationS = durationS;
        TollDistanceM = tollDistanceM;
        TollAmount = tollAmount;
        TollCurrencyCode = Check.Length(tollCurrencyCode, nameof(tollCurrencyCode), RoutingConsts.MaxCurrencyCodeLength);
        Co2Kg = co2Kg;
        Polyline = polyline;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public bool IsExpired(DateTime now) => ExpiresAt <= now;

    /// <summary>A fresh answer for the same key (the previous one expired): the row is reused, the key stays unique.</summary>
    public void Refresh(
        string provider,
        int distanceM,
        int durationS,
        int? tollDistanceM,
        decimal? tollAmount,
        string? tollCurrencyCode,
        decimal? co2Kg,
        string? polyline,
        DateTime createdAt,
        DateTime expiresAt)
    {
        Provider = Check.NotNullOrWhiteSpace(provider, nameof(provider), RoutingConsts.MaxProviderLength);
        DistanceM = distanceM;
        DurationS = durationS;
        TollDistanceM = tollDistanceM;
        TollAmount = tollAmount;
        TollCurrencyCode = Check.Length(tollCurrencyCode, nameof(tollCurrencyCode), RoutingConsts.MaxCurrencyCodeLength);
        Co2Kg = co2Kg;
        Polyline = polyline;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }
}
