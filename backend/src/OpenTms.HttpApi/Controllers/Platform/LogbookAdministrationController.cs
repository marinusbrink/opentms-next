using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenTms.Platform.Administration.Logbook;
using OpenTms.Platform.Grid;
using OpenTms.Features;
using OpenTms.Platform.Features;
using OpenTms.Platform.Permissions;

namespace OpenTms.Controllers.Platform;

/// <summary>
/// The two logbooks of the Administration app, read-only: the technical logbook (requests, ABP
/// audit logs) and the functional logbook (entity changes, ABP entity history).
/// </summary>
[ApiController]
[Route("api/platform/administration/logbook")]
public class LogbookAdministrationController : OpenTmsController
{
    private readonly ILogbookAppService _logbookAppService;

    public LogbookAdministrationController(ILogbookAppService logbookAppService)
    {
        _logbookAppService = logbookAppService;
    }

    [RequiresTenantAppFeature(AppFeatures.Administration.Default, AppFeatures.Administration.AuditLogs.Default, AppFeatures.Administration.AuditLogs.TechnicalLogbook)]
    /// <summary>A page of the technical logbook; newest first unless <paramref name="sortBy"/> is given.</summary>
    [HttpGet("technical")]
    [Authorize(PlatformPermissions.Administration.Logbook.Technical)]
    [ProducesResponseType(typeof(GridResponse<TechnicalLogbookRowDto>), 200)]
    public async Task<ActionResult<GridResponse<TechnicalLogbookRowDto>>> GetTechnicalListAsync(
        [FromQuery] int startRow = 0,
        [FromQuery] int endRow = 50,
        [FromQuery] string? wildcardSearch = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDir = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] string? userName = null,
        [FromQuery] string? httpMethod = null,
        [FromQuery] string? url = null,
        [FromQuery] int? httpStatusCode = null,
        [FromQuery] bool? hasException = null,
        [FromQuery] string? correlationId = null)
    {
        var request = BuildGridRequest(startRow, endRow, wildcardSearch, sortBy, sortDir);
        var filter = new TechnicalLogbookFilterDto
        {
            From = from,
            To = to,
            UserName = userName,
            HttpMethod = httpMethod,
            Url = url,
            HttpStatusCode = httpStatusCode,
            HasException = hasException,
            CorrelationId = correlationId
        };
        var result = await _logbookAppService.GetTechnicalListAsync(request, filter);
        return Ok(result);
    }

    [RequiresTenantAppFeature(AppFeatures.Administration.Default, AppFeatures.Administration.AuditLogs.Default, AppFeatures.Administration.AuditLogs.TechnicalLogbook)]
    [HttpGet("technical/{id:guid}")]
    [Authorize(PlatformPermissions.Administration.Logbook.Technical)]
    [ProducesResponseType(typeof(TechnicalLogbookDetailDto), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<TechnicalLogbookDetailDto>> GetTechnicalAsync(Guid id)
    {
        var result = await _logbookAppService.GetTechnicalAsync(id);
        return Ok(result);
    }

    [RequiresTenantAppFeature(AppFeatures.Administration.Default, AppFeatures.Administration.AuditLogs.Default, AppFeatures.Administration.AuditLogs.FunctionalLogbook)]
    /// <summary>A page of the functional logbook; newest first unless <paramref name="sortBy"/> is given.</summary>
    [HttpGet("functional")]
    [Authorize(PlatformPermissions.Administration.Logbook.Functional)]
    [ProducesResponseType(typeof(GridResponse<FunctionalLogbookRowDto>), 200)]
    public async Task<ActionResult<GridResponse<FunctionalLogbookRowDto>>> GetFunctionalListAsync(
        [FromQuery] int startRow = 0,
        [FromQuery] int endRow = 50,
        [FromQuery] string? wildcardSearch = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDir = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] string? changeType = null,
        [FromQuery] string? entityTypeFullName = null,
        [FromQuery] string? entityId = null,
        [FromQuery] string? userName = null)
    {
        var request = BuildGridRequest(startRow, endRow, wildcardSearch, sortBy, sortDir);
        var filter = new FunctionalLogbookFilterDto
        {
            From = from,
            To = to,
            ChangeType = changeType,
            EntityTypeFullName = entityTypeFullName,
            EntityId = entityId,
            UserName = userName
        };
        var result = await _logbookAppService.GetFunctionalListAsync(request, filter);
        return Ok(result);
    }

    [RequiresTenantAppFeature(AppFeatures.Administration.Default, AppFeatures.Administration.AuditLogs.Default, AppFeatures.Administration.AuditLogs.FunctionalLogbook)]
    [HttpGet("functional/{id:guid}")]
    [Authorize(PlatformPermissions.Administration.Logbook.Functional)]
    [ProducesResponseType(typeof(FunctionalLogbookDetailDto), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<FunctionalLogbookDetailDto>> GetFunctionalAsync(Guid id)
    {
        var result = await _logbookAppService.GetFunctionalAsync(id);
        return Ok(result);
    }

    private static GridRequest BuildGridRequest(int startRow, int endRow, string? wildcardSearch, string? sortBy, string? sortDir)
    {
        var sortModels = sortBy != null
            ? new List<SortModel> { new SortModel(sortBy, sortDir ?? "asc") }
            : new List<SortModel>();

        return new GridRequest
        {
            StartRow = startRow,
            EndRow = endRow,
            WildcardSearch = wildcardSearch,
            SortModels = sortModels
        };
    }
}
