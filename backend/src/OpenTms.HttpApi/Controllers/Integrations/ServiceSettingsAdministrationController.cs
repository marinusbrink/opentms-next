using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenTms.Integrations.Permissions;
using OpenTms.Integrations.ServiceSettings;
using OpenTms.Platform.Administration.Maintenance;

namespace OpenTms.Controllers.Integrations;

/// <summary>
/// Administration → Services (docs/designs/maintenance-view.md, "Services"): one configuration of pdfgeneratorapi and
/// routing for every tenant, and a connection test per service. Host only: the permissions are host-side and the service
/// guards the side again.
/// </summary>
[ApiController]
[Route("api/integrations/administration/service-settings")]
[Authorize(IntegrationsPermissions.ServiceSettings.Default)]
public class ServiceSettingsAdministrationController : OpenTmsController
{
    private readonly IServiceSettingsAppService _settings;

    public ServiceSettingsAdministrationController(IServiceSettingsAppService settings)
    {
        _settings = settings;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ServiceSettingsDto), 200)]
    public async Task<ActionResult<ServiceSettingsDto>> GetAsync() => Ok(await _settings.GetAsync());

    [HttpPut]
    [Authorize(IntegrationsPermissions.ServiceSettings.Update)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> UpdateAsync([FromBody] UpdateServiceSettingsDto input)
    {
        await _settings.UpdateAsync(input);
        return NoContent();
    }

    [HttpPost("test/{service}")]
    [Authorize(IntegrationsPermissions.ServiceSettings.Test)]
    [ProducesResponseType(typeof(List<HealthCheckEntryDto>), 200)]
    public async Task<ActionResult<List<HealthCheckEntryDto>>> TestAsync(string service) => Ok(await _settings.TestAsync(service));
}
