namespace OpenTms.Platform.ReferenceData;

/// <summary>
/// A reference-data row that can be the one default of its scope (assessment §3.8): the default is enforced by a unique
/// partial index and moved only through the service's set-default operation; an archived row is never the default.
/// </summary>
public interface IHasReferenceDataDefault
{
    bool IsDefault { get; }

    bool IsArchived { get; }

    void SetDefault(bool isDefault);
}
