using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace OpenTms.MasterData.Relations;

/// <summary>
/// The one Relation of assessment §3.1: customer, carrier, creditor, depot or the own company, narrow by
/// design. Settings per domain live in that domain keyed by RelationId (design 43).
/// </summary>
public class Relation : MasterDataAggregateRoot
{
    public string Code { get; private set; } = default!;

    public string Name { get; private set; } = default!;

    /// <summary>Lower-case, diacritics folded: name and code. Indexed (B-tree and trigram), never shown.</summary>
    public string SearchKey { get; private set; } = string.Empty;

    public RelationRoles Roles { get; private set; }

    public bool IsArchived { get; private set; }

    /// <summary>Shown as a flag (PO 2026-10-12, the old "Is geblokkeerd"); Orders and Financial decide later what a block stops.</summary>
    public bool IsBlocked { get; private set; }

    /// <summary>None unless blocked.</summary>
    public RelationBlockReason BlockReason { get; private set; }

    public string? Phone { get; private set; }

    public string? Mobile { get; private set; }

    public string? Email { get; private set; }

    public string? Website { get; private set; }

    public string? ChamberOfCommerceNumber { get; private set; }

    public string? VatNumber { get; private set; }

    public string? PreferredCulture { get; private set; }

    public virtual ICollection<RelationAddress> Addresses { get; private set; } = new List<RelationAddress>();

    public virtual ICollection<RelationContactPerson> ContactPersons { get; private set; } = new List<RelationContactPerson>();

    public virtual ICollection<RelationBankAccount> BankAccounts { get; private set; } = new List<RelationBankAccount>();

    public virtual ICollection<RelationCategoryMembership> Categories { get; private set; } = new List<RelationCategoryMembership>();

    public virtual ICollection<RelationGeographicalRegion> GeographicalRegions { get; private set; } = new List<RelationGeographicalRegion>();

    public virtual ICollection<RelationOrganizationUnit> OrganizationUnits { get; private set; } = new List<RelationOrganizationUnit>();

    protected Relation()
    {
    }

    public Relation(Guid id, Guid? tenantId, string code, string name, RelationRoles roles)
        : base(id, tenantId)
    {
        SetCode(code);
        SetName(name);
        SetRoles(roles);
    }

    public bool HasRole(RelationRoles role) => (Roles & role) == role;

    public void SetCode(string code)
    {
        Code = MasterDataCodes.NormalizeRequired(code, MasterDataConsts.MaxCodeLength, "code");
        SearchKey = MasterDataValues.SearchKey(Name, Code);
    }

    public void SetName(string name)
    {
        Name = NormalizeName(name);
        SearchKey = MasterDataValues.SearchKey(Name, Code);
    }

    /// <summary>At least one role; OwnCompany uniqueness per tenant is the partial index plus the service check.</summary>
    public void SetRoles(RelationRoles roles)
    {
        if (roles == RelationRoles.None || !Enum.IsDefined(roles & ~(RelationRoles.Customer | RelationRoles.Carrier | RelationRoles.Creditor | RelationRoles.Depot | RelationRoles.OwnCompany)) || (int)roles < 0 || (int)roles > 31)
        {
            throw new BusinessException(MasterDataErrorCodes.RolesRequired);
        }

        Roles = roles;
    }

    public void Archive() => IsArchived = true;

    public void Unarchive() => IsArchived = false;

    public void SetBlocked(bool blocked, RelationBlockReason reason)
    {
        if (!Enum.IsDefined(reason))
        {
            throw new BusinessException(MasterDataErrorCodes.InvalidCode).WithData("field", "blockReason").WithData("value", (int)reason);
        }

        if (blocked && reason == RelationBlockReason.None)
        {
            throw new BusinessException(MasterDataErrorCodes.BlockReasonRequired);
        }

        IsBlocked = blocked;
        BlockReason = blocked ? reason : RelationBlockReason.None;
    }

    public void SetContact(string? phone, string? mobile, string? email, string? website)
    {
        Phone = Text(phone, MasterDataConsts.MaxPhoneLength, nameof(phone));
        Mobile = Text(mobile, MasterDataConsts.MaxPhoneLength, nameof(mobile));
        Email = MasterDataPeople.Email(email);
        Website = Text(website, MasterDataConsts.MaxWebsiteLength, nameof(website));
    }

