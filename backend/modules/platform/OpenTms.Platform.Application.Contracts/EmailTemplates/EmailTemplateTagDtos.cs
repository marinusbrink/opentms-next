using System.Collections.Generic;

namespace OpenTms.Platform.EmailTemplates;

/// <summary>A group of the tag catalogue for a purpose (design 54, "Tags and default content"), localized.</summary>
public class EmailTemplateTagGroupDto
{
    /// <summary>The group key of the catalogue ("company", "freight", "invoice.lines").</summary>
    public string Group { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public List<EmailTemplateTagDto> Tags { get; set; } = [];
}

/// <summary>A catalogued tag with the text the editor inserts.</summary>
public class EmailTemplateTagDto
{
    /// <summary>The full Scriban path the sender's model exposes ("company.email", "freight.product").</summary>
    public string Path { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public EmailTemplateTagKind Kind { get; set; }
    /// <summary>
    /// The text to insert: <c>{{ company.email }}</c> for a value, <c>{{ line.product }}</c> for a collection's child,
    /// the <c>{{ for line in freight }} … {{ end }}</c> block for the collection itself.
    /// </summary>
    public string Snippet { get; set; } = string.Empty;
    /// <summary>A collection's child fields; empty for a value.</summary>
    public List<EmailTemplateTagDto> Children { get; set; } = [];
}

/// <summary>The default content of a new template for a purpose and language (applied on create while untouched).</summary>
public class EmailTemplateDefaultsDto
{
    /// <summary>"From".</summary>
    public string SenderAddress { get; set; } = string.Empty;
    public string ReplyTo { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string HtmlBody { get; set; } = string.Empty;
}
