namespace OpenTms.Platform.DocumentRendering;

/// <summary>The output format of a rendered document (design 50). Starts at 1 so a missing value fails validation.</summary>
public enum ReportRenderFormat
{
    Pdf = 1,
    Html = 2,
    Zip = 3,
    Xlsx = 4
}
