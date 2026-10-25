namespace OpenTms.Platform.DocumentTypes;

/// <summary>
/// A step of a document type's typed transformation pipeline (design 54, assessment §5): an enum, never a class name
/// resolved by reflection. Executed by the attachments run (GAP-05), not by Platform.
/// </summary>
public enum DocumentTransformationKind
{
    ImageToPdf = 1,
    LinkByFileName = 2,

    /// <summary>Needs an argument: the parent property the file is renamed after.</summary>
    RenameByParentProperty = 3,

    /// <summary>Needs an argument: a .NET regular expression, compiled with a one-second match timeout.</summary>
    SplitPdfByReferencePattern = 4
}
