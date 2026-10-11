using OpenTms.Platform.Auditing;
using Shouldly;
using Volo.Abp.Auditing;
using Volo.Abp.AuditLogging;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.BlobStoring.Database;
using Volo.Abp.EntityFrameworkCore.DistributedEvents;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Identity;
using Volo.Abp.OpenIddict.Tokens;
using Volo.Abp.PermissionManagement;
using Volo.Abp.SettingManagement;
using Volo.Abp.TenantManagement;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Platform.Auditing;

/// <summary>
/// The functional logbook is on by default (PO decision 2026-10-10): business entities are
/// recorded, infrastructure tables are not. Risk class: High — a wrong selector either hides
/// changes administrators rely on or floods the logbook with machine traffic.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class FunctionalLogbookEntitySelectorTests : OpenTmsEntityFrameworkCoreTestBase
{
    [Theory]
    [InlineData(typeof(IdentityUser))]
    [InlineData(typeof(IdentityRole))]
    [InlineData(typeof(OrganizationUnit))]
    [InlineData(typeof(PermissionGrant))]
    [InlineData(typeof(FeatureValue))]
    [InlineData(typeof(Tenant))]
    public void Business_entities_are_recorded(System.Type entityType)
    {
        FunctionalLogbookEntitySelector.IsSelected(entityType).ShouldBeTrue();
    }

    [Theory]
    [InlineData(typeof(OpenIddictToken))]
    [InlineData(typeof(Setting))]
    [InlineData(typeof(BackgroundJobRecord))]
    [InlineData(typeof(DatabaseBlob))]
    [InlineData(typeof(OutgoingEventRecord))]
    [InlineData(typeof(IncomingEventRecord))]
    [InlineData(typeof(IdentitySecurityLog))]
    [InlineData(typeof(IdentitySession))]
    [InlineData(typeof(AuditLog))]
    [InlineData(typeof(EntityChange))]
    public void Infrastructure_entities_are_not_recorded(System.Type entityType)
    {
        FunctionalLogbookEntitySelector.IsSelected(entityType).ShouldBeFalse();
    }

    [Fact]
    public void Non_entities_are_never_selected()
    {
        FunctionalLogbookEntitySelector.IsSelected(typeof(string)).ShouldBeFalse();
    }

    [Fact]
    public void The_selector_is_wired_into_ABP_so_entity_history_is_on_by_default()
    {
        var helper = GetRequiredService<IAuditingHelper>();

        helper.IsEntityHistoryEnabled(typeof(IdentityUser)).ShouldBeTrue();
        helper.IsEntityHistoryEnabled(typeof(OrganizationUnit)).ShouldBeTrue();
        helper.IsEntityHistoryEnabled(typeof(OpenIddictToken)).ShouldBeFalse();
        helper.IsEntityHistoryEnabled(typeof(AuditLog)).ShouldBeFalse();
    }
}
