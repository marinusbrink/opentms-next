using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace OpenTms.Platform.TestData;

/// <summary>
/// Bulk writes for the test-dataset contributors. Every batch commits in a unit of work of its own, so memory
/// stays flat for 150,000 relations; a failed run leaves part of a dataset and the next reset wipes it first.
/// </summary>
public class TestDatasetWriter : ITransientDependency
{
    public const int DefaultBatchSize = 1_000;

    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IDataFilter _dataFilter;

    public TestDatasetWriter(IUnitOfWorkManager unitOfWorkManager, IDataFilter dataFilter)
    {
        _unitOfWorkManager = unitOfWorkManager;
        _dataFilter = dataFilter;
    }

    /// <summary>Inserts the entities (children included) in batches; returns how many.</summary>
    public async Task<int> InsertAsync<TEntity>(
        IRepository<TEntity> repository, IEnumerable<TEntity> entities, int batchSize = DefaultBatchSize)
        where TEntity : class, IEntity
    {
        var count = 0;
        var batch = new List<TEntity>(batchSize);
        foreach (var entity in entities)
        {
            batch.Add(entity);
            if (batch.Count == batchSize)
            {
                count += await SaveAsync(repository, batch);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            count += await SaveAsync(repository, batch);
        }

        return count;
    }

    /// <summary>
    /// Hard-deletes every row of the current tenant in one statement, soft-deleted rows included. Only the
    /// soft-delete filter is off; the tenant filter stays on (constitution rule 1), so a shared-tier tenant in
    /// the host database loses only its own rows. Child tables go with the parent through their cascading
    /// foreign key.
    /// </summary>
    public async Task DeleteAllAsync<TEntity>(IRepository<TEntity> repository)
        where TEntity : class, IEntity
    {
        using (_dataFilter.Disable<ISoftDelete>())
        using (var unitOfWork = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            await repository.DeleteDirectAsync(_ => true);
            await unitOfWork.CompleteAsync();
        }
    }

    private async Task<int> SaveAsync<TEntity>(IRepository<TEntity> repository, List<TEntity> batch)
        where TEntity : class, IEntity
    {
        using (var unitOfWork = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            await repository.InsertManyAsync(batch);
            await unitOfWork.CompleteAsync();
        }

        return batch.Count;
    }
}
