using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace OpenTms.Integrations.OrderIntake;

/// <summary>
/// The order intake's tenant settings (design 72 build note 25): <c>GET</c> and <c>PUT /api/integrations/order-intake-settings</c>,
/// permission <c>Integrations.OrderIntakeSettings</c>, feature <c>Integrations.OrderIntake</c>; tenant side only. Shown in the
/// Integrations app under Order intake → Settings.
/// </summary>
public interface IOrderIntakeSettingsAppService : IApplicationService
{
    Task<OrderIntakeSettingsDto> GetAsync();

    Task<OrderIntakeSettingsDto> UpdateAsync(OrderIntakeSettingsDto input);
}

public class OrderIntakeSettingsDto
{
    /// <summary>
    /// The name similarity (pg_trgm similarity, 0.5–0.95, two decimals; default 0.75) a received name needs to match an
    /// existing customer, address, transport unit, product or temperature despite typing errors.
    /// </summary>
    [Range(0.5, 0.95)]
    public decimal NameSimilarityThreshold { get; set; } = OrderIntakeConsts.DefaultNameSimilarityThreshold;
}