    public void SetLegal(string? chamberOfCommerceNumber, string? vatNumber)
    {
        ChamberOfCommerceNumber = MasterDataValues.NormalizeLegalNumber(chamberOfCommerceNumber);
        if (ChamberOfCommerceNumber != null && ChamberOfCommerceNumber.Length > MasterDataConsts.MaxLegalNumberLength)
        {
            throw new BusinessException(MasterDataErrorCodes.InvalidCode).WithData("field", "chamberOfCommerceNumber").WithData("value", chamberOfCommerceNumber ?? string.Empty);
        }

        var vat = MasterDataValues.NormalizeLegalNumber(vatNumber);
        if (vat != null && (vat.Length > MasterDataConsts.MaxLegalNumberLength || !MasterDataValues.IsVatNumberShape(vat)))
        {
            throw new BusinessException(MasterDataErrorCodes.InvalidVatNumber).WithData("value", vatNumber ?? string.Empty);
        }

        VatNumber = vat;
    }

    public void SetPreferredCulture(string? culture, IReadOnlyCollection<string> configuredCultures)
        => PreferredCulture = MasterDataPeople.Culture(culture, configuredCultures);

    /// <summary>Replaces the addresses: rows with a known id are updated, new rows added, the rest removed.</summary>
    public void SetAddresses(IEnumerable<RelationAddressInput> inputs, Func<Guid> newId)
    {
        var wanted = inputs.ToList();
        if (wanted.Count > MasterDataConsts.MaxAddressesPerRelation)
        {
            throw new BusinessException(MasterDataErrorCodes.TooManyChildren).WithData("max", MasterDataConsts.MaxAddressesPerRelation).WithData("kind", "addresses");
        }

        if (wanted.Select(a => a.AddressTypeId).Distinct().Count() != wanted.Count)
        {
            throw new BusinessException(MasterDataErrorCodes.DuplicateKey).WithData("field", "addressTypeId").WithData("value", string.Empty);
        }

        var keep = new HashSet<Guid>();
        foreach (var input in wanted)
        {
            var existing = input.Id != null ? Addresses.FirstOrDefault(a => a.Id == input.Id.Value) : Addresses.FirstOrDefault(a => a.AddressTypeId == input.AddressTypeId);
            if (existing == null)
            {
                /* Validate before adding: a refused input leaves the aggregate untouched. */
                existing = new RelationAddress(newId(), TenantId, Id);
                existing.Set(input);
                Addresses.Add(existing);
            }
            else
            {
                existing.Set(input);
            }

            keep.Add(existing.Id);
        }

        foreach (var stale in Addresses.Where(a => !keep.Contains(a.Id)).ToList())
        {
            Addresses.Remove(stale);
        }
    }

    public void SetContactPersons(IEnumerable<RelationContactPersonInput> inputs, Func<Guid> newId)
    {
        var wanted = inputs.ToList();
        if (wanted.Count > MasterDataConsts.MaxContactPersonsPerRelation)
        {
            throw new BusinessException(MasterDataErrorCodes.TooManyChildren).WithData("max", MasterDataConsts.MaxContactPersonsPerRelation).WithData("kind", "contactPersons");
        }

        var keep = new HashSet<Guid>();
        foreach (var input in wanted)
        {
            var existing = input.Id != null ? ContactPersons.FirstOrDefault(c => c.Id == input.Id.Value) : null;
            if (existing == null)
            {
                /* Validate before adding: a refused input leaves the aggregate untouched. */
                existing = new RelationContactPerson(newId(), TenantId, Id);
                existing.Set(input);
                ContactPersons.Add(existing);
            }
            else
            {
                existing.Set(input);
            }

            keep.Add(existing.Id);
        }

        foreach (var stale in ContactPersons.Where(c => !keep.Contains(c.Id)).ToList())
        {
            ContactPersons.Remove(stale);
        }
    }

