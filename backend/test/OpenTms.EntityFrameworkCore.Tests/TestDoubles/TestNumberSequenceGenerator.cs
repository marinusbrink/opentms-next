using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using OpenTms.Platform.NumberSequences;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace OpenTms.TestDoubles;

/// <summary>
/// On SQLite the counters live in the test database (one per test, so numbering starts at 1 every time) but the
/// transaction rule is skipped; on PostgreSQL the real generator runs (gapless, locked, transactional).
/// </summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(INumberSequenceGenerator))]
public class TestNumberSequenceGenerator : NumberSequenceGenerator
{
    private readonly INumberSequenceCounterStore _store;
    private readonly bool _sqlite;

    public TestNumberSequenceGenerator(INumberSequenceCounterStore store, IUnitOfWorkManager unitOfWorkManager, IClock clock, IOptions<NumberSequenceOptions> options, IOptions<AbpDbConnectionOptions> connections)
        : base(store, unitOfWorkManager, clock, options)
    {
        _store = store;
        _sqlite = TestDatabase.IsSqlite(connections);
    }

    public override async Task<string> NextAsync(string sequenceKey, NumberSequenceFormat format)
    {
        if (!_sqlite)
        {
            return await base.NextAsync(sequenceKey, format);
        }

        var today = LocalToday();
        return format.Format(await _store.IncrementAsync(sequenceKey, format.Period(today)), today);
    }
}
