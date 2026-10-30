using System.Threading.Tasks;

namespace OpenTms.Platform.TestData;

/// <summary>
/// One module's part of "Reset to dataset" (docs/designs/65-tenant-test-datasets.md). Every module that owns
/// business data implements one, like an <c>IDataSeedContributor</c>; Platform only orchestrates and never knows
/// the module's records. The job runs in the tenant's context: wipe in descending <see cref="Order"/>, then the
/// normal seed, then generate in ascending <see cref="Order"/>. A contributor reads what an earlier one generated
/// from <see cref="TestDatasetContext.Get{T}"/> (typed outputs in the publishing module's <c>Domain.Shared</c>).
/// </summary>
public interface ITestDatasetContributor
{
    /// <summary>Generation order: Platform 0, Master Data 100, Financial 200, Orders 300, Planning &amp; Execution 400.</summary>
    int Order { get; }

    /// <summary>
    /// Hard-deletes the module's business data of the current tenant, soft-deleted rows included; the tenant
    /// filter stays on (constitution rule 1). Keeps what the task keeps: users, settings, number-sequence
    /// definitions, ISO reference data. Whatever the module's seed contributor writes may go: the seed runs after.
    /// </summary>
    Task WipeAsync(TestDatasetContext context);

    /// <summary>Generates the module's records for <see cref="TestDatasetContext.Profile"/>.</summary>
    Task GenerateAsync(TestDatasetContext context);
}
