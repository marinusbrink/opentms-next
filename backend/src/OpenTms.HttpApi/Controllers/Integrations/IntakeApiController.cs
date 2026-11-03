using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OpenTms.Integrations.OrderIntake;
using OpenTms.Integrations.Permissions;
using Volo.Abp.Content;

namespace OpenTms.Controllers.Integrations;

/// <summary>
/// The intake API of design 72 for customer systems: OAuth client credentials (an API client's token carries its tenant
/// and client id), permission <c>Integrations.IntakeApi.Submit</c> (granted to API clients only), feature
/// <c>Integrations.OrderIntake</c> and the active-client check in the service. JSON orders are processed in the request;
/// documents are recorded and processed by the pipeline job (202).
/// </summary>
[ApiController]
[Route("api/integrations/intake")]
[Authorize(IntegrationsPermissions.IntakeApi.Submit)]
public class IntakeApiController : OpenTmsController
{
    /// <summary>The 25 MB document plus the multipart envelope and the externalId field.</summary>
    private const long MaxRequestBytes = OrderIntakeConsts.MaxDocumentBytes + 1024 * 1024;

    private readonly IIntakeApiAppService _intake;

    public IntakeApiController(IIntakeApiAppService intake)
    {
        _intake = intake;
    }

    [HttpPost("orders")]
    [ProducesResponseType(typeof(IntakeResultDto), 200)]
    public async Task<ActionResult<IntakeResultDto>> SubmitOrderAsync([FromBody] IntakeOrderRequestDto input)
        => Ok(await _intake.SubmitOrderAsync(input));

    [HttpPost("documents")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    [ProducesResponseType(typeof(IntakeResultDto), 202)]
    public async Task<ActionResult<IntakeResultDto>> SubmitDocumentAsync([FromForm] string externalId, IFormFile file)
    {
        var result = await _intake.SubmitDocumentAsync(
            externalId,
            new RemoteStreamContent(file.OpenReadStream(), file.FileName, file.ContentType, file.Length));
        return Accepted(result);
    }

    [HttpGet("messages/{externalId}")]
    [ProducesResponseType(typeof(IntakeResultDto), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<IntakeResultDto>> GetByExternalIdAsync(string externalId)
    {
        var result = await _intake.GetByExternalIdAsync(externalId);
        return result == null ? NotFound() : Ok(result);
    }
}
