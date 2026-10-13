using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.MasterData.Relations;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace OpenTms.MasterData;

/// <summary>
/// Unit tests for the invariants the Relation aggregate enforces itself (design 43, "Relation" and its
/// children). Risk class: Critical for the roles rule (the own-company partial index relies on a valid
/// flags value), the IBAN validation and the single default bank account; High for the child
/// replace semantics, the legal-number normalisation and the search key.
/// </summary>
public class RelationEntityRuleTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Visiting = Guid.NewGuid();
    private static readonly Guid Postal = Guid.NewGuid();
    private static readonly Guid Nl = Guid.NewGuid();
    private static readonly string[] Cultures = ["nl", "en"];

    private static Relation NewRelation(RelationRoles roles = RelationRoles.Customer, string code = "acm01", string name = "ACME Transport")
        => new(Guid.NewGuid(), Tenant, code, name, roles);

    /// <summary>A null <paramref name="typeId"/> is the relation's default address ("all addresses equal", PO 2026-10-12).</summary>
    private static RelationAddressInput Address(Guid? typeId, string street = "Main", string postalCode = "1234 AB", string city = "Amsterdam", Guid? id = null, string? timeZone = null, string? houseNumber = "1", string? suffix = null)
        => new(id, typeId, Nl, street, houseNumber, suffix, null, postalCode, city, null, null, timeZone);

    private static RelationContactPersonInput Contact(string first, string last, Guid? id = null, string? email = null)
        => new(id, first, null, last, ContactPersonRole.Planning, null, null, email, null, null);

    private static RelationBankAccountInput Bank(string iban, bool isDefault = false, Guid? id = null)
        => new(id, iban, null, null, null, isDefault);

    // ── Code, name, search key ────────────────────────────────────────────────

    [Fact]
    public void Construction_normalises_the_code_trims_the_name_and_builds_the_search_key()
    {
        var relation = NewRelation(code: " acm01 ", name: "  Müller & Zn. B.V. ");

        relation.Code.ShouldBe("ACM01");
        relation.Name.ShouldBe("Müller & Zn. B.V.");
        relation.SearchKey.ShouldBe("muller zn b v acm01");
        relation.TenantId.ShouldBe(Tenant);
        relation.IsArchived.ShouldBeFalse();
        relation.Roles.ShouldBe(RelationRoles.Customer);
    }

    [Fact]
    public void Renaming_or_recoding_rebuilds_the_search_key()
    {
        var relation = NewRelation();

        relation.SetName("Jansen Logistiek");
        relation.SearchKey.ShouldBe("jansen logistiek acm01");

        relation.SetCode("jan01");
        relation.Code.ShouldBe("JAN01");
        relation.SearchKey.ShouldBe("jansen logistiek jan01");
    }

    [Fact]
    public void Code_and_name_are_required_and_bounded()
    {
        Should.Throw<BusinessException>(() => NewRelation(code: "  ")).Code.ShouldBe(MasterDataErrorCodes.InvalidCode);
        Should.Throw<BusinessException>(() => NewRelation(code: new string('c', MasterDataConsts.MaxCodeLength + 1))).Code.ShouldBe(MasterDataErrorCodes.InvalidCode);
        Should.Throw<ArgumentException>(() => NewRelation(name: "   "));
        Should.Throw<ArgumentException>(() => NewRelation(name: new string('n', MasterDataConsts.MaxNameLength + 1)));
    }

    // ── Full name (PO 2026-10-12) ────────────────────────────────────────────

    [Fact]
    public void A_new_relation_has_no_full_name_and_its_search_key_folds_name_and_code_only()
    {
        var relation = NewRelation();

        relation.FullName.ShouldBeNull();
        relation.SearchKey.ShouldBe("acme transport acm01");
    }

    [Fact]
    public void SetName_with_a_full_name_trims_both_and_folds_the_full_name_into_the_search_key_between_name_and_code()
    {
        var relation = NewRelation();

        relation.SetName("  ACME Transport ", "  Müller & Zn. Transport Holding B.V.  ");

        relation.Name.ShouldBe("ACME Transport");
        relation.FullName.ShouldBe("Müller & Zn. Transport Holding B.V.");
        relation.SearchKey.ShouldBe("acme transport muller zn transport holding b v acm01", "lower-case, diacritics folded, punctuation collapsed to single spaces");
        relation.SearchKey.ShouldContain(MasterDataValues.SearchKey(relation.FullName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_full_name_is_stored_as_null_and_clears_a_previous_one(string? fullName)
    {
        var relation = NewRelation();
        relation.SetName("ACME Transport", "ACME Transport Holding B.V.");
        relation.FullName.ShouldNotBeNull();

        relation.SetName("ACME Transport", fullName);

        relation.FullName.ShouldBeNull();
        relation.SearchKey.ShouldBe("acme transport acm01", "a cleared full name leaves the search key");
    }

    [Fact]
    public void SetName_without_a_full_name_clears_it_because_the_caller_states_the_whole_name()
    {
        var relation = NewRelation();
        relation.SetName("ACME Transport", "ACME Transport Holding B.V.");

        relation.SetName("Jansen Logistiek");

        relation.Name.ShouldBe("Jansen Logistiek");
        relation.FullName.ShouldBeNull();
        relation.SearchKey.ShouldBe("jansen logistiek acm01");
    }

    [Fact]
    public void Recoding_keeps_the_full_name_in_the_search_key()
    {
        var relation = NewRelation();
        relation.SetName("Jansen", "Jansen Transport Holding B.V.");

        relation.SetCode("jan01");

        relation.FullName.ShouldBe("Jansen Transport Holding B.V.");
        relation.SearchKey.ShouldBe("jansen jansen transport holding b v jan01");
    }

    [Fact]
    public void A_full_name_is_bounded_by_the_name_length_after_trimming()
    {
        var relation = NewRelation();
        var atLimit = new string('f', MasterDataConsts.MaxNameLength);

        relation.SetName("ACME Transport", "  " + atLimit + "  ");
        relation.FullName.ShouldBe(atLimit, "the trimmed value is what is measured");

        Should.Throw<ArgumentException>(() => relation.SetName("ACME Transport", new string('f', MasterDataConsts.MaxNameLength + 1)));
        relation.FullName.ShouldBe(atLimit, "a refused full name leaves the stored one");
        relation.SearchKey.ShouldBe("acme transport " + atLimit + " acm01");
    }

    [Fact]
    public void Name_full_name_and_code_at_their_limits_fit_the_search_key_without_truncation()
    {
        var name = new string('n', MasterDataConsts.MaxNameLength);
        var fullName = new string('f', MasterDataConsts.MaxNameLength);
        var code = new string('c', MasterDataConsts.MaxCodeLength);

        var relation = NewRelation(code: code, name: name);
        relation.SetName(name, fullName);

        relation.SearchKey.ShouldBe($"{name} {fullName} {code}");
        relation.SearchKey.Length.ShouldBe(MasterDataConsts.MaxNameLength * 2 + MasterDataConsts.MaxCodeLength + 2);
        relation.SearchKey.Length.ShouldBeLessThanOrEqualTo(MasterDataConsts.MaxSearchKeyLength, "the column holds name, full name and code with separators (PO 2026-10-12)");
    }

    // ── Roles ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(RelationRoles.Customer)]
    [InlineData(RelationRoles.Carrier)]
    [InlineData(RelationRoles.Creditor)]
    [InlineData(RelationRoles.Depot)]
    [InlineData(RelationRoles.OwnCompany)]
    [InlineData(RelationRoles.Customer | RelationRoles.Carrier)]
    [InlineData(RelationRoles.Customer | RelationRoles.Carrier | RelationRoles.Creditor | RelationRoles.Depot | RelationRoles.OwnCompany)]
    public void Any_combination_of_the_five_roles_is_accepted(RelationRoles roles)
    {
        var relation = NewRelation(roles);

        relation.Roles.ShouldBe(roles);
        foreach (var role in new[] { RelationRoles.Customer, RelationRoles.Carrier, RelationRoles.Creditor, RelationRoles.Depot, RelationRoles.OwnCompany })
        {
            relation.HasRole(role).ShouldBe((roles & role) == role);
        }
    }

    [Theory]
    [InlineData(RelationRoles.None)]
    [InlineData((RelationRoles)32)]
    [InlineData((RelationRoles)33)]
    [InlineData((RelationRoles)(-1))]
    [InlineData((RelationRoles)1024)]
    public void No_role_or_an_undefined_flag_is_refused_with_RolesRequired(RelationRoles roles)
    {
        Should.Throw<BusinessException>(() => NewRelation(roles)).Code.ShouldBe(MasterDataErrorCodes.RolesRequired);

        var relation = NewRelation();
        Should.Throw<BusinessException>(() => relation.SetRoles(roles)).Code.ShouldBe(MasterDataErrorCodes.RolesRequired);
        relation.Roles.ShouldBe(RelationRoles.Customer, "a refused change leaves the roles untouched");
    }

    [Fact]
    public void HasRole_with_a_combined_flag_requires_all_of_its_roles()
    {
        var relation = NewRelation(RelationRoles.Customer | RelationRoles.Carrier);

        relation.HasRole(RelationRoles.Customer | RelationRoles.Carrier).ShouldBeTrue();
        relation.HasRole(RelationRoles.Customer | RelationRoles.Creditor).ShouldBeFalse();
    }

    // ── Archive ──────────────────────────────────────────────────────────────

    [Fact]
    public void Archive_and_unarchive_toggle_the_flag_and_are_idempotent()
    {
        var relation = NewRelation();

        relation.Archive();
        relation.IsArchived.ShouldBeTrue();
        relation.Archive();
        relation.IsArchived.ShouldBeTrue();
        relation.Unarchive();
        relation.IsArchived.ShouldBeFalse();
        relation.Unarchive();
        relation.IsArchived.ShouldBeFalse();
    }

    // ── Blocked flag and reason (PO 2026-10-12) ──────────────────────────────

    [Fact]
    public void A_new_relation_is_not_blocked_and_carries_no_block_reason()
    {
        var relation = NewRelation();

        relation.IsBlocked.ShouldBeFalse();
        relation.BlockReason.ShouldBe(RelationBlockReason.None);
    }

    [Theory]
    [InlineData(RelationBlockReason.PaymentArrears)]
    [InlineData(RelationBlockReason.CreditLimitExceeded)]
    [InlineData(RelationBlockReason.Insolvency)]
    [InlineData(RelationBlockReason.Dispute)]
    [InlineData(RelationBlockReason.Other)]
    public void Blocking_with_a_defined_reason_stores_the_flag_and_the_reason(RelationBlockReason reason)
    {
        var relation = NewRelation();

        relation.SetBlocked(true, reason);

        relation.IsBlocked.ShouldBeTrue();
        relation.BlockReason.ShouldBe(reason);
        relation.IsArchived.ShouldBeFalse("blocked is not archived");
    }

    [Fact]
    public void The_reason_of_a_blocked_relation_can_change_while_it_stays_blocked()
    {
        var relation = NewRelation();
        relation.SetBlocked(true, RelationBlockReason.PaymentArrears);

        relation.SetBlocked(true, RelationBlockReason.Insolvency);

        relation.IsBlocked.ShouldBeTrue();
        relation.BlockReason.ShouldBe(RelationBlockReason.Insolvency);
    }

    [Theory]
    [InlineData(RelationBlockReason.None)]
    [InlineData(RelationBlockReason.PaymentArrears)]
    [InlineData(RelationBlockReason.Other)]
    public void Unblocking_stores_not_blocked_and_None_whatever_reason_is_passed(RelationBlockReason reason)
    {
        var relation = NewRelation();
        relation.SetBlocked(true, RelationBlockReason.Dispute);

        relation.SetBlocked(false, reason);

        relation.IsBlocked.ShouldBeFalse();
        relation.BlockReason.ShouldBe(RelationBlockReason.None, "a reason exists only while the relation is blocked");

        /* On a relation that was never blocked the call ends in the same state. */
        var fresh = NewRelation(code: "FRS");
        fresh.SetBlocked(false, reason);
        fresh.IsBlocked.ShouldBeFalse();
        fresh.BlockReason.ShouldBe(RelationBlockReason.None);
    }

    [Fact]
    public void Blocking_without_a_reason_is_refused_with_BlockReasonRequired_and_leaves_the_aggregate_unchanged()
    {
        var relation = NewRelation();

        Should.Throw<BusinessException>(() => relation.SetBlocked(true, RelationBlockReason.None)).Code.ShouldBe(MasterDataErrorCodes.BlockReasonRequired);
        relation.IsBlocked.ShouldBeFalse();
        relation.BlockReason.ShouldBe(RelationBlockReason.None);

        /* An already blocked relation keeps its reason: "blocked without a reason" is never a stored state. */
        relation.SetBlocked(true, RelationBlockReason.CreditLimitExceeded);
        Should.Throw<BusinessException>(() => relation.SetBlocked(true, RelationBlockReason.None)).Code.ShouldBe(MasterDataErrorCodes.BlockReasonRequired);
        relation.IsBlocked.ShouldBeTrue();
        relation.BlockReason.ShouldBe(RelationBlockReason.CreditLimitExceeded);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(42)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void An_undefined_reason_is_refused_with_InvalidCode_whether_blocking_or_unblocking(int value)
    {
        var reason = (RelationBlockReason)value;
        var relation = NewRelation();
        relation.SetBlocked(true, RelationBlockReason.Dispute);

        foreach (var blocked in new[] { true, false })
        {
            var ex = Should.Throw<BusinessException>(() => relation.SetBlocked(blocked, reason));
            ex.Code.ShouldBe(MasterDataErrorCodes.InvalidCode);
            ex.Data["field"].ShouldBe("blockReason");
            ex.Data["value"].ShouldBe(value);
            relation.IsBlocked.ShouldBeTrue("a refused change leaves the flag untouched");
            relation.BlockReason.ShouldBe(RelationBlockReason.Dispute, "a refused change leaves the reason untouched");
        }
    }

    // ── Contact and legal fields ─────────────────────────────────────────────

    [Fact]
    public void SetContact_trims_turns_empty_into_null_and_checks_the_email_shape()
    {
        var relation = NewRelation();

        relation.SetContact(" 020-1234567 ", "", "  planner@acme.nl ", " https://acme.nl ");
        relation.Phone.ShouldBe("020-1234567");
        relation.Mobile.ShouldBeNull();
        relation.Email.ShouldBe("planner@acme.nl");
        relation.Website.ShouldBe("https://acme.nl");

        var ex = Should.Throw<BusinessException>(() => relation.SetContact(null, null, "not-an-email", null));
        ex.Code.ShouldBe(MasterDataErrorCodes.InvalidEmail);
        ex.Data["value"].ShouldBeNull("an e-mail address is personal data and never reaches the log (constitution rule 5)");
        Should.Throw<ArgumentException>(() => relation.SetContact(new string('1', MasterDataConsts.MaxPhoneLength + 1), null, null, null));
    }

    [Fact]
    public void SetLegal_normalises_both_numbers_and_checks_the_vat_shape()
    {
        var relation = NewRelation();

        relation.SetLegal(" 12 34 56 78 ", " nl 8012.34.567.b01 ");
        relation.ChamberOfCommerceNumber.ShouldBe("12345678");
        relation.VatNumber.ShouldBe("NL801234567B01");

        relation.SetLegal(null, "");
        relation.ChamberOfCommerceNumber.ShouldBeNull();
        relation.VatNumber.ShouldBeNull();

        var ex = Should.Throw<BusinessException>(() => relation.SetLegal(null, "801234567B01"));
        ex.Code.ShouldBe(MasterDataErrorCodes.InvalidVatNumber);
        ex.Data["value"].ShouldBe("801234567B01");
        Should.Throw<BusinessException>(() => relation.SetLegal(null, "NL" + new string('1', 14))).Code.ShouldBe(MasterDataErrorCodes.InvalidVatNumber);
        Should.Throw<BusinessException>(() => relation.SetLegal(new string('1', MasterDataConsts.MaxLegalNumberLength + 1), null)).Code.ShouldBe(MasterDataErrorCodes.InvalidCode);
    }

    [Fact]
    public void SetPreferredCulture_accepts_configured_cultures_only()
    {
        var relation = NewRelation();

        relation.SetPreferredCulture(" NL ", Cultures);
        relation.PreferredCulture.ShouldBe("nl");
        relation.SetPreferredCulture(null, Cultures);
        relation.PreferredCulture.ShouldBeNull();

        var ex = Should.Throw<BusinessException>(() => relation.SetPreferredCulture("de", Cultures));
        ex.Code.ShouldBe(MasterDataErrorCodes.InvalidCulture);
        ex.Data["value"].ShouldBe("de");
    }

    // ── Addresses ────────────────────────────────────────────────────────────

    [Fact]
    public void SetAddresses_replaces_by_id_matches_new_rows_by_type_and_removes_the_rest()
    {
        var relation = NewRelation();
        relation.SetAddresses([Address(Visiting, "Old street"), Address(Postal, "PO box")], Guid.NewGuid);
        relation.Addresses.Count.ShouldBe(2);
        var visiting = relation.Addresses.Single(a => a.AddressTypeId == Visiting);
        var postal = relation.Addresses.Single(a => a.AddressTypeId == Postal);
        visiting.RelationId.ShouldBe(relation.Id);
        visiting.TenantId.ShouldBe(Tenant);
        visiting.PostalCodeKey.ShouldBe("1234AB");

        /* Update by id, keep the postal row by type without an id, nothing removed. */
        relation.SetAddresses([Address(Visiting, "New street", id: visiting.Id), Address(Postal, "PO box 2")], Guid.NewGuid);
        relation.Addresses.Count.ShouldBe(2);
        relation.Addresses.Single(a => a.Id == visiting.Id).Street.ShouldBe("New street");
        relation.Addresses.Single(a => a.Id == postal.Id).Street.ShouldBe("PO box 2");

        /* Drop the postal address. */
        relation.SetAddresses([Address(Visiting, id: visiting.Id)], Guid.NewGuid);
        relation.Addresses.ShouldHaveSingleItem().Id.ShouldBe(visiting.Id);

        relation.SetAddresses([], Guid.NewGuid);
        relation.Addresses.ShouldBeEmpty();
    }

    [Fact]
    public void Two_addresses_of_the_same_type_are_refused_with_DuplicateKey()
    {
        var relation = NewRelation();

        var ex = Should.Throw<BusinessException>(() => relation.SetAddresses([Address(Visiting), Address(Visiting, "Other")], Guid.NewGuid));
        ex.Code.ShouldBe(MasterDataErrorCodes.DuplicateKey);
        ex.Data["field"].ShouldBe("addressTypeId");
        relation.Addresses.ShouldBeEmpty();
    }

    // ── Default address ("all addresses equal", PO 2026-10-12) ───────────────

    [Fact]
    public void A_default_address_without_type_coexists_with_typed_addresses()
    {
        var relation = NewRelation();

        relation.SetAddresses([Address(Visiting, "Visit street"), Address(null, "Default street", city: "Utrecht"), Address(Postal, "PO box")], Guid.NewGuid);

        relation.Addresses.Count.ShouldBe(3);
        var @default = relation.Addresses.Single(a => a.AddressTypeId == null);
        @default.Street.ShouldBe("Default street");
        @default.City.ShouldBe("Utrecht");
        @default.RelationId.ShouldBe(relation.Id);
        @default.TenantId.ShouldBe(Tenant);
        @default.PostalCodeKey.ShouldBe("1234AB");
        relation.Addresses.Where(a => a.AddressTypeId != null).Select(a => a.AddressTypeId!.Value).ShouldBe([Visiting, Postal], ignoreOrder: true);

        /* A default address on its own is a valid address set too. */
        var alone = NewRelation(code: "DEF");
        alone.SetAddresses([Address(null)], Guid.NewGuid);
        alone.Addresses.ShouldHaveSingleItem().AddressTypeId.ShouldBeNull();
    }

    [Fact]
    public void Two_default_addresses_are_refused_with_DuplicateKey_like_two_of_one_type()
    {
        var relation = NewRelation();

        var ex = Should.Throw<BusinessException>(() => relation.SetAddresses([Address(null), Address(null, "Other")], Guid.NewGuid));
        ex.Code.ShouldBe(MasterDataErrorCodes.DuplicateKey);
        ex.Data["field"].ShouldBe("addressTypeId");
        relation.Addresses.ShouldBeEmpty("a refused set leaves the aggregate untouched");

        /* The same with typed rows in between: null counts as a type of its own. */
        Should.Throw<BusinessException>(() => relation.SetAddresses([Address(null), Address(Visiting), Address(Postal), Address(null, "Other")], Guid.NewGuid)).Code.ShouldBe(MasterDataErrorCodes.DuplicateKey);
        relation.Addresses.ShouldBeEmpty();

        /* On an existing set the second default is refused as well, and the set is untouched. */
        relation.SetAddresses([Address(null, "Kept"), Address(Visiting)], Guid.NewGuid);
        var keptId = relation.Addresses.Single(a => a.AddressTypeId == null).Id;
        Should.Throw<BusinessException>(() => relation.SetAddresses([Address(null, "Kept", id: keptId), Address(null, "Second")], Guid.NewGuid)).Code.ShouldBe(MasterDataErrorCodes.DuplicateKey);
        relation.Addresses.Count.ShouldBe(2);
        relation.Addresses.Single(a => a.AddressTypeId == null).Street.ShouldBe("Kept");
    }

    [Fact]
    public void The_default_address_is_replaced_in_place_by_id_or_by_its_null_type()
    {
        var relation = NewRelation();
        relation.SetAddresses([Address(null, "Default 1"), Address(Visiting, "Visit 1")], Guid.NewGuid);
        var defaultId = relation.Addresses.Single(a => a.AddressTypeId == null).Id;
        var visitingId = relation.Addresses.Single(a => a.AddressTypeId == Visiting).Id;

        /* By id: the row keeps its identity and takes the new values. */
        relation.SetAddresses([Address(null, "Default 2", id: defaultId), Address(Visiting, "Visit 1", id: visitingId)], Guid.NewGuid);
        relation.Addresses.Count.ShouldBe(2);
        relation.Addresses.Single(a => a.Id == defaultId).Street.ShouldBe("Default 2");
        relation.Addresses.Single(a => a.Id == defaultId).AddressTypeId.ShouldBeNull();

        /* Without an id: the null type matches the existing default row, nothing is duplicated. */
        relation.SetAddresses([Address(null, "Default 3"), Address(Visiting, "Visit 2")], Guid.NewGuid);
        relation.Addresses.Count.ShouldBe(2);
        relation.Addresses.Single(a => a.AddressTypeId == null).Id.ShouldBe(defaultId);
        relation.Addresses.Single(a => a.AddressTypeId == null).Street.ShouldBe("Default 3");
        relation.Addresses.Single(a => a.AddressTypeId == Visiting).Id.ShouldBe(visitingId);
        relation.Addresses.Single(a => a.AddressTypeId == Visiting).Street.ShouldBe("Visit 2");

        /* A typed row may become the default by id (and vice versa) as long as the set stays unique. */
        relation.SetAddresses([Address(null, "Was visiting", id: visitingId)], Guid.NewGuid);
        var only = relation.Addresses.ShouldHaveSingleItem();
        only.Id.ShouldBe(visitingId);
        only.AddressTypeId.ShouldBeNull();
        only.Street.ShouldBe("Was visiting");

        /* Dropping the default leaves the typed addresses alone. */
        relation.SetAddresses([Address(null, "Back", id: visitingId), Address(Postal, "PO box")], Guid.NewGuid);
        relation.SetAddresses([Address(Postal, "PO box")], Guid.NewGuid);
        relation.Addresses.ShouldHaveSingleItem().AddressTypeId.ShouldBe(Postal);
    }

    [Fact]
    public void More_than_fifty_addresses_are_refused_with_TooManyChildren()
    {
        var relation = NewRelation();
        var inputs = Enumerable.Range(0, MasterDataConsts.MaxAddressesPerRelation + 1).Select(_ => Address(Guid.NewGuid())).ToList();

        var ex = Should.Throw<BusinessException>(() => relation.SetAddresses(inputs, Guid.NewGuid));
        ex.Code.ShouldBe(MasterDataErrorCodes.TooManyChildren);
        ex.Data["max"].ShouldBe(MasterDataConsts.MaxAddressesPerRelation);

        relation.SetAddresses(inputs.Take(MasterDataConsts.MaxAddressesPerRelation), Guid.NewGuid);
        relation.Addresses.Count.ShouldBe(MasterDataConsts.MaxAddressesPerRelation);
    }

    [Fact]
    public void An_address_validates_its_fields_time_zone_and_coordinates()
    {
        var address = new RelationAddress(Guid.NewGuid(), Tenant, Guid.NewGuid());

        address.Set(new RelationAddressInput(null, Visiting, Nl, " Main ", " 12a ", "", null, " 1234 ab ", " Amsterdam ", 52.37, 4.89, " Europe/Amsterdam "));
        address.Street.ShouldBe("Main");
        address.HouseNumber.ShouldBe("12a");
        address.AddressLine2.ShouldBeNull();
        address.PostalCode.ShouldBe("1234 ab");
        address.PostalCodeKey.ShouldBe("1234AB");
        address.City.ShouldBe("Amsterdam");
        address.Latitude.ShouldBe(52.37);
        address.Longitude.ShouldBe(4.89);
        address.TimeZoneId.ShouldBe("Europe/Amsterdam");

        Should.Throw<ArgumentException>(() => address.Set(Address(Visiting, street: " ")));
        Should.Throw<ArgumentException>(() => address.Set(Address(Visiting, postalCode: "")));
        Should.Throw<ArgumentException>(() => address.Set(Address(Visiting, city: "")));
        Should.Throw<BusinessException>(() => address.Set(new RelationAddressInput(null, Visiting, Nl, "Main", null, null, null, "1234", "City", 91, 0, null))).Code.ShouldBe(MasterDataErrorCodes.InvalidCode);
        Should.Throw<BusinessException>(() => address.Set(new RelationAddressInput(null, Visiting, Nl, "Main", null, null, null, "1234", "City", 0, -181, null))).Code.ShouldBe(MasterDataErrorCodes.InvalidCode);
        var ex = Should.Throw<BusinessException>(() => address.Set(Address(Visiting, timeZone: "Mars/Olympus")));
        ex.Code.ShouldBe(MasterDataErrorCodes.InvalidTimeZone);
        ex.Data["value"].ShouldBe("Mars/Olympus");
    }

    // ── House-number suffix (PO 2026-10-13) ──────────────────────────────────

    [Fact]
    public void The_house_number_suffix_is_stored_trimmed_next_to_the_number_and_shares_its_length()
    {
        var address = new RelationAddress(Guid.NewGuid(), Tenant, Guid.NewGuid());

        address.Set(Address(Visiting, houseNumber: " 12 ", suffix: " a "));

        address.HouseNumber.ShouldBe("12");
        address.HouseNumberSuffix.ShouldBe("a", "the suffix is kept apart from the number, trimmed");

        /* Any text is a suffix: letters, a range dash, a word; the limit is the house-number length after trimming. */
        address.Set(Address(Visiting, houseNumber: "12", suffix: "-14"));
        address.HouseNumberSuffix.ShouldBe("-14");
        var atLimit = new string('s', MasterDataConsts.MaxHouseNumberLength);
        address.Set(Address(Visiting, houseNumber: "12", suffix: "  " + atLimit + "  "));
        address.HouseNumberSuffix.ShouldBe(atLimit, "the trimmed value is what is measured");

        /* A suffix without a number is allowed by the entity: both fields are optional and independent. */
        address.Set(Address(Visiting, houseNumber: null, suffix: "bis"));
        address.HouseNumber.ShouldBeNull();
        address.HouseNumberSuffix.ShouldBe("bis");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_house_number_suffix_is_stored_as_null_and_clears_a_previous_one(string? suffix)
    {
        var address = new RelationAddress(Guid.NewGuid(), Tenant, Guid.NewGuid());
        address.Set(Address(Visiting, houseNumber: "12", suffix: "a"));
        address.HouseNumberSuffix.ShouldBe("a");

        address.Set(Address(Visiting, houseNumber: "12", suffix: suffix));

        address.HouseNumber.ShouldBe("12", "clearing the suffix leaves the number");
        address.HouseNumberSuffix.ShouldBeNull();
    }

    [Fact]
    public void A_seventeen_character_house_number_suffix_is_refused_and_adds_nothing_to_the_aggregate()
    {
        var tooLong = new string('s', MasterDataConsts.MaxHouseNumberLength + 1);

        /* On a fresh child the refusal is an argument error like every other over-long optional text. */
        var address = new RelationAddress(Guid.NewGuid(), Tenant, Guid.NewGuid());
        Should.Throw<ArgumentException>(() => address.Set(Address(Visiting, houseNumber: "12", suffix: tooLong)));

        /* Sixteen characters of padding around a short suffix are not a seventeen-character suffix. */
        address.Set(Address(Visiting, houseNumber: "12", suffix: new string(' ', 8) + "a" + new string(' ', 8)));
        address.HouseNumberSuffix.ShouldBe("a");

        /* On an empty aggregate a refused set adds nothing. */
        var fresh = NewRelation(code: "FRS");
        Should.Throw<ArgumentException>(() => fresh.SetAddresses([Address(Visiting, houseNumber: "12", suffix: tooLong)], Guid.NewGuid));
        fresh.Addresses.ShouldBeEmpty("a refused set on an empty aggregate adds nothing");

        /* Next to an existing row a refused new row is not added and the existing row keeps its values; nothing is removed either. */
        var relation = NewRelation();
        relation.SetAddresses([Address(Visiting, "Kept", houseNumber: "12", suffix: "a")], Guid.NewGuid);
        var keptId = relation.Addresses.Single().Id;
        Should.Throw<ArgumentException>(() => relation.SetAddresses([Address(Visiting, "Kept", id: keptId, houseNumber: "12", suffix: "a"), Address(Postal, "New", houseNumber: "1", suffix: tooLong)], Guid.NewGuid));
        var kept = relation.Addresses.ShouldHaveSingleItem();
        kept.Id.ShouldBe(keptId);
        kept.Street.ShouldBe("Kept");
        kept.HouseNumber.ShouldBe("12");
        kept.HouseNumberSuffix.ShouldBe("a");
    }

    [Fact]
    public void A_refused_house_number_suffix_leaves_an_existing_address_row_as_it_was()
    {
        /* The aggregate promises "a refused input leaves the aggregate untouched" (Relation.SetAddresses). For a row updated in place that
           means the row keeps every field it had, like Relation.SetName keeps the stored full name when the new one is refused. */
        var tooLong = new string('s', MasterDataConsts.MaxHouseNumberLength + 1);

        var address = new RelationAddress(Guid.NewGuid(), Tenant, Guid.NewGuid());
        address.Set(Address(Visiting, street: "Kept", houseNumber: "12", suffix: "a"));
        Should.Throw<ArgumentException>(() => address.Set(Address(Visiting, street: "Changed", houseNumber: "99", suffix: tooLong)));
        address.Street.ShouldBe("Kept", "a refused change leaves the row untouched");
        address.HouseNumber.ShouldBe("12", "a refused change leaves the row untouched");
        address.HouseNumberSuffix.ShouldBe("a");

        var relation = NewRelation();
        relation.SetAddresses([Address(Visiting, "Kept", houseNumber: "12", suffix: "a")], Guid.NewGuid);
        var keptId = relation.Addresses.Single().Id;
        Should.Throw<ArgumentException>(() => relation.SetAddresses([Address(Visiting, "Changed", id: keptId, houseNumber: "99", suffix: tooLong)], Guid.NewGuid));
        var kept = relation.Addresses.ShouldHaveSingleItem();
        kept.Id.ShouldBe(keptId);
        kept.Street.ShouldBe("Kept", "a refused change leaves the row untouched");
        kept.HouseNumber.ShouldBe("12", "a refused change leaves the row untouched");
        kept.HouseNumberSuffix.ShouldBe("a");
    }

    [Fact]
    public void DefaultTimeZone_fills_the_countrys_zone_only_when_the_address_has_none()
    {
        var address = new RelationAddress(Guid.NewGuid(), Tenant, Guid.NewGuid());

        address.Set(Address(Visiting));
        address.TimeZoneId.ShouldBeNull();
        address.DefaultTimeZone(null);
        address.TimeZoneId.ShouldBeNull();
        address.DefaultTimeZone("Europe/Amsterdam");
        address.TimeZoneId.ShouldBe("Europe/Amsterdam");

        address.Set(Address(Visiting, timeZone: "Europe/Brussels"));
        address.DefaultTimeZone("Europe/Amsterdam");
        address.TimeZoneId.ShouldBe("Europe/Brussels");
    }

    // ── Contact persons ──────────────────────────────────────────────────────

    [Fact]
    public void SetContactPersons_updates_by_id_adds_rows_without_id_and_removes_the_rest()
    {
        var relation = NewRelation();
        relation.SetContactPersons([Contact("Jan", "Jansen"), Contact("Piet", "Pietersen")], Guid.NewGuid);
        var jan = relation.ContactPersons.Single(c => c.FirstName == "Jan");
        var piet = relation.ContactPersons.Single(c => c.FirstName == "Piet");
        jan.RelationId.ShouldBe(relation.Id);
        jan.TenantId.ShouldBe(Tenant);

        relation.SetContactPersons([Contact("Jan", "de Vries", id: jan.Id), Contact("Kees", "Klaassen")], Guid.NewGuid);

        relation.ContactPersons.Count.ShouldBe(2);
        relation.ContactPersons.Single(c => c.Id == jan.Id).LastName.ShouldBe("de Vries");
        relation.ContactPersons.ShouldNotContain(c => c.Id == piet.Id);
        relation.ContactPersons.ShouldContain(c => c.FirstName == "Kees");

        /* An unknown id is treated as a new row, never as a reach into another relation's child. */
        relation.SetContactPersons([Contact("Jan", "de Vries", id: Guid.NewGuid())], Guid.NewGuid);
        relation.ContactPersons.ShouldHaveSingleItem().Id.ShouldNotBe(jan.Id);
    }

    [Fact]
    public void A_contact_person_validates_names_role_and_email()
    {
        var person = new RelationContactPerson(Guid.NewGuid(), Tenant, Guid.NewGuid());

        person.Set(new RelationContactPersonInput(null, " Jan ", " ", " Jansen ", ContactPersonRole.Invoicing, " Controller ", "", " jan@acme.nl ", " 020 ", null));
        person.FirstName.ShouldBe("Jan");
        person.MiddleName.ShouldBeNull();
        person.LastName.ShouldBe("Jansen");
        person.Role.ShouldBe(ContactPersonRole.Invoicing);
        person.JobTitle.ShouldBe("Controller");
        person.Department.ShouldBeNull();
        person.Email.ShouldBe("jan@acme.nl");
        person.Phone.ShouldBe("020");
        person.Mobile.ShouldBeNull();

        Should.Throw<ArgumentException>(() => person.Set(Contact("", "Jansen")));
        Should.Throw<ArgumentException>(() => person.Set(Contact("Jan", "")));
        Should.Throw<BusinessException>(() => person.Set(new RelationContactPersonInput(null, "Jan", null, "Jansen", (ContactPersonRole)9, null, null, null, null, null))).Code.ShouldBe(MasterDataErrorCodes.InvalidCode);
        Should.Throw<BusinessException>(() => person.Set(Contact("Jan", "Jansen", email: "jan@"))).Code.ShouldBe(MasterDataErrorCodes.InvalidEmail);
    }

    [Fact]
    public void More_than_a_hundred_contact_persons_are_refused_with_TooManyChildren()
    {
        var relation = NewRelation();
        var inputs = Enumerable.Range(0, MasterDataConsts.MaxContactPersonsPerRelation + 1).Select(i => Contact("P", $"Person {i}")).ToList();

        Should.Throw<BusinessException>(() => relation.SetContactPersons(inputs, Guid.NewGuid)).Code.ShouldBe(MasterDataErrorCodes.TooManyChildren);
        relation.SetContactPersons(inputs.Take(MasterDataConsts.MaxContactPersonsPerRelation), Guid.NewGuid);
        relation.ContactPersons.Count.ShouldBe(MasterDataConsts.MaxContactPersonsPerRelation);
    }

    // ── Bank accounts ────────────────────────────────────────────────────────

    [Fact]
    public void The_first_bank_account_becomes_the_default_when_none_is_marked()
    {
        var relation = NewRelation();

        relation.SetBankAccounts([Bank("NL20 INGB 0001 2345 67"), Bank("DE89370400440532013000")], Guid.NewGuid);

        relation.BankAccounts.Count.ShouldBe(2);
        relation.BankAccounts.Single(b => b.IsDefault).Iban.ShouldBe("DE89370400440532013000", "the lowest IBAN is the deterministic choice");
        relation.BankAccounts.Single(b => b.Iban == "NL20INGB0001234567").IsDefault.ShouldBeFalse();
    }

    [Fact]
    public void A_single_bank_account_is_always_the_default()
    {
        var relation = NewRelation();

        relation.SetBankAccounts([Bank("NL91ABNA0417164300", isDefault: false)], Guid.NewGuid);

        relation.BankAccounts.ShouldHaveSingleItem().IsDefault.ShouldBeTrue();
    }

    [Fact]
    public void The_marked_default_is_kept_and_may_be_moved_to_another_account()
    {
        var relation = NewRelation();
        relation.SetBankAccounts([Bank("NL20INGB0001234567"), Bank("DE89370400440532013000", isDefault: true)], Guid.NewGuid);
        relation.BankAccounts.Single(b => b.IsDefault).Iban.ShouldBe("DE89370400440532013000");
        var nl = relation.BankAccounts.Single(b => b.Iban == "NL20INGB0001234567");
        var de = relation.BankAccounts.Single(b => b.Iban == "DE89370400440532013000");

        relation.SetBankAccounts([Bank("NL20INGB0001234567", isDefault: true, id: nl.Id), Bank("DE89370400440532013000", id: de.Id)], Guid.NewGuid);

        relation.BankAccounts.Count.ShouldBe(2);
        relation.BankAccounts.Single(b => b.IsDefault).Id.ShouldBe(nl.Id);
        relation.BankAccounts.Single(b => b.Id == de.Id).IsDefault.ShouldBeFalse();
    }

    [Fact]
    public void Removing_the_default_account_promotes_another_one()
    {
        var relation = NewRelation();
        relation.SetBankAccounts([Bank("NL20INGB0001234567", isDefault: true), Bank("DE89370400440532013000")], Guid.NewGuid);
        var de = relation.BankAccounts.Single(b => b.Iban == "DE89370400440532013000");

        relation.SetBankAccounts([Bank("DE89370400440532013000", id: de.Id)], Guid.NewGuid);

        relation.BankAccounts.ShouldHaveSingleItem().IsDefault.ShouldBeTrue();
    }

    [Fact]
    public void Two_defaults_a_duplicate_iban_or_an_invalid_iban_are_refused()
    {
        var relation = NewRelation();

        var twoDefaults = Should.Throw<BusinessException>(() => relation.SetBankAccounts([Bank("NL20INGB0001234567", true), Bank("DE89370400440532013000", true)], Guid.NewGuid));
        twoDefaults.Code.ShouldBe(MasterDataErrorCodes.DuplicateKey);
        twoDefaults.Data["field"].ShouldBe("isDefault");

        var duplicate = Should.Throw<BusinessException>(() => relation.SetBankAccounts([Bank("NL20INGB0001234567"), Bank("nl20 ingb 0001 2345 67")], Guid.NewGuid));
        duplicate.Code.ShouldBe(MasterDataErrorCodes.DuplicateKey);
        duplicate.Data["field"].ShouldBe("iban");

        var invalid = Should.Throw<BusinessException>(() => relation.SetBankAccounts([Bank("NL91ABNA0417164301")], Guid.NewGuid));
        invalid.Code.ShouldBe(MasterDataErrorCodes.InvalidIban);
        invalid.Data["value"].ShouldBeNull("an IBAN is personal data and never reaches the log (constitution rule 5)");
        Should.Throw<BusinessException>(() => relation.SetBankAccounts([Bank("")], Guid.NewGuid)).Code.ShouldBe(MasterDataErrorCodes.InvalidIban);

        relation.BankAccounts.ShouldBeEmpty();
    }

    [Fact]
    public void More_than_ten_bank_accounts_are_refused_with_TooManyChildren()
    {
        var relation = NewRelation();
        /* Eleven distinct valid IBANs: NL check digits recomputed per account number. */
        var ibans = Enumerable.Range(0, MasterDataConsts.MaxBankAccountsPerRelation + 1).Select(i => ValidNlIban($"TEST{i:D10}")).ToList();

        Should.Throw<BusinessException>(() => relation.SetBankAccounts(ibans.Select(i => Bank(i)), Guid.NewGuid)).Code.ShouldBe(MasterDataErrorCodes.TooManyChildren);
        relation.SetBankAccounts(ibans.Take(MasterDataConsts.MaxBankAccountsPerRelation).Select(i => Bank(i)), Guid.NewGuid);
        relation.BankAccounts.Count.ShouldBe(MasterDataConsts.MaxBankAccountsPerRelation);
        relation.BankAccounts.Count(b => b.IsDefault).ShouldBe(1);
    }

    [Fact]
    public void A_bank_account_normalises_the_iban_and_bic_and_bounds_the_texts()
    {
        var account = new RelationBankAccount(Guid.NewGuid(), Tenant, Guid.NewGuid());

        account.Set(new RelationBankAccountInput(null, " nl91 abna 0417 1643 00 ", " abnanl2a ", " ABN AMRO ", " ACME B.V. ", true));

        account.Iban.ShouldBe("NL91ABNA0417164300");
        account.Bic.ShouldBe("ABNANL2A");
        account.BankName.ShouldBe("ABN AMRO");
        account.AccountHolder.ShouldBe("ACME B.V.");
        account.IsDefault.ShouldBeTrue();
        Should.Throw<ArgumentException>(() => account.Set(new RelationBankAccountInput(null, "NL91ABNA0417164300", new string('B', MasterDataConsts.MaxBicLength + 1), null, null, false)));
    }

    // ── Memberships ──────────────────────────────────────────────────────────

    [Fact]
    public void Join_rows_are_synced_as_a_set_with_duplicates_ignored()
    {
        var relation = NewRelation();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();

        relation.SetCategories([a, b, a]);
        relation.Categories.Select(m => m.RelationCategoryId).ShouldBe([a, b], ignoreOrder: true);
        relation.Categories.ShouldAllBe(m => m.RelationId == relation.Id && m.TenantId == Tenant);
        var keptRow = relation.Categories.Single(m => m.RelationCategoryId == b);

        relation.SetCategories([b, c]);
        relation.Categories.Select(m => m.RelationCategoryId).ShouldBe([b, c], ignoreOrder: true);
        relation.Categories.ShouldContain(keptRow, "an unchanged membership row is kept, not recreated");

        relation.SetGeographicalRegions([a]);
        relation.GeographicalRegions.ShouldHaveSingleItem().GeographicalRegionId.ShouldBe(a);
        relation.SetGeographicalRegions([]);
        relation.GeographicalRegions.ShouldBeEmpty();

        relation.SetOrganizationUnits([a, b]);
        relation.OrganizationUnits.Count.ShouldBe(2);
        relation.OrganizationUnits.First().GetKeys().ShouldBe([relation.Id, relation.OrganizationUnits.First().OrganizationUnitId]);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>A Dutch IBAN with the correct check digits for a given 14-character BBAN.</summary>
    private static string ValidNlIban(string bban)
    {
        var rearranged = bban + "NL00";
        var remainder = 0;
        foreach (var ch in rearranged)
        {
            var value = char.IsDigit(ch) ? ch - '0' : ch - 'A' + 10;
            remainder = value >= 10 ? (remainder * 100 + value) % 97 : (remainder * 10 + value) % 97;
        }

        return $"NL{98 - remainder:D2}{bban}";
    }
}
