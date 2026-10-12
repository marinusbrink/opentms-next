using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.MasterData.Relations;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace OpenTms.MasterData.Drivers;

/// <summary>
/// A driver as its own aggregate (assessment §3.1): person data, the employer relation, the user account
/// of the driver app, and qualifications. Device binding is the Integrations run (§3.15). Personal data:
/// anonymised on an art. 17 request, keeping id and code for trip history (design 43).
/// </summary>
public class Driver : MasterDataAggregateRoot
{
    public string Code { get; private set; } = default!;

    public string FirstName { get; private set; } = default!;

    public string? MiddleName { get; private set; }

    public string LastName { get; private set; } = default!;

    public string? Initials { get; private set; }

    public string SearchKey { get; private set; } = string.Empty;

    public string? Phone { get; private set; }

    public string? Mobile { get; private set; }

    public string? Email { get; private set; }

    public string? PreferredCulture { get; private set; }

    public Guid? EmployerRelationId { get; private set; }

    /// <summary>The driver app login (ABP identity user, Platform); id without constraint.</summary>
    public Guid? UserId { get; private set; }

    public bool IsArchived { get; private set; }

    public bool IsAnonymised { get; private set; }

    public virtual ICollection<DriverQualification> Qualifications { get; private set; } = new List<DriverQualification>();

    protected Driver()
    {
    }

    public Driver(Guid id, Guid? tenantId, string code, string firstName, string lastName)
        : base(id, tenantId)
    {
        SetCode(code);
        SetPerson(firstName, null, lastName, null);
    }

    public string FullName => string.Join(" ", new[] { FirstName, MiddleName, LastName }.Where(p => !string.IsNullOrEmpty(p)));

    public void SetCode(string code)
    {
        EnsureWritable();
        Code = MasterDataCodes.NormalizeRequired(code, MasterDataConsts.MaxCodeLength, "code");
        SearchKey = MasterDataValues.SearchKey(FirstName, MiddleName, LastName, Code);
    }

    public void SetPerson(string firstName, string? middleName, string lastName, string? initials)
    {
        EnsureWritable();
        FirstName = Check.NotNullOrWhiteSpace(firstName, nameof(firstName), MasterDataConsts.MaxPersonNameLength).Trim();
        var middle = middleName?.Trim();
        MiddleName = string.IsNullOrEmpty(middle) ? null : Check.Length(middle, nameof(middleName), MasterDataConsts.MaxMiddleNameLength);
        LastName = Check.NotNullOrWhiteSpace(lastName, nameof(lastName), MasterDataConsts.MaxPersonNameLength).Trim();
        var init = initials?.Trim();
        Initials = string.IsNullOrEmpty(init) ? null : Check.Length(init, nameof(initials), MasterDataConsts.MaxInitialsLength);
        SearchKey = MasterDataValues.SearchKey(FirstName, MiddleName, LastName, Code);
    }

    public void SetContact(string? phone, string? mobile, string? email)
    {
        EnsureWritable();
        Phone = MasterDataPeople.Phone(phone, nameof(phone));
        Mobile = MasterDataPeople.Phone(mobile, nameof(mobile));
        Email = MasterDataPeople.Email(email);
    }

    public void SetPreferredCulture(string? culture, IReadOnlyCollection<string> configuredCultures)
    {
        EnsureWritable();
        PreferredCulture = MasterDataPeople.Culture(culture, configuredCultures);
    }

    /// <summary>The service checks that the relation carries the Carrier or OwnCompany role.</summary>
    public void SetEmployer(Guid? relationId)
    {
        EnsureWritable();
        EmployerRelationId = relationId;
    }

    public void SetUser(Guid? userId)
    {
        EnsureWritable();
        UserId = userId;
    }

    public void Archive() => IsArchived = true;

    /// <summary>An anonymised driver stays archived: it never returns to the working lists.</summary>
    public void Unarchive()
    {
        EnsureWritable();
        IsArchived = false;
    }

    public void SetQualifications(IEnumerable<DriverQualificationInput> inputs, Func<Guid> newId)
    {
        EnsureWritable();
        var wanted = inputs.ToList();
        if (wanted.Count > MasterDataConsts.MaxQualificationsPerDriver)
        {
            throw new BusinessException(MasterDataErrorCodes.TooManyChildren).WithData("max", MasterDataConsts.MaxQualificationsPerDriver).WithData("kind", "qualifications");
        }

        var keep = new HashSet<Guid>();
        foreach (var input in wanted)
        {
            var existing = input.Id != null ? Qualifications.FirstOrDefault(q => q.Id == input.Id.Value) : null;
            if (existing == null)
            {
                existing = new DriverQualification(newId(), TenantId, Id);
                Qualifications.Add(existing);
            }

            existing.Set(input);
            keep.Add(existing.Id);
        }

        foreach (var stale in Qualifications.Where(q => !keep.Contains(q.Id)).ToList())
        {
            Qualifications.Remove(stale);
        }
    }

    /// <summary>GDPR art. 17: the person fields are replaced, contact details, user link and qualifications cleared; id and code stay.</summary>
    public void Anonymise()
    {
        FirstName = MasterDataConsts.AnonymisedText;
        MiddleName = null;
        LastName = MasterDataConsts.AnonymisedText;
        Initials = null;
        Phone = null;
        Mobile = null;
        Email = null;
        PreferredCulture = null;
        UserId = null;
        SetDescription(null);
        Qualifications.Clear();
        SearchKey = MasterDataValues.SearchKey(Code);
        IsArchived = true;
        IsAnonymised = true;
    }

    private void EnsureWritable()
    {
        if (IsAnonymised)
        {
            throw new BusinessException(MasterDataErrorCodes.AnonymisedRecordIsReadOnly);
        }
    }
}

public sealed record DriverQualificationInput(Guid? Id, DriverQualificationKind Kind, string? Number, DateOnly? ValidFrom, DateOnly? ValidUntil, string? Description);

public class DriverQualification : Entity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public Guid DriverId { get; private set; }

    public DriverQualificationKind Kind { get; private set; }

    public string? Number { get; private set; }

    public DateOnly? ValidFrom { get; private set; }

    public DateOnly? ValidUntil { get; private set; }

    public string? Description { get; private set; }

    protected DriverQualification()
    {
    }

    public DriverQualification(Guid id, Guid? tenantId, Guid driverId)
        : base(id)
    {
        TenantId = tenantId;
        DriverId = driverId;
    }

    public void Set(DriverQualificationInput input)
    {
        if (!Enum.IsDefined(input.Kind))
        {
            throw new BusinessException(MasterDataErrorCodes.InvalidCode).WithData("field", "kind").WithData("value", (int)input.Kind);
        }

        if (input.ValidFrom != null && input.ValidUntil != null && input.ValidFrom > input.ValidUntil)
        {
            throw new BusinessException(MasterDataErrorCodes.InvalidCode).WithData("field", "validUntil").WithData("value", input.ValidUntil);
        }

        Kind = input.Kind;
        var number = input.Number?.Trim();
        Number = string.IsNullOrEmpty(number) ? null : Check.Length(number, nameof(input.Number), MasterDataConsts.MaxQualificationNumberLength);
        ValidFrom = input.ValidFrom;
        ValidUntil = input.ValidUntil;
        var text = input.Description?.Trim();
        Description = string.IsNullOrEmpty(text) ? null : Check.Length(text, nameof(input.Description), MasterDataConsts.MaxQualificationDescriptionLength);
    }
}
