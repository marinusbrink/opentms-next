using OpenTms.Platform.Localization;
using Volo.Abp.Features;
using Volo.Abp.Localization;
using Volo.Abp.Validation.StringValues;

namespace OpenTms.Platform.Features;

/* The app and navigation features (docs/designs/tenants-and-editions.md, "App features"). Defaults (PO 2026-10-14):
 * screens that were live without a flag are on (Administration and its nodes; the Master data app and its groups),
 * screens that were behind a flag stay off (the Master data lists, Maintenance), placeholder apps are off. Every
 * node is a toggle and a child of the node above it, so an edition's Features tab shows the app tree. */
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
        var relations = On(masterData, AppFeatures.MasterData.Relations.Default);
        Off(relations, AppFeatures.MasterData.Relations.RelationsList);
        Off(relations, AppFeatures.MasterData.Relations.AddressTypes);
        Off(relations, AppFeatures.MasterData.Relations.RelationCategories);
        var transport = On(masterData, AppFeatures.MasterData.Transport.Default);
        Off(transport, AppFeatures.MasterData.Transport.Products);
        Off(transport, AppFeatures.MasterData.Transport.TransportUnits);
        Off(transport, AppFeatures.MasterData.Transport.Temperatures);
        var fleet = On(masterData, AppFeatures.MasterData.Fleet.Default);
        Off(fleet, AppFeatures.MasterData.Fleet.Drivers);
        Off(fleet, AppFeatures.MasterData.Fleet.VehicleTypes);
        Off(fleet, AppFeatures.MasterData.Fleet.TransportCharacteristics);
        var reference = On(masterData, AppFeatures.MasterData.Reference.Default);
        Off(reference, AppFeatures.MasterData.Reference.Countries);
        Off(reference, AppFeatures.MasterData.Reference.Currencies);
        Off(reference, AppFeatures.MasterData.Reference.GeographicalRegions);

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
