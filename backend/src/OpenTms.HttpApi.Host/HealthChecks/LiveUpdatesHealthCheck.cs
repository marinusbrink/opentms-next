using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTms.Platform.Maintenance;
using StackExchange.Redis;
using Volo.Abp.DependencyInjection;

namespace OpenTms.HealthChecks;

/// <summary>
/// Live record updates (design 49) reach a browser connected to another instance only through the Redis backplane
/// (<c>SignalR:Redis:Configuration</c>). Without it one instance is fine (optional) and more are Degraded: a screen
/// refreshes only when the job ran on its own instance. With it, the check pings Redis over one connection it keeps;
/// no answer is Degraded (screens stop refreshing, data stays right).
/// </summary>
public class LiveUpdatesHealthCheck : IHealthCheck, ITransientDependency
{
    public const string Name = "live-updates";

    public const string SettingName = "SignalR:Redis:Configuration";

    public const string NoBackplane = "NoBackplane";

    private static readonly string[] Settings = [SettingName];

    private readonly IConfiguration _configuration;
    private readonly IInstanceFleet _fleet;
    private readonly LiveUpdatesRedisConnection _connection;

    public LiveUpdatesHealthCheck(IConfiguration configuration, IInstanceFleet fleet, LiveUpdatesRedisConnection connection)
    {
        _configuration = configuration;
        _fleet = fleet;
        _connection = connection;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var configuration = _configuration[SettingName];
        if (string.IsNullOrWhiteSpace(configuration))
        {
            var running = (await _fleet.GetRunningAsync(cancellationToken))?.Count ?? 1;
            var extra = new Dictionary<string, object> { ["instances"] = running };
            return running > 1
                ? HealthCheckResult.Degraded(
                    $"live updates: no backplane while {running} instances run.",
                    data: HealthCheckFacts.Data(NoBackplane, Settings, extra: extra))
                : HealthCheckResult.Healthy(
                    "live updates: one instance, no backplane needed.",
                    HealthCheckFacts.Data(HealthCheckFacts.Reasons.NotConfigured, Settings, optional: true, extra: extra));
        }

        /* Only the endpoint before the first comma: the options after it may carry a password. */
        var target = configuration.Split(',', 2)[0].Trim();
        try
        {
            var redis = await _connection.ConnectAsync(configuration, cancellationToken);
            var latency = await redis.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy(
                $"live updates: Redis answered in {latency.TotalMilliseconds:0} ms.",
                HealthCheckFacts.Data(HealthCheckFacts.Reasons.Ok, Settings, target));
        }
        catch (Exception e) when (e is RedisException or TimeoutException)
        {
            return HealthCheckResult.Degraded(
                "live updates: Redis did not answer.", e, HealthCheckFacts.Data(HealthCheckFacts.Reasons.Unreachable, Settings, target));
        }
    }
}

/// <summary>The one Redis connection the live-updates check keeps between runs (a connect per run would be wasteful).</summary>
public class LiveUpdatesRedisConnection : ISingletonDependency, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ConnectionMultiplexer? _redis;

    public async Task<ConnectionMultiplexer> ConnectAsync(string configuration, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_redis is not { IsConnected: true })
            {
                _redis?.Dispose();
                var options = ConfigurationOptions.Parse(configuration);
                options.AbortOnConnectFail = true;
                options.ConnectTimeout = 5000;
                _redis = await ConnectionMultiplexer.ConnectAsync(options);
            }

            return _redis;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _redis?.Dispose();
        _gate.Dispose();
    }
}
