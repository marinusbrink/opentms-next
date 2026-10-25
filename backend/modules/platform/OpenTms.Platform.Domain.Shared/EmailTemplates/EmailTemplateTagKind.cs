namespace OpenTms.Platform.EmailTemplates;

/// <summary>
/// How a catalogued tag is inserted (design 54, "Tags and default content"): a value as <c>{{ path }}</c>, a collection
/// as a <c>{{ for item in path }} … {{ end }}</c> block over its child fields. Starts at 1 so a missing value fails
/// validation.
/// </summary>
public enum EmailTemplateTagKind
{
    Value = 1,
    Collection = 2
}
