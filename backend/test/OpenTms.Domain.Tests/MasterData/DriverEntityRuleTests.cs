using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.MasterData.Drivers;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace OpenTms.MasterData;

/// <summary>
/// Unit tests for the Driver aggregate (design 43, "Driver" and the security quickscan's art. 17
/// mechanism). Risk class: Critical for Anonymise (personal data must be gone and the record must
/// stay read-only and referable by id); High for the qualification rules and the search key.
/// </summary>
public class DriverEntityRuleTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly string[] Cultures = ["nl", "en"];
    private static readonly DateOnly Today = new(2026, 10, 11);

    private static Driver NewDriver(string code = "drv01", string first = "Jan", string last = "Jansen")
        => new(Guid.NewGuid(), Tenant, code, first, last);

    private static DriverQualificationInput Qualification(DriverQualificationKind kind, string? number = null, DateOnly? from = null, DateOnly? until = null, Guid? id = null, string? description = null)
        => new(id, kind, number, from, until, description);

    /// <summary>A driver with every personal field filled, as the anonymise tests start from.</summary>
    private static Driver FilledDriver()
    {
        var driver = NewDriver();
        driver.SetPerson("Jan", "van der", "Berg", "J.");
        driver.SetContact("020-1", "06-1", "jan@acme.nl");
        driver.SetPreferredCulture("nl", Cultures);
        driver.SetDescription("night shifts only");
        driver.SetEmployer(Guid.NewGuid());
        driver.SetUser(Guid.NewGuid());
        driver.SetQualifications([Qualification(DriverQualificationKind.DrivingLicenceCE, "123", Today.AddYears(-5), Today.AddYears(5)), Qualification(DriverQualificationKind.Code95)], Guid.NewGuid);
        return driver;
    }

    // ── Person, code, search key ─────────────────────────────────────────────

    [Fact]
    public void Construction_normalises_the_code_trims_the_names_and_builds_the_search_key()
    {
        var driver = NewDriver(" drv01 ", " Jan ", " Jansen ");

        driver.Code.ShouldBe("DRV01");
        driver.FirstName.ShouldBe("Jan");
        driver.LastName.ShouldBe("Jansen");
        driver.MiddleName.ShouldBeNull();
        driver.FullName.ShouldBe("Jan Jansen");
        driver.SearchKey.ShouldBe("jan jansen drv01");
        driver.TenantId.ShouldBe(Tenant);
        driver.IsAnonymised.ShouldBeFalse();
        driver.IsArchived.ShouldBeFalse();
    }

    [Fact]
    public void FullName_includes_the_middle_name_and_the_search_key_follows_every_name_change()
    {
        var driver = NewDriver();

        driver.SetPerson("Jan", " van der ", "Berg", " J.P. ");

        driver.FullName.ShouldBe("Jan van der Berg");
        driver.Initials.ShouldBe("J.P.");
        driver.SearchKey.ShouldBe("jan van der berg drv01");

        driver.SetCode("DRV02");
        driver.SearchKey.ShouldBe("jan van der berg drv02");
    }

    [Fact]
    public void Names_and_code_are_required_and_bounded()
    {
        var driver = NewDriver();

        Should.Throw<ArgumentException>(() => driver.SetPerson(" ", null, "Jansen", null));
        Should.Throw<ArgumentException>(() => driver.SetPerson("Jan", null, "", null));
        Should.Throw<ArgumentException>(() => driver.SetPerson("Jan", new string('m', MasterDataConsts.MaxMiddleNameLength + 1), "Jansen", null));
        Should.Throw<ArgumentException>(() => driver.SetPerson("Jan", null, "Jansen", new string('i', MasterDataConsts.MaxInitialsLength + 1)));
        Should.Throw<BusinessException>(() => driver.SetCode(" ")).Code.ShouldBe(MasterDataErrorCodes.InvalidCode);
        Should.Throw<BusinessException>(() => NewDriver(code: "")).Code.ShouldBe(MasterDataErrorCodes.InvalidCode);
    }

    [Fact]
    public void Contact_and_culture_follow_the_shared_person_rules()
    {
        var driver = NewDriver();

        driver.SetContact(" 020 ", "", " jan@acme.nl ");
        driver.Phone.ShouldBe("020");
        driver.Mobile.ShouldBeNull();
        driver.Email.ShouldBe("jan@acme.nl");
        Should.Throw<BusinessException>(() => driver.SetContact(null, null, "jan@acme")).Code.ShouldBe(MasterDataErrorCodes.InvalidEmail);

        driver.SetPreferredCulture("EN", Cultures);
        driver.PreferredCulture.ShouldBe("en");
        Should.Throw<BusinessException>(() => driver.SetPreferredCulture("fr", Cultures)).Code.ShouldBe(MasterDataErrorCodes.InvalidCulture);
    }

    // ── Qualifications ───────────────────────────────────────────────────────

    [Fact]
    public void SetQualifications_updates_by_id_adds_rows_without_id_and_removes_the_rest()
    {
        var driver = NewDriver();
        driver.SetQualifications([Qualification(DriverQualificationKind.DrivingLicenceC, "C-1", Today.AddYears(-1), Today.AddYears(4)), Qualification(DriverQualificationKind.AdrCertificate, "ADR-1")], Guid.NewGuid);
        var licence = driver.Qualifications.Single(q => q.Kind == DriverQualificationKind.DrivingLicenceC);
        var adr = driver.Qualifications.Single(q => q.Kind == DriverQualificationKind.AdrCertificate);
        licence.DriverId.ShouldBe(driver.Id);
        licence.TenantId.ShouldBe(Tenant);
        licence.ValidFrom.ShouldBe(Today.AddYears(-1));
        licence.ValidUntil.ShouldBe(Today.AddYears(4));

        driver.SetQualifications([Qualification(DriverQualificationKind.DrivingLicenceCE, "CE-1", id: licence.Id), Qualification(DriverQualificationKind.Code95, "95", until: Today.AddDays(30))], Guid.NewGuid);

        driver.Qualifications.Count.ShouldBe(2);
        driver.Qualifications.Single(q => q.Id == licence.Id).Kind.ShouldBe(DriverQualificationKind.DrivingLicenceCE);
        driver.Qualifications.ShouldNotContain(q => q.Id == adr.Id);
        driver.Qualifications.Single(q => q.Kind == DriverQualificationKind.Code95).ValidUntil.ShouldBe(Today.AddDays(30));
    }

    [Fact]
    public void A_qualification_validates_kind_date_order_and_lengths()
    {
        var qualification = new DriverQualification(Guid.NewGuid(), Tenant, Guid.NewGuid());

        qualification.Set(Qualification(DriverQualificationKind.Other, " X-1 ", Today, Today, description: " none "));
        qualification.Number.ShouldBe("X-1");
        qualification.Description.ShouldBe("none");
        qualification.Set(Qualification(DriverQualificationKind.TachographCard, "", null, null, description: ""));
        qualification.Number.ShouldBeNull();
        qualification.Description.ShouldBeNull();

        Should.Throw<BusinessException>(() => qualification.Set(Qualification((DriverQualificationKind)42))).Code.ShouldBe(MasterDataErrorCodes.InvalidCode);
        Should.Throw<BusinessException>(() => qualification.Set(Qualification(DriverQualificationKind.Code95, from: Today, until: Today.AddDays(-1)))).Code.ShouldBe(MasterDataErrorCodes.InvalidCode);
        Should.Throw<ArgumentException>(() => qualification.Set(Qualification(DriverQualificationKind.Code95, new string('n', MasterDataConsts.MaxQualificationNumberLength + 1))));
        Should.Throw<ArgumentException>(() => qualification.Set(Qualification(DriverQualificationKind.Code95, description: new string('d', MasterDataConsts.MaxQualificationDescriptionLength + 1))));
    }

    [Fact]
    public void More_than_twenty_qualifications_are_refused_with_TooManyChildren()
    {
        var driver = NewDriver();
        var inputs = Enumerable.Range(0, MasterDataConsts.MaxQualificationsPerDriver + 1).Select(i => Qualification(DriverQualificationKind.Other, $"Q{i}")).ToList();

        var ex = Should.Throw<BusinessException>(() => driver.SetQualifications(inputs, Guid.NewGuid));
        ex.Code.ShouldBe(MasterDataErrorCodes.TooManyChildren);
        ex.Data["max"].ShouldBe(MasterDataConsts.MaxQualificationsPerDriver);

        driver.SetQualifications(inputs.Take(MasterDataConsts.MaxQualificationsPerDriver), Guid.NewGuid);
        driver.Qualifications.Count.ShouldBe(MasterDataConsts.MaxQualificationsPerDriver);
    }

    // ── Anonymise (GDPR art. 17) ─────────────────────────────────────────────

    [Fact]
    public void Anonymise_replaces_every_personal_field_clears_links_and_qualifications_and_keeps_id_and_code()
    {
        var driver = FilledDriver();
        var id = driver.Id;
        var employer = driver.EmployerRelationId;

        driver.Anonymise();

        driver.Id.ShouldBe(id);
        driver.Code.ShouldBe("DRV01");
        driver.FirstName.ShouldBe(MasterDataConsts.AnonymisedText);
        driver.LastName.ShouldBe(MasterDataConsts.AnonymisedText);
        driver.MiddleName.ShouldBeNull();
        driver.Initials.ShouldBeNull();
        driver.FullName.ShouldBe("Anonymised Anonymised");
        driver.Phone.ShouldBeNull();
        driver.Mobile.ShouldBeNull();
        driver.Email.ShouldBeNull();
        driver.PreferredCulture.ShouldBeNull();
        driver.Description.ShouldBeNull();
        driver.UserId.ShouldBeNull();
        driver.Qualifications.ShouldBeEmpty();
        driver.SearchKey.ShouldBe("drv01");
        driver.IsAnonymised.ShouldBeTrue();
        driver.IsArchived.ShouldBeTrue();
        driver.EmployerRelationId.ShouldBe(employer, "the employer is company data and trip history needs it");
    }

    [Fact]
    public void Nothing_personal_survives_anonymise_in_any_string_property()
    {
        var driver = FilledDriver();
        var personal = new[] { "Jan", "van der", "Berg", "J.", "020-1", "06-1", "jan@acme.nl", "night shifts only", "123" };

        driver.Anonymise();

        var texts = typeof(Driver).GetProperties()
            .Where(p => p.PropertyType == typeof(string))
            .Select(p => (string?)p.GetValue(driver))
            .Where(v => v != null)
            .Select(v => v!)
            .ToList();
        foreach (var value in personal)
        {
            texts.ShouldAllBe(t => !t.Contains(value, StringComparison.OrdinalIgnoreCase), $"'{value}' must not survive in {string.Join(" | ", texts)}");
        }
    }

    [Fact]
    public void Anonymise_twice_is_one_effect()
    {
        var driver = FilledDriver();
        driver.Anonymise();
        var snapshot = Snapshot(driver);

        driver.Anonymise();

        Snapshot(driver).ShouldBe(snapshot);
    }

    [Fact]
    public void Every_mutator_throws_AnonymisedRecordIsReadOnly_after_anonymise()
    {
        var driver = FilledDriver();
        driver.Anonymise();
        var snapshot = Snapshot(driver);

        var mutators = new Dictionary<string, Action>
        {
            ["SetCode"] = () => driver.SetCode("DRV99"),
            ["SetPerson"] = () => driver.SetPerson("Piet", null, "Pietersen", null),
            ["SetContact"] = () => driver.SetContact("1", "2", "p@acme.nl"),
            ["SetPreferredCulture"] = () => driver.SetPreferredCulture("en", Cultures),
            ["SetEmployer"] = () => driver.SetEmployer(Guid.NewGuid()),
            ["SetUser"] = () => driver.SetUser(Guid.NewGuid()),
            ["SetQualifications"] = () => driver.SetQualifications([Qualification(DriverQualificationKind.Code95)], Guid.NewGuid)
        };

        foreach (var (name, mutate) in mutators)
        {
            Should.Throw<BusinessException>(mutate, name).Code.ShouldBe(MasterDataErrorCodes.AnonymisedRecordIsReadOnly, name);
        }

        Snapshot(driver).ShouldBe(snapshot, "a refused write leaves the record untouched");
    }

    [Fact]
    public void A_driver_that_is_not_anonymised_accepts_every_mutator()
    {
        var driver = NewDriver();

        driver.SetCode("DRV99");
        driver.SetPerson("Piet", null, "Pietersen", null);
        driver.SetContact("1", "2", "p@acme.nl");
        driver.SetPreferredCulture("en", Cultures);
        driver.SetEmployer(Guid.NewGuid());
        driver.SetUser(Guid.NewGuid());
        driver.SetQualifications([Qualification(DriverQualificationKind.Code95)], Guid.NewGuid);
        driver.Archive();
        driver.Unarchive();

        driver.IsAnonymised.ShouldBeFalse();
        driver.Code.ShouldBe("DRV99");
        driver.FullName.ShouldBe("Piet Pietersen");
        driver.Qualifications.ShouldHaveSingleItem();
    }

    private static string Snapshot(Driver d)
        => string.Join("|", d.Id, d.Code, d.FirstName, d.MiddleName, d.LastName, d.Initials, d.Phone, d.Mobile, d.Email, d.PreferredCulture, d.EmployerRelationId, d.UserId, d.IsArchived, d.IsAnonymised, d.Description, d.SearchKey, d.Qualifications.Count);
}
