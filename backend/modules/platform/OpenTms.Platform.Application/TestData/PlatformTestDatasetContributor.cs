using System;
using System.Threading.Tasks;
using OpenTms.Platform.DerivedValues;
using OpenTms.Platform.NumberSequences;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace OpenTms.Platform.TestData;

/// <summary>
/// Platform's part of "Reset to dataset": the number-sequence counters start over (the definitions stay) and the
/// derived-value states of the wiped records go. Platform generates nothing itself.
/// </summary>
public class PlatformTestDatasetContributor : ITestDatasetContributor, ITransientDependency
{
    private readonly TestDatasetWriter _writer;
    private readonly IRepository<NumberSequenceCounter, Guid> _counters;
    private readonly IRepository<DerivedValueState, Guid> _derivedValueStates;

    public PlatformTestDatasetContributor(
        TestDatasetWriter writer,
        IRepository<NumberSequenceCounter, Guid> counters,
        IRepository<DerivedValueState, Guid> derivedValueStates)
    {
        _writer = writer;
        _counters = counters;
        _derivedValueStates = derivedValueStates;
    }

    public int Order => 0;

    public async Task WipeAsync(TestDatasetContext context)
    {
        await _writer.DeleteAllAsync(_derivedValueStates);
        await _writer.DeleteAllAsync(_counters);
    }

    public Task GenerateAsync(TestDatasetContext context) => Task.CompletedTask;
}
