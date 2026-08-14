using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.Application.Services;

namespace OpenTms.Platform.Grid;

[RemoteService(IsEnabled = false)]
public interface IGridSettingsAppService : IApplicationService
{
    Task<GridSettingsDto?> GetAsync(string gridId);
    Task SetAsync(string gridId, GridSettingsDto input);
    Task ResetAsync(string gridId);
}
