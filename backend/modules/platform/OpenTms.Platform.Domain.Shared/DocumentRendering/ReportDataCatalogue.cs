using System.Text.Json.Nodes;
using OpenTms.Platform.ReportDefinitions;

namespace OpenTms.Platform.DocumentRendering;

/// <summary>
/// The report data catalogue (design 50, "Report data catalogue"): per <see cref="ReportPurpose"/> the JSON a template
/// receives, in the OpenTMS envelope <c>{ CompanyLogo, Me, Entity, EntityType }</c> so FiLogic's existing templates
/// render unchanged. This version holds the envelope and the OpenTMS entity type name per purpose; the field list per
/// purpose (path, type, description, sample value) follows as design 50 describes.
/// </summary>
public static class ReportDataCatalogue
{
    /// <summary>A complete sample payload for the purpose: used when a template has no sample data of its own.</summary>
    public static JsonObject SampleFor(ReportPurpose purpose) => new()
    {
        ["CompanyLogo"] = "",
        ["Me"] = new JsonObject(),
        ["Entity"] = new JsonObject(),
        ["EntityType"] = EntityTypeOf(purpose)
    };

    /// <summary>
    /// The entity type name OpenTMS put in <c>EntityType</c> (the CLR name of its payload class). The transport-unit
    /// balance keeps OpenTMS's extra wrapper, whose generic type name OpenTMS sent as is.
    /// </summary>
    public static string EntityTypeOf(ReportPurpose purpose) => purpose switch
    {
        ReportPurpose.TransportOrderConfirmation => "TransportOrder",
        ReportPurpose.TransportFile => "TransportFile",
        ReportPurpose.Invoice => "Invoice",
        ReportPurpose.PurchaseOrder => "PurchaseOrder",
        ReportPurpose.TransportUnitBalance => "GetSingleOutput`1",
        _ => purpose.ToString()
    };
}
