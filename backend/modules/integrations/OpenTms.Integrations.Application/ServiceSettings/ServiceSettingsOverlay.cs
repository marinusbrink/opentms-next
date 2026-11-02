using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using OpenTms.Integrations.DocumentRendering.PdfGeneratorApi;
using OpenTms.Integrations.Routing;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Threading;
using Volo.Abp.SettingManagement;
using Volo.Abp.Settings;
using Volo.Abp.Uow;

namespace OpenTms.Integrations.ServiceSettings;

/// <summary>
/// The host's service settings laid over the deployment's configuration (docs/designs/maintenance-view.md, "Services").
/// The adapters keep reading <c>IOptions&lt;PdfGeneratorApiOptions&gt;</c> and <c>IOptions&lt;RoutingIntegrationOptions&gt;</c>;
/// the module resolves those per use from the options monitor, whose value is the configuration with every stored setting
/// on top (<see cref="ServiceSettingsPostConfigure"/>). This singleton holds the stored values: read at start, again after
/// every save on this instance, and every <see cref="RefreshPeriod"/> on the others (like ABP's own setting cache, a save
/// reaches another instance within that period). Each refresh renews the change token, so the monitor recomputes.
/// </summary>
public class ServiceSettingsOverlay
{
    public static readonly TimeSpan RefreshPeriod = TimeSpan.FromSeconds(60);

    private readonly IServiceScopeFactory _scopes;
    private IReadOnlyDictionary<string, string> _values = new Dictionary<string, string>();
    private CancellationTokenSource _changed = new();
    private readonly object _apply = new();
    private long _started;
    private long _applied;

    public ServiceSettingsOverlay(IServiceScopeFactory scopes)
    {
        _scopes = scopes;
    }

    /// <summary>The stored value, or null when the configuration decides.</summary>
    public string? Get(string name) => _values.TryGetValue(name, out var value) ? value : null;

    /// <summary>Fires when the stored values changed.</summary>
    public IChangeToken ChangeToken => new CancellationChangeToken(_changed.Token);

    /// <summary>Reads every service setting from the host database, in the host's context, in a unit of work of its own.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        /* A read that started earlier never overwrites a later one (the worker's read can finish after a save's). */
        var ticket = Interlocked.Increment(ref _started);
        var values = new Dictionary<string, string>();
        using (var scope = _scopes.CreateScope())
        {
            var currentTenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
            var unitOfWorkManager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var repository = scope.ServiceProvider.GetRequiredService<ISettingRepository>();
            var definitions = scope.ServiceProvider.GetRequiredService<ISettingDefinitionManager>();
            var encryption = scope.ServiceProvider.GetRequiredService<ISettingEncryptionService>();
            using (currentTenant.Change(null))
            using (var unitOfWork = unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(), requiresNew: true))
            {
                /* The stored rows themselves, not ISettingManager: its cache is per instance and slides, so a save on
                 * another instance would never arrive while this one keeps reading it. */
                var rows = await repository.GetListAsync(GlobalSettingValueProvider.ProviderName, null, cancellationToken);
                foreach (var row in rows.Where(r => ServiceSettingNames.All.Contains(r.Name) && !string.IsNullOrWhiteSpace(r.Value)))
                {
                    var definition = await definitions.GetAsync(row.Name);
                    var value = definition.IsEncrypted ? encryption.Decrypt(definition, row.Value) : row.Value;
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        values[row.Name] = value;
                    }
                }

                await unitOfWork.CompleteAsync(cancellationToken);
            }
        }

        CancellationTokenSource previous;
        lock (_apply)
        {
            if (ticket < _applied || SameAs(values))
            {
                _applied = Math.Max(_applied, ticket);
                return;
            }

            _applied = ticket;
            _values = values;
            previous = _changed;
            _changed = new CancellationTokenSource();
        }

        /* Outside the lock: cancelling runs the options monitor's callbacks. */
        previous.Cancel();
        previous.Dispose();
    }

    private bool SameAs(IReadOnlyDictionary<string, string> values)
    {
        if (values.Count != _values.Count)
        {
            return false;
        }

        foreach (var (key, value) in values)
        {
            if (!_values.TryGetValue(key, out var current) || current != value)
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>Lays the stored service settings over the configuration; an absent setting keeps the configured value.</summary>
public class ServiceSettingsPostConfigure : IPostConfigureOptions<PdfGeneratorApiOptions>, IPostConfigureOptions<RoutingIntegrationOptions>
{
    private readonly ServiceSettingsOverlay _overlay;

    public ServiceSettingsPostConfigure(ServiceSettingsOverlay overlay)
    {
        _overlay = overlay;
    }

    public void PostConfigure(string? name, PdfGeneratorApiOptions options)
    {
        options.BaseUrl = _overlay.Get(ServiceSettingNames.PdfGeneratorApi.BaseUrl) ?? options.BaseUrl;
        options.Key = _overlay.Get(ServiceSettingNames.PdfGeneratorApi.Key) ?? options.Key;
        options.Secret = _overlay.Get(ServiceSettingNames.PdfGeneratorApi.Secret) ?? options.Secret;
    }

    public void PostConfigure(string? name, RoutingIntegrationOptions options)
    {
        options.Provider = _overlay.Get(ServiceSettingNames.Routing.Provider) ?? options.Provider;
        options.Osrm.BaseUrl = _overlay.Get(ServiceSettingNames.Routing.OsrmBaseUrl) ?? options.Osrm.BaseUrl;
        options.RouteFunction.BaseUrl = _overlay.Get(ServiceSettingNames.Routing.RouteFunctionBaseUrl) ?? options.RouteFunction.BaseUrl;
        options.RouteFunction.ApiKey = _overlay.Get(ServiceSettingNames.Routing.RouteFunctionApiKey) ?? options.RouteFunction.ApiKey;
        options.Vroom.BaseUrl = _overlay.Get(ServiceSettingNames.Routing.VroomBaseUrl) ?? options.Vroom.BaseUrl;
    }
}

/// <summary>Tells the options monitor that the stored service settings changed.</summary>
public class ServiceSettingsChangeTokenSource<TOptions> : IOptionsChangeTokenSource<TOptions>
{
    private readonly ServiceSettingsOverlay _overlay;

    public ServiceSettingsChangeTokenSource(ServiceSettingsOverlay overlay)
    {
        _overlay = overlay;
    }

    public string Name => Options.DefaultName;

    public IChangeToken GetChangeToken() => _overlay.ChangeToken;
}

/// <summary>Reads the stored service settings at start and every <see cref="ServiceSettingsOverlay.RefreshPeriod"/>.</summary>
public class ServiceSettingsRefreshWorker : AsyncPeriodicBackgroundWorkerBase, ISingletonDependency
{
    public ServiceSettingsRefreshWorker(AbpAsyncTimer timer, IServiceScopeFactory serviceScopeFactory)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = (int)ServiceSettingsOverlay.RefreshPeriod.TotalMilliseconds;
        Timer.RunOnStart = true;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        try
        {
            await workerContext.ServiceProvider.GetRequiredService<ServiceSettingsOverlay>().RefreshAsync(workerContext.CancellationToken);
        }
        catch (OperationCanceledException) when (workerContext.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            /* While the database is away the configuration keeps deciding; the next period retries. */
            Logger.LogWarning(e, "The service settings could not be read; the configuration decides until the next period.");
        }
    }
}
