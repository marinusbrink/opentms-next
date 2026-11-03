using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using OpenTms.MasterData.Reference;
using OpenTms.MasterData.Relations;
using OpenTms.MasterData.Transport;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

namespace OpenTms.MasterData.Published;

/// <summary>
/// See <see cref="IMasterDataIntakeWriter"/> (design 72 build note 27). Builds the rows through the same aggregate methods
/// the relation, transport unit, product and temperature services use, with their uniqueness checks repeated here (the
/// relation code, VAT and chamber of commerce number, the external identifier, the name of a transport unit, product or
/// temperature); the unique indexes behind those checks also refuse the second of two parallel intakes that create the
/// same row (the caller's job retries and then finds the first one). Every write saves at once, so the next intake in the
/// same unit of work finds the row. Logs ids only (constitution rule 5).
/// </summary>
// Published in-process writer: Orders' intake runs in Integrations' Hangfire job without a user, so it is anonymous; the HTTP-facing caller authorizes.
[RemoteService(IsEnabled = false)]
[AllowAnonymous]
public class MasterDataIntakeWriter : MasterDataAppServiceBase, IMasterDataIntakeWriter
{
    /// <summary>The longest relation code the writer derives from a name (the code may hold 32; room for "-n").</summary>
    public const int MaxDerivedCodeLength = 24;

    /// <summary>The country of an address without a country code when the tenant marked no default country.</summary>
    public const string FallbackCountryCode = "NL";

    private readonly IRepository<Relation, Guid> _relations;
    private readonly IRepository<RelationExternalIdentifier, Guid> _externalIds;
    private readonly IRepository<Country, Guid> _countries;

    public MasterDataIntakeWriter(
        IRepository<Relation, Guid> relations,
        IRepository<RelationExternalIdentifier, Guid> externalIds,
        IRepository<Country, Guid> countries)
    {
        _relations = relations;
        _externalIds = externalIds;
        _countries = countries;
    }

    // ── relations ──────────────────────────────────────────────────────────────

    public virtual async Task<IntakeWriteResultDto> CreateRelationAsync(IntakeRelationCreateDto input)
    {
        Check.NotNull(input, nameof(input));
        var reference = Cut(input.SourceReference, MasterDataConsts.MaxExternalIdentifierLength);
        var name = Cut(input.Name, MasterDataConsts.MaxNameLength);
        if (reference == null || name == null || input.Roles == RelationRoles.None)
        {
            return Refused(IMasterDataIntakeWriter.RefusedInvalid);
        }

        /* Idempotent on the caller's reference: the relation this intake field created before is the answer. */
        var source = IMasterDataIntakeWriter.SourceName;
        var earlier = (await AsyncExecuter.ToListAsync((await _externalIds.GetQueryableAsync())
            .Where(e => e.Source == source && e.Identifier == reference).Select(e => e.RelationId).Take(1))).FirstOrDefault();
        if (earlier != Guid.Empty && await _relations.FindAsync(earlier, includeDetails: true) is { } known)
        {
            return Written(known, known.Addresses.FirstOrDefault(a => a.AddressTypeId == null), created: false);
        }

        RelationAddressInput? address = null;
        Country? country = null;
        if (input.Address != null)
        {
            (address, country, var refused) = await AddressInputAsync(input.Address);
            if (refused != null)
            {
                return Refused(refused);
            }
        }

        var relation = new Relation(GuidGenerator.Create(), CurrentTenant.Id, await FreeCodeAsync(name), name, input.Roles);

        /* Optional keys the create rules would refuse are left out, never fatal: a document's typo is no reason for review. */
        var vat = MasterDataValues.NormalizeLegalNumber(input.VatNumber);
        if (vat != null && (vat.Length > MasterDataConsts.MaxLegalNumberLength || !MasterDataValues.IsVatNumberShape(vat) || await _relations.AnyAsync(r => r.VatNumber == vat)))
        {
            vat = null;
        }

        var coc = MasterDataValues.NormalizeLegalNumber(input.ChamberOfCommerceNumber);
        if (coc != null && (coc.Length > MasterDataConsts.MaxLegalNumberLength || await _relations.AnyAsync(r => r.ChamberOfCommerceNumber == coc)))
        {
            coc = null;
        }

        var email = input.EmailAddress?.Trim();
        if (string.IsNullOrEmpty(email) || email.Length > MasterDataConsts.MaxEmailLength || !MasterDataValues.IsEmailShape(email))
        {
            email = null;
        }

        relation.SetLegal(coc, vat);
        relation.SetContact(null, null, email, null);
        relation.SetDescription(L["MasterData:CreatedByOrderIntake"].Value);
        relation.SetExternalIdentifiers([new RelationExternalIdentifierInput(null, source, reference)], () => GuidGenerator.Create());
        if (address != null)
        {
            relation.SetAddresses([address], () => GuidGenerator.Create());
            relation.Addresses.Single().DefaultTimeZone(country!.PrimaryTimeZoneId);
        }

        await _relations.InsertAsync(relation, autoSave: true);
        Logger.LogInformation("Order intake created relation {RelationId}.", relation.Id);
        return Written(relation, relation.Addresses.FirstOrDefault(), created: true);
    }