    public void SetBankAccounts(IEnumerable<RelationBankAccountInput> inputs, Func<Guid> newId)
    {
        var wanted = inputs.ToList();
        if (wanted.Count > MasterDataConsts.MaxBankAccountsPerRelation)
        {
            throw new BusinessException(MasterDataErrorCodes.TooManyChildren).WithData("max", MasterDataConsts.MaxBankAccountsPerRelation).WithData("kind", "bankAccounts");
        }

        var ibans = wanted.Select(b => MasterDataValues.NormalizeIban(b.Iban)).ToList();
        if (ibans.Distinct().Count() != ibans.Count)
        {
            throw new BusinessException(MasterDataErrorCodes.DuplicateKey).WithData("field", "iban").WithData("value", string.Empty);
        }

        if (wanted.Count(b => b.IsDefault) > 1)
        {
            throw new BusinessException(MasterDataErrorCodes.DuplicateKey).WithData("field", "isDefault").WithData("value", string.Empty);
        }

        var keep = new HashSet<Guid>();
        foreach (var input in wanted)
        {
            var normalized = MasterDataValues.NormalizeIban(input.Iban);
            var existing = input.Id != null ? BankAccounts.FirstOrDefault(b => b.Id == input.Id.Value) : BankAccounts.FirstOrDefault(b => b.Iban == normalized);
            if (existing == null)
            {
                /* Validate before adding: a refused input leaves the aggregate untouched. */
                existing = new RelationBankAccount(newId(), TenantId, Id);
                existing.Set(input);
                BankAccounts.Add(existing);
            }
            else
            {
                existing.Set(input);
            }

            keep.Add(existing.Id);
        }

        foreach (var stale in BankAccounts.Where(b => !keep.Contains(b.Id)).ToList())
        {
            BankAccounts.Remove(stale);
        }

        /* Exactly one default when there is any account. */
        if (BankAccounts.Count > 0 && BankAccounts.All(b => !b.IsDefault))
        {
            BankAccounts.OrderBy(b => b.Iban).First().MarkDefault(true);
        }
    }

    public void SetCategories(IEnumerable<Guid> ids) => SyncJoin(Categories, ids, c => c.RelationCategoryId, id => new RelationCategoryMembership(TenantId, Id, id));

    public void SetGeographicalRegions(IEnumerable<Guid> ids) => SyncJoin(GeographicalRegions, ids, r => r.GeographicalRegionId, id => new RelationGeographicalRegion(TenantId, Id, id));

    public void SetOrganizationUnits(IEnumerable<Guid> ids) => SyncJoin(OrganizationUnits, ids, o => o.OrganizationUnitId, id => new RelationOrganizationUnit(TenantId, Id, id));

    private static void SyncJoin<TJoin>(ICollection<TJoin> rows, IEnumerable<Guid> ids, Func<TJoin, Guid> key, Func<Guid, TJoin> create)
    {
        var wanted = ids.Distinct().ToHashSet();
        foreach (var stale in rows.Where(r => !wanted.Contains(key(r))).ToList())
        {
            rows.Remove(stale);
        }

        foreach (var id in wanted.Where(id => rows.All(r => key(r) != id)))
        {
            rows.Add(create(id));
        }
    }

    private static string? Text(string? value, int maxLength, string name)
    {
        var text = value?.Trim();
        return string.IsNullOrEmpty(text) ? null : Check.Length(text, name, maxLength);
    }
}

/// <summary>Shared person and contact field rules of the run (design 43).</summary>
public static class MasterDataPeople
{
    public static string? Email(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (text.Length > MasterDataConsts.MaxEmailLength || !MasterDataValues.IsEmailShape(text))
        {
            throw new BusinessException(MasterDataErrorCodes.InvalidEmail); /* no value: an e-mail address is personal data (rule 5) */
        }

        return text;
    }

    public static string? Culture(string? value, IReadOnlyCollection<string> configuredCultures)
    {
        var culture = MasterDataValues.NormalizeCulture(value);
        if (culture == null)
        {
            return null;
        }

        if (culture.Length > MasterDataConsts.MaxCultureLength || !configuredCultures.Contains(culture))
        {
            throw new BusinessException(MasterDataErrorCodes.InvalidCulture).WithData("value", culture);
        }

        return culture;
    }

    public static string? Phone(string? value, string name)
    {
        var text = value?.Trim();
        return string.IsNullOrEmpty(text) ? null : Check.Length(text, name, MasterDataConsts.MaxPhoneLength);
    }
}

/// <summary>An address without an address type is the relation's default: it serves every type that has no address of its own ("all addresses equal").</summary>
public sealed record RelationAddressInput(Guid? Id, Guid? AddressTypeId, Guid CountryId, string Street, string? HouseNumber, string? AddressLine2, string PostalCode, string City, double? Latitude, double? Longitude, string? TimeZoneId);

public sealed record RelationContactPersonInput(Guid? Id, string FirstName, string? MiddleName, string LastName, ContactPersonRole Role, string? JobTitle, string? Department, string? Email, string? Phone, string? Mobile);

public sealed record RelationBankAccountInput(Guid? Id, string Iban, string? Bic, string? BankName, string? AccountHolder, bool IsDefault);

