using System.Threading.Tasks;
using OpenTms.Platform.TestData;
using OpenTms.Reporting.Facts;
using Volo.Abp.Auditing;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Uow;

namespace OpenTms.Reporting.TestData;

/// <summary>
/// Reporting's part of "Reset to dataset" (docs/designs/65-tenant-test-datasets.md; design 75): the facts and the backfill's
/// settings go, so a reset tenant holds no stale numbers. It generates nothing: the generators raise no events, so the facts of
/// the new data come from a new backfill. Generate runs last (Order 500) and clears once more, because a pass of the backfill
/// worker may have started while the other modules were still generating.
/// </summary>
[DisableAuditing]
public class ReportingTestDatasetContributor : ITestDatasetContributor, ITransientDependency
{
    private readonly ReportingFactState _state;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public ReportingTestDatasetContributor(ReportingFactState state, IUnitOfWorkManager unitOfWorkManager)
    {
        _state = state;
        _unitOfWorkManager = unitOfWorkManager;
    }

    public int Order => 500;

    public Task WipeAsync(TestDatasetContext context) => ClearAsync();

    public Task GenerateAsync(TestDatasetContext context) => ClearAsync();

    private async Task ClearAsync()
    {
        using var unitOfWork = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        await _state.ClearFactsAsync();
        await _state.ClearBackfillAsync();
        await unitOfWork.CompleteAsync();
    }
}