    public virtual async Task<IntakeWriteResultDto> AddRelationAddressAsync(IntakeRelationAddressAddDto input)
    {
        Check.NotNull(input, nameof(input));
        Check.NotNull(input.Address, nameof(input.Address));
        var relation = await _relations.FindAsync(input.RelationId, includeDetails: true);
        if (relation == null)
        {
            return Refused(IMasterDataIntakeWriter.RefusedNotFound);
        }

        if (relation.IsArchived)
        {
            return Refused(IMasterDataIntakeWriter.RefusedArchived);
        }

        /* A relation holds one address per address type and one default (type none, design 43); the intake only adds the default. */
        if (relation.Addresses.Any(a => a.AddressTypeId == null))
        {
            return Refused(IMasterDataIntakeWriter.RefusedDefaultAddressTaken);
        }

        var (address, country, refused) = await AddressInputAsync(input.Address);
        if (refused != null)
        {
            return Refused(refused);
        }

        if (relation.Addresses.Count >= MasterDataConsts.MaxAddressesPerRelation)
        {
            return Refused(IMasterDataIntakeWriter.RefusedInvalid);
        }

        var inputs = relation.Addresses
            .Select(a => new RelationAddressInput(a.Id, a.AddressTypeId, a.CountryId, a.Street, a.HouseNumber, a.HouseNumberSuffix, a.AddressLine2, a.PostalCode, a.City, a.Latitude, a.Longitude, a.TimeZoneId))
            .Append(address!)
            .ToList();
        relation.SetAddresses(inputs, () => GuidGenerator.Create());
        var added = relation.Addresses.Single(a => a.AddressTypeId == null);
        added.DefaultTimeZone(country!.PrimaryTimeZoneId);
        await _relations.UpdateAsync(relation, autoSave: true);
        Logger.LogInformation("Order intake added address {RelationAddressId} to relation {RelationId}.", added.Id, relation.Id);
        return Written(relation, added, created: true);
    }

    /// <summary>The address as the relation's default address input, or why it cannot be one (a required part missing or too long, an unknown country).</summary>
    private async Task<(RelationAddressInput? Input, Country? Country, string? Refused)> AddressInputAsync(IntakeAddressInputDto address)
    {
        var street = address.Street?.Trim();
        var houseNumber = address.HouseNumber?.Trim();
        var postalCode = address.PostalCode?.Trim();
        var city = address.City?.Trim();
        if (string.IsNullOrEmpty(street) || street.Length > MasterDataConsts.MaxStreetLength
            || string.IsNullOrEmpty(postalCode) || postalCode.Length > MasterDataConsts.MaxPostalCodeLength
            || string.IsNullOrEmpty(city) || city.Length > MasterDataConsts.MaxCityLength
            || houseNumber is { Length: > MasterDataConsts.MaxHouseNumberLength })
        {
            return (null, null, IMasterDataIntakeWriter.RefusedInvalid);
        }

        var countries = await _countries.GetQueryableAsync();
        var code = MasterDataCodes.Normalize(address.CountryCode);
        Country? country;
        if (code != null)
        {
            country = (await AsyncExecuter.ToListAsync(countries.Where(c => c.IsoAlpha2 == code).Take(1))).FirstOrDefault();
        }
        else
        {
            /* No country on the document: the tenant's default country, else the Netherlands (the product's market). */
            country = (await AsyncExecuter.ToListAsync(countries.Where(c => c.IsDefault).Take(1))).FirstOrDefault()
                      ?? (await AsyncExecuter.ToListAsync(countries.Where(c => c.IsoAlpha2 == FallbackCountryCode).Take(1))).FirstOrDefault();
        }

        if (country == null)
        {
            return (null, null, IMasterDataIntakeWriter.RefusedCountryUnknown);
        }

        return (new RelationAddressInput(null, null, country.Id, street, string.IsNullOrEmpty(houseNumber) ? null : houseNumber, null, null, postalCode, city, null, null, null), country, null);
    }

