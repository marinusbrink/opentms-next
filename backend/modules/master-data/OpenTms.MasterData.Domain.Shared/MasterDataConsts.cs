namespace OpenTms.MasterData;

/// <summary>Field lengths and bounds of the Master Data module (docs/designs/41-master-data-first-run.md).</summary>
public static class MasterDataConsts
{
    public const int MaxNameLength = 128;

    /// <summary>PO decision at gate 1 (2026-10-11): descriptions hold up to 2,500 characters.</summary>
    public const int MaxDescriptionLength = 2500;

    public const int MaxCodeLength = 32;
    public const int IsoCurrencyCodeLength = 3;
    public const int IsoAlpha2Length = 2;
    public const int IsoAlpha3Length = 3;
    public const int MaxSymbolLength = 8;
    public const int MaxTimeZoneIdLength = 64;
    public const int MaxPostalCodePrefixLength = 16;
    public const int MaxPostalCodeLength = 16;
    public const int AdrUnNumberLength = 4;
    public const int MaxAdrClassLength = 8;
    public const int MaxAdrPackingGroupLength = 3;
    public const int MaxAdrDescriptionLength = 256;
    public const int MaxAdrTechnicalNameLength = 256;
    public const int MaxAdrClassificationCodeLength = 8;
    public const int MaxAdrHazardLabelsLength = 32;
    public const int AdrHazardIdentificationNumberMaxLength = 4;
    public const int MaxAdrTunnelRestrictionCodeLength = 8;
    public const int MaxAdrSpecialProvisionsLength = 64;
    public const int MinAdrTransportCategory = 0;
    public const int MaxAdrTransportCategory = 4;
    public const int MinMinorUnits = 0;
    public const int MaxMinorUnits = 4;

    /// <summary>A list page is at most this many rows (the Platform grid contract uses the same bound).</summary>
    public const int MaxPageSize = 200;

    /// <summary>A lookup (picker) returns at most this many rows; a tenant beyond it needs EntityLookup (GAP-01).</summary>
    public const int MaxLookupTake = 500;

    public const int MaxSearchLength = 200;

    // Run 2: Relation and Driver (design 43)
    /// <summary>Folded name, full name and code with separators (128 + 128 + 32).</summary>
    public const int MaxSearchKeyLength = 320;
    public const int MaxPhoneLength = 32;
    public const int MaxEmailLength = 256;
    public const int MaxWebsiteLength = 256;
    public const int MaxLegalNumberLength = 32;

    /* Relation administration (PO 2026-10-13, the old "Administratie" tab). */
    public const int MaxAccountNumberLength = 32;
    public const int MaxCarrierCodeLength = 32;
    public const int MaxExternalIdentifierLength = 128;
    public const int MaxExternalSourceLength = 64;
    public const int MaxExternalIdentifiersPerRelation = 50;
    public const int MaxTransportCharacteristicsPerRelation = 50;
    public const int MaxCultureLength = 10;
    public const int MaxStreetLength = 128;
    public const int MaxHouseNumberLength = 16;
    public const int MaxAddressLine2Length = 128;
    public const int MaxCityLength = 128;
    public const int MaxPersonNameLength = 64;
    public const int MaxMiddleNameLength = 32;
    public const int MaxInitialsLength = 16;
    public const int MaxJobTitleLength = 64;
    public const int MaxIbanLength = 34;
    public const int MaxBicLength = 11;
    public const int MaxBankNameLength = 64;
    public const int MaxAccountHolderLength = 128;
    public const int MaxQualificationNumberLength = 64;
    public const int MaxQualificationDescriptionLength = 256;
    public const int MaxAddressesPerRelation = 50;
    public const int MaxContactPersonsPerRelation = 100;
    public const int MaxBankAccountsPerRelation = 10;
    public const int MaxQualificationsPerDriver = 20;
    public const int LookupPageSize = 25;
    public const int MaxMatchCandidates = 10;
    public const string AnonymisedText = "Anonymised";
    public const int MaxLookupTermLength = 100;
}
