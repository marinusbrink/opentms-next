namespace OpenTms.Platform.DerivedValues;

/// <summary>The business error codes of design 49; localized in the Platform resource.</summary>
public static class DerivedValueErrorCodes
{
    public const string Namespace = "OpenTms.Platform";

    public const string UnknownTargetType = Namespace + ":DerivedValues.UnknownTargetType";
    public const string TooManyTargets = Namespace + ":DerivedValues.TooManyTargets";
    public const string RequiresTransaction = Namespace + ":DerivedValues.RequiresTransaction";

    public static readonly string[] BadRequestCodes = [UnknownTargetType, TooManyTargets];
}
