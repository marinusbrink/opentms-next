using System;
using System.Threading.Tasks;
using OpenTms.Platform.Grid;
using Volo.Abp;
using Volo.Abp.Application.Services;

namespace OpenTms.Platform.Administration.Logbook;

/// <summary>
/// Read-only access to the two logbooks ABP's audit logging keeps per tenant:
/// the <b>technical</b> logbook (requests: <c>AuditLog</c>) and the <b>functional</b> logbook
/// (entity changes: <c>EntityChange</c>). Exposed only through LogbookAdministrationController.
/// </summary>
[RemoteService(IsEnabled = false)]
public interface ILogbookAppService : IApplicationService
{
    /// <summary>A page of the technical logbook, newest first unless the request sorts.</summary>
    Task<GridResponse<TechnicalLogbookRowDto>> GetTechnicalListAsync(GridRequest request, TechnicalLogbookFilterDto filter);

    /// <summary>One technical entry with its actions and entity changes.</summary>
    Task<TechnicalLogbookDetailDto> GetTechnicalAsync(Guid id);

    /// <summary>A page of the functional logbook, newest first unless the request sorts.</summary>
    Task<GridResponse<FunctionalLogbookRowDto>> GetFunctionalListAsync(GridRequest request, FunctionalLogbookFilterDto filter);

    /// <summary>One functional entry with its property changes.</summary>
    Task<FunctionalLogbookDetailDto> GetFunctionalAsync(Guid id);
}