/// <summary>One address per address type per relation (design 43); the type's kind says what it is.</summary>
public class RelationAddress : Entity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public Guid RelationId { get; private set; }

    /// <summary>Null: the default address for every type without one of its own (one per relation, like every type).</summary>
    public Guid? AddressTypeId { get; private set; }

    public Guid CountryId { get; private set; }

    public string Street { get; private set; } = default!;

    public string? HouseNumber { get; private set; }

    public string? AddressLine2 { get; private set; }

    public string PostalCode { get; private set; } = default!;

    /// <summary>Upper-case, no spaces: what the matcher compares.</summary>
    public string PostalCodeKey { get; private set; } = string.Empty;

    public string City { get; private set; } = default!;

    public double? Latitude { get; private set; }

    public double? Longitude { get; private set; }

    public string? TimeZoneId { get; private set; }

    protected RelationAddress()
    {
    }

    public RelationAddress(Guid id, Guid? tenantId, Guid relationId)
        : base(id)
    {
        TenantId = tenantId;
        RelationId = relationId;
    }

    public void Set(RelationAddressInput input)
    {
        AddressTypeId = input.AddressTypeId;
        CountryId = input.CountryId;
        Street = Check.NotNullOrWhiteSpace(input.Street, nameof(input.Street), MasterDataConsts.MaxStreetLength).Trim();
        HouseNumber = Optional(input.HouseNumber, MasterDataConsts.MaxHouseNumberLength, nameof(input.HouseNumber));
        AddressLine2 = Optional(input.AddressLine2, MasterDataConsts.MaxAddressLine2Length, nameof(input.AddressLine2));
        PostalCode = Check.NotNullOrWhiteSpace(input.PostalCode, nameof(input.PostalCode), MasterDataConsts.MaxPostalCodeLength).Trim();
        PostalCodeKey = MasterDataCodes.NormalizePostalCode(PostalCode);
        City = Check.NotNullOrWhiteSpace(input.City, nameof(input.City), MasterDataConsts.MaxCityLength).Trim();
        if (input.Latitude is < -90 or > 90)
        {
            throw new BusinessException(MasterDataErrorCodes.InvalidCode).WithData("field", "latitude").WithData("value", input.Latitude);
        }

        if (input.Longitude is < -180 or > 180)
        {
            throw new BusinessException(MasterDataErrorCodes.InvalidCode).WithData("field", "longitude").WithData("value", input.Longitude);
        }

        Latitude = input.Latitude;
        Longitude = input.Longitude;

        var zone = input.TimeZoneId?.Trim();
        if (!string.IsNullOrEmpty(zone) && (zone.Length > MasterDataConsts.MaxTimeZoneIdLength || !TimeZoneInfo.TryFindSystemTimeZoneById(zone, out _)))
        {
            throw new BusinessException(MasterDataErrorCodes.InvalidTimeZone).WithData("value", zone);
        }

        TimeZoneId = string.IsNullOrEmpty(zone) ? null : zone;
    }

    /// <summary>The service fills the country's primary zone when the address has none.</summary>
    public void DefaultTimeZone(string? countryZone)
    {
        if (TimeZoneId == null && !string.IsNullOrEmpty(countryZone))
        {
            TimeZoneId = countryZone;
        }
    }

    private static string? Optional(string? value, int maxLength, string name)
    {
        var text = value?.Trim();
        return string.IsNullOrEmpty(text) ? null : Check.Length(text, name, maxLength);
    }
}