    /// <summary>
    /// The relation code from the name: its letters and digits (diacritics folded), upper case, at most
    /// <see cref="MaxDerivedCodeLength"/>; when taken (any relation of the tenant, archived ones included, as the unique
    /// index counts them) the first free "-2", "-3", …. Deterministic for one state of the table.
    /// </summary>
    private async Task<string> FreeCodeAsync(string name)
    {
        var letters = new string(MasterDataValues.SearchKey(name).Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        var basis = letters.Length == 0 ? "REL" : letters[..Math.Min(MaxDerivedCodeLength, letters.Length)];
        var taken = (await AsyncExecuter.ToListAsync((await _relations.GetQueryableAsync()).Where(r => r.Code.StartsWith(basis)).Select(r => r.Code).Take(1000)))
            .ToHashSet(StringComparer.Ordinal);
        if (!taken.Contains(basis))
        {
            return basis;
        }

        var n = 2;
        while (taken.Contains(basis + "-" + n))
        {
            n++;
        }

        return basis + "-" + n;
    }

    private static IntakeWriteResultDto Written(Relation relation, RelationAddress? address, bool created) => new()
    {
        Created = created,
        Id = relation.Id,
        RelationAddressId = address?.Id,
        Name = relation.Name,
        Detail = address == null ? null : Describe(address)
    };

    private static string Describe(RelationAddress a)
    {
        var houseNumber = string.IsNullOrEmpty(a.HouseNumber) ? a.HouseNumberSuffix : a.HouseNumber + a.HouseNumberSuffix;
        var street = string.IsNullOrEmpty(houseNumber) ? a.Street : a.Street + " " + houseNumber;
        return street + ", " + a.PostalCode + " " + a.City;
    }

    // ── transport units, products, temperatures ────────────────────────────────

    public virtual async Task<IntakeWriteResultDto> CreateReferenceAsync(IntakeReferenceCreateDto input)
    {
        Check.NotNull(input, nameof(input));
        var name = Cut(input.Name, MasterDataConsts.MaxNameLength);
        if (name == null)
        {
            return Refused(IMasterDataIntakeWriter.RefusedInvalid);
        }

        var lowered = name.ToLowerInvariant();
        return input.Kind switch
        {
            MasterDataReferenceKind.TransportUnit => await CreateNamedAsync(u => u.Name.ToLower() == lowered, id => new TransportUnit(id, CurrentTenant.Id, name), u => u.Name, u => u.IsArchived),
            MasterDataReferenceKind.Product => await CreateNamedAsync(p => p.Name.ToLower() == lowered, id => new Product(id, CurrentTenant.Id, name), p => p.Name, p => p.IsArchived),
            MasterDataReferenceKind.Temperature => await CreateNamedAsync(t => t.Name.ToLower() == lowered, id => new Temperature(id, CurrentTenant.Id, name), t => t.Name, t => t.IsArchived),
            _ => Refused(IMasterDataIntakeWriter.RefusedInvalid)
        };
    }

    /// <summary>
    /// The row with this name (ignoring case: stricter than the services' exact check, so "europallet" never sits beside
    /// "Europallet"), else a new one with nothing but its name and the marker; the unique (TenantId, Name) index backs it.
    /// </summary>
    private async Task<IntakeWriteResultDto> CreateNamedAsync<T>(Expression<Func<T, bool>> sameName, Func<Guid, T> create, Func<T, string> nameOf, Func<T, bool> archivedOf)
        where T : MasterDataAggregateRoot
    {
        var repository = LazyServiceProvider.LazyGetRequiredService<IRepository<T, Guid>>();
        var existing = (await AsyncExecuter.ToListAsync((await repository.GetQueryableAsync()).Where(sameName).OrderBy(e => e.Id).Take(2))).ToList();
        if (existing.Count > 0)
        {
            var active = existing.FirstOrDefault(e => !archivedOf(e));
            return active == null
                ? Refused(IMasterDataIntakeWriter.RefusedArchived)
                : new IntakeWriteResultDto { Created = false, Id = active.Id, Name = nameOf(active) };
        }

        var row = create(GuidGenerator.Create());
        row.SetDescription(L["MasterData:CreatedByOrderIntake"].Value);
        await repository.InsertAsync(row, autoSave: true);
        Logger.LogInformation("Order intake created {Kind} {Id}.", typeof(T).Name, row.Id);
        return new IntakeWriteResultDto { Created = true, Id = row.Id, Name = nameOf(row) };
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private static IntakeWriteResultDto Refused(string code) => new() { RefusedCode = code };

    private static string? Cut(string? value, int maxLength)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        return text.Length > maxLength ? text[..maxLength].TrimEnd() : text;
    }
}
