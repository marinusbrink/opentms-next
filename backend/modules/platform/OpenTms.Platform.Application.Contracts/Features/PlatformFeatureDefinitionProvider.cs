using OpenTms.Platform.Localization;
using Volo.Abp.Features;
using Volo.Abp.Localization;
using Volo.Abp.Validation.StringValues;

namespace OpenTms.Platform.Features;

/* The app and navigation features (docs/designs/tenants-and-editions.md, "App features"). Defaults (PO 2026-10-14):
 * screens that were live without a flag are on (Administration and its nodes; the Master data app and its groups),
 * screens that were behind a flag stay off (the Master data lists, Maintenance), placeholder apps are off. Every
 * node is a toggle and a child of the node above it, so an edition's Features tab shows the app tree. The Master data
 * app holds one group per domain (PO 2026-10-16), each domain group defaulting on like the groups below it; General
 * (Master Data), Orders, Planning & Execution and Financial (designs 41–53) and the Platform group (design 54). */
public class PlatformFeatureDefinitionProvider : FeatureDefinitionProvider
{
    public override void Define(IFeatureDefinitionContext context)
    {
        var apps = context.AddGroup(AppFeatures.GroupName, L("Feature:Apps"));

        Off(apps, AppFeatures.Dashboard);
        Off(apps, AppFeatures.Planboard);
        Off(apps, AppFeatures.Transport);
        Off(apps, AppFeatures.Finance);

        var masterData = On(apps, AppFeatures.MasterData.Default);
        var general = On(masterData, AppFeatures.MasterData.General.Default);
        var relations = On(general, AppFeatures.MasterData.General.Relations.Default);
        Off(relations, AppFeatures.MasterData.General.Relations.RelationsList);
        Off(relations, AppFeatures.MasterData.General.Relations.AddressTypes);
        Off(relations, AppFeatures.MasterData.General.Relations.RelationCategories);
        var transport = On(general, AppFeatures.MasterData.General.Transport.Default);
        Off(transport, AppFeatures.MasterData.General.Transport.Products);
        Off(transport, AppFeatures.MasterData.General.Transport.TransportUnits);
        Off(transport, AppFeatures.MasterData.General.Transport.ProductRelationCodes);
        Off(transport, AppFeatures.MasterData.General.Transport.Temperatures);
        var fleet = On(general, AppFeatures.MasterData.General.Fleet.Default);
        Off(fleet, AppFeatures.MasterData.General.Fleet.Vehicles);
        Off(fleet, AppFeatures.MasterData.General.Fleet.VehicleCombinations);
        Off(fleet, AppFeatures.MasterData.General.Fleet.Drivers);
        Off(fleet, AppFeatures.MasterData.General.Fleet.VehicleTypes);
        Off(fleet, AppFeatures.MasterData.General.Fleet.TransportCharacteristics);
        var reference = On(general, AppFeatures.MasterData.General.Reference.Default);
        Off(reference, AppFeatures.MasterData.General.Reference.Countries);
        Off(reference, AppFeatures.MasterData.General.Reference.Currencies);
        Off(reference, AppFeatures.MasterData.General.Reference.GeographicalRegions);

        /* The other domains' groups (designs 51–53): the groups default on, the lists off. */
        var orders = On(masterData, AppFeatures.MasterData.Orders.Default);
        var transportOrders = On(orders, AppFeatures.MasterData.Orders.TransportOrders.Default);
        Off(transportOrders, AppFeatures.MasterData.Orders.TransportOrders.TransportOrderTypes);
        Off(transportOrders, AppFeatures.MasterData.Orders.TransportOrders.WaypointTypes);
        Off(transportOrders, AppFeatures.MasterData.Orders.TransportOrders.TimeSlots);
        var planning = On(masterData, AppFeatures.MasterData.PlanningExecution.Default);
        var trips = On(planning, AppFeatures.MasterData.PlanningExecution.Trips.Default);
        Off(trips, AppFeatures.MasterData.PlanningExecution.Trips.TransportTripTypes);
        var financial = On(masterData, AppFeatures.MasterData.Financial.Default);
        var invoicing = On(financial, AppFeatures.MasterData.Financial.Invoicing.Default);
        Off(invoicing, AppFeatures.MasterData.Financial.Invoicing.VatPercentages);
        Off(invoicing, AppFeatures.MasterData.Financial.Invoicing.OutboundInvoiceTypes);
        Off(invoicing, AppFeatures.MasterData.Financial.Invoicing.InboundInvoiceTypes);
        var pricing = On(financial, AppFeatures.MasterData.Financial.Pricing.Default);
        Off(pricing, AppFeatures.MasterData.Financial.Pricing.AdditionalServices);
        Off(pricing, AppFeatures.MasterData.Financial.Pricing.DiscountsOrSurcharges);
        var platform = On(masterData, AppFeatures.MasterData.Platform.Default);
        Off(platform, AppFeatures.MasterData.Platform.EmailTemplates);
        Off(platform, AppFeatures.MasterData.Platform.ReportDefinitions);
        Off(platform, AppFeatures.MasterData.Platform.DocumentTypes);

        Off(apps, AppFeatures.Integrations);
        Off(apps, AppFeatures.Reports);

        var administration = On(apps, AppFeatures.Administration.Default);
        On(administration, AppFeatures.Administration.Users);
        On(administration, AppFeatures.Administration.Roles);
        On(administration, AppFeatures.Administration.OrganizationUnits);
        var auditLogs = On(administration, AppFeatures.Administration.AuditLogs.Default);
        On(auditLogs, AppFeatures.Administration.AuditLogs.FunctionalLogbook);
        On(auditLogs, AppFeatures.Administration.AuditLogs.TechnicalLogbook);
        Off(administration, AppFeatures.Administration.Maintenance);

        /* Behaviour features, outside the app tree. Document rendering (design 50) is off by default: switched on per
         * tenant or edition once the deployment holds the provider's key and secret. */
        var platformGroup = context.AddGroup(PlatformFeatures.GroupName, L("Feature:" + PlatformFeatures.GroupName));
        Off(platformGroup, PlatformFeatures.DocumentRendering);
    }

    private static FeatureDefinition On(FeatureGroupDefinition group, string name) => Add(group, name, "true");

    private static FeatureDefinition Off(FeatureGroupDefinition group, string name) => Add(group, name, "false");

    private static FeatureDefinition On(FeatureDefinition parent, string name) => Add(parent, name, "true");

    private static FeatureDefinition Off(FeatureDefinition parent, string name) => Add(parent, name, "false");

    private static FeatureDefinition Add(FeatureGroupDefinition group, string name, string defaultValue) =>
        group.AddFeature(name, defaultValue, L("Feature:" + name), L("Feature:" + name + ".Description"), new ToggleStringValueType());

    private static FeatureDefinition Add(FeatureDefinition parent, string name, string defaultValue) =>
        parent.CreateChild(name, defaultValue, L("Feature:" + name), L("Feature:" + name + ".Description"), new ToggleStringValueType());

    private static LocalizableString L(string name) => LocalizableString.Create<PlatformResource>(name);
}
