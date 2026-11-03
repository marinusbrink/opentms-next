using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using OpenTms.Integrations.Features;
using OpenTms.Integrations.Permissions;
using OpenTms.Integrations.Settings;
using OpenTms.Platform.Features;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;
using Volo.Abp.Features;
using Volo.Abp.SettingManagement;
using Volo.Abp.Settings;

namespace OpenTms.Integrations.OrderIntake;

/// <summary>
/// See <see cref="IOrderIntakeSettingsAppService"/> (design 72 build note 25). Reads and writes the current tenant's
/// setting <see cref="IntegrationsSettings.NameSimilarityThreshold"/>; the pipeline reads the same value for every intake
/// (<see cref="OrderIntakeSettingValues"/>). Behind the order intake feature plus the app features of the Integrations
/// app's node Order intake → Settings.
/// </summary>
[RequiresFeature(
    IntegrationsFeatures.OrderIntake,
    AppFeatures.Integrations,
    AppFeatures.IntegrationsApp.Intake.Default,
    AppFeatures.IntegrationsApp.Intake.Settings,
    RequiresAll = true)]
[Authorize(IntegrationsPermissions.OrderIntakeSettings)]
public class OrderIntakeSettingsAppService : IntegrationsAppServiceBase, IOrderIntakeSettingsAppService
{
    private readonly ISettingManager _settingManager;

    public OrderIntakeSettingsAppService(ISettingManager settingManager)
    {
        _settingManager = settingManager;
    }

    public virtual async Task<OrderIntakeSettingsDto> GetAsync()
        => new() { NameSimilarityThreshold = await OrderIntakeSettingValues.NameSimilarityThresholdAsync(SettingProvider) };

    public virtual async Task<OrderIntakeSettingsDto> UpdateAsync(OrderIntakeSettingsDto input)
    {
        Check.NotNull(input, nameof(input));
        if (CurrentTenant.Id == null)
        {
            /* A tenant setting (tenant-side permission): the host has no order intake of its own. */
            throw new BusinessException(ReferenceDataErrorCodes.InvalidCode).WithData("field", "tenant").WithData("value", "host");
        }

        if (input.NameSimilarityThreshold is < OrderIntakeConsts.MinNameSimilarityThreshold or > OrderIntakeConsts.MaxNameSimilarityThreshold)
        {
            throw new BusinessException(ReferenceDataErrorCodes.InvalidCode).WithData("field", "nameSimilarityThreshold").WithData("value", input.NameSimilarityThreshold);
        }

        var value = Math.Round(input.NameSimilarityThreshold, 2, MidpointRounding.AwayFromZero);
        await _settingManager.SetForCurrentTenantAsync(IntegrationsSettings.NameSimilarityThreshold, value.ToString("0.00", CultureInfo.InvariantCulture));
        Logger.LogInformation("Order intake name similarity threshold of tenant {TenantId} set to {Threshold}.", CurrentTenant.Id, value);
        return new OrderIntakeSettingsDto { NameSimilarityThreshold = value };
    }
}

/// <summary>The order intake's tenant setting values, parsed and bounded (a stored value outside the range is clamped).</summary>
public static class OrderIntakeSettingValues
{
    public static async Task<decimal> NameSimilarityThresholdAsync(ISettingProvider settings)
    {
        var text = await settings.GetOrNullAsync(IntegrationsSettings.NameSimilarityThreshold);
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
        {
            value = OrderIntakeConsts.DefaultNameSimilarityThreshold;
        }

        return Math.Clamp(value, OrderIntakeConsts.MinNameSimilarityThreshold, OrderIntakeConsts.MaxNameSimilarityThreshold);
    }
}