public class RelationContactPerson : Entity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public Guid RelationId { get; private set; }

    public string FirstName { get; private set; } = default!;

    public string? MiddleName { get; private set; }

    public string LastName { get; private set; } = default!;

    public ContactPersonRole Role { get; private set; }

    public string? JobTitle { get; private set; }

    public string? Department { get; private set; }

    public string? Email { get; private set; }

    public string? Phone { get; private set; }

    public string? Mobile { get; private set; }

    protected RelationContactPerson()
    {
    }

    public RelationContactPerson(Guid id, Guid? tenantId, Guid relationId)
        : base(id)
    {
        TenantId = tenantId;
        RelationId = relationId;
    }

    public void Set(RelationContactPersonInput input)
    {
        FirstName = Check.NotNullOrWhiteSpace(input.FirstName, nameof(input.FirstName), MasterDataConsts.MaxPersonNameLength).Trim();
        var middle = input.MiddleName?.Trim();
        MiddleName = string.IsNullOrEmpty(middle) ? null : Check.Length(middle, nameof(input.MiddleName), MasterDataConsts.MaxMiddleNameLength);
        LastName = Check.NotNullOrWhiteSpace(input.LastName, nameof(input.LastName), MasterDataConsts.MaxPersonNameLength).Trim();
        if (!Enum.IsDefined(input.Role))
        {
            throw new BusinessException(MasterDataErrorCodes.InvalidCode).WithData("field", "role").WithData("value", (int)input.Role);
        }

        Role = input.Role;
        var job = input.JobTitle?.Trim();
        JobTitle = string.IsNullOrEmpty(job) ? null : Check.Length(job, nameof(input.JobTitle), MasterDataConsts.MaxJobTitleLength);
        var department = input.Department?.Trim();
        Department = string.IsNullOrEmpty(department) ? null : Check.Length(department, nameof(input.Department), MasterDataConsts.MaxJobTitleLength);
        Email = MasterDataPeople.Email(input.Email);
        Phone = MasterDataPeople.Phone(input.Phone, nameof(input.Phone));
        Mobile = MasterDataPeople.Phone(input.Mobile, nameof(input.Mobile));
    }
}

public class RelationBankAccount : Entity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public Guid RelationId { get; private set; }

    public string Iban { get; private set; } = default!;

    public string? Bic { get; private set; }

    public string? BankName { get; private set; }

    public string? AccountHolder { get; private set; }

    public bool IsDefault { get; private set; }

    protected RelationBankAccount()
    {
    }

    public RelationBankAccount(Guid id, Guid? tenantId, Guid relationId)
        : base(id)
    {
        TenantId = tenantId;
        RelationId = relationId;
    }

    public void Set(RelationBankAccountInput input)
    {
        var iban = MasterDataValues.NormalizeIban(input.Iban);
        if (iban == null || !MasterDataValues.IsValidIban(iban))
        {
            throw new BusinessException(MasterDataErrorCodes.InvalidIban); /* no value: an IBAN is personal data (rule 5) */
        }

        Iban = iban;
        var bic = MasterDataCodes.Normalize(input.Bic);
        Bic = bic == null ? null : Check.Length(bic, nameof(input.Bic), MasterDataConsts.MaxBicLength);
        var bank = input.BankName?.Trim();
        BankName = string.IsNullOrEmpty(bank) ? null : Check.Length(bank, nameof(input.BankName), MasterDataConsts.MaxBankNameLength);
        var holder = input.AccountHolder?.Trim();
        AccountHolder = string.IsNullOrEmpty(holder) ? null : Check.Length(holder, nameof(input.AccountHolder), MasterDataConsts.MaxAccountHolderLength);
        IsDefault = input.IsDefault;
    }

    public void MarkDefault(bool isDefault) => IsDefault = isDefault;
}

public class RelationCategoryMembership : Entity, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public Guid RelationId { get; private set; }

    public Guid RelationCategoryId { get; private set; }

    protected RelationCategoryMembership()
    {
    }

    public RelationCategoryMembership(Guid? tenantId, Guid relationId, Guid relationCategoryId)
    {
        TenantId = tenantId;
        RelationId = relationId;
        RelationCategoryId = relationCategoryId;
    }

    public override object[] GetKeys() => [RelationId, RelationCategoryId];
}

public class RelationGeographicalRegion : Entity, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public Guid RelationId { get; private set; }

    public Guid GeographicalRegionId { get; private set; }

    protected RelationGeographicalRegion()
    {
    }

    public RelationGeographicalRegion(Guid? tenantId, Guid relationId, Guid geographicalRegionId)
    {
        TenantId = tenantId;
        RelationId = relationId;
        GeographicalRegionId = geographicalRegionId;
    }

    public override object[] GetKeys() => [RelationId, GeographicalRegionId];
}

/// <summary>Which organisation units may use the relation; empty = all. Platform ids, no constraint.</summary>
public class RelationOrganizationUnit : Entity, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public Guid RelationId { get; private set; }

    public Guid OrganizationUnitId { get; private set; }

    protected RelationOrganizationUnit()
    {
    }

    public RelationOrganizationUnit(Guid? tenantId, Guid relationId, Guid organizationUnitId)
    {
        TenantId = tenantId;
        RelationId = relationId;
        OrganizationUnitId = organizationUnitId;
    }

    public override object[] GetKeys() => [RelationId, OrganizationUnitId];
}
