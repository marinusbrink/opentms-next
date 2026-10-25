using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace OpenTms.Platform.RecordUpdates;

/// <summary>Delivers a <see cref="RecordUpdate"/> to the browsers watching it; the API host implements it over SignalR.</summary>
public interface IRecordUpdateTransport
{
    Task SendAsync(RecordUpdate update);
}

/// <summary>The default where no hub runs (the DbMigrator, tests): nothing to deliver to.</summary>
public class NullRecordUpdateTransport : IRecordUpdateTransport, ISingletonDependency
{
    public Task SendAsync(RecordUpdate update) => Task.CompletedTask;
}
