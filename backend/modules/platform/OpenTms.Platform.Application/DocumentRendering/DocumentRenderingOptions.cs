using System;

namespace OpenTms.Platform.DocumentRendering;

/// <summary>
/// Platform's document-rendering settings (design 50, "Resilience"), configuration section
/// <c>Platform:DocumentRendering</c>. Host configuration, not tenant data.
/// </summary>
public class DocumentRenderingOptions
{
    public const string SectionName = "Platform:DocumentRendering";

    public static readonly TimeSpan DefaultInteractiveCommandBudget = TimeSpan.FromSeconds(20);

    /// <summary>
    /// The one budget every interactive command of the report definition service (rendering status, open editor, copy
    /// template, test render) shares across all its provider calls; each call keeps its own 15 s limit inside it.
    /// Default 20 s; tests set a short one (e.g. 200 ms).
    /// </summary>
    public TimeSpan InteractiveCommandBudget { get; set; } = DefaultInteractiveCommandBudget;
}
