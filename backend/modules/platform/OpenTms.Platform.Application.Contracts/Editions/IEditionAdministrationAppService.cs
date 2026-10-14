using System;
using System.Threading.Tasks;
using OpenTms.Platform.Grid;
using Volo.Abp.Application.Services;

namespace OpenTms.Platform.Editions;

/// <summary>
/// The Editions app (host only, docs/designs/tenants-and-editions.md). Feature values of an edition are edited
/// through ABP's feature-management endpoint with provider <see cref="EditionConsts.FeatureProviderName"/>.
/// </summary>
public interface IEditionAdministrationAppService : IApplicationService
{
    Task<GridResponse<EditionRowDto>> GetListAsync(GridRequest request);

    Task<EditionRowDto> GetAsync(Guid id);

    Task<EditionRowDto> CreateAsync(EditionCreateUpdateDto input);

    Task<EditionRowDto> UpdateAsync(Guid id, EditionCreateUpdateDto input);

    Task DeleteAsync(Guid id);
}
