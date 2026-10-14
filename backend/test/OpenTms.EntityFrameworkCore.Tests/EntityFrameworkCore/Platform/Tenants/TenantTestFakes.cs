using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using OpenTms.Platform.EntityFrameworkCore.Tenants;
using OpenTms.Platform.Tenants;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Data;
using Volo.Abp.Emailing;
using Volo.Abp.Modularity;

namespace OpenTms.EntityFrameworkCore.Platform.Tenants;

/// <summary>Records every queued job instead of handing it to Hangfire, so a test can assert what was queued and when.</summary>
public sealed class FakeBackgroundJobManager : IBackgroundJobManager
{
    private readonly object _gate = new();
    private readonly List<object> _jobs = new();

    public IReadOnlyList<object> Jobs
    {
        get
        {
            lock (_gate)
            {
                return _jobs.ToList();
            }
        }
    }

    public IReadOnlyList<T> JobsOf<T>() => Jobs.OfType<T>().ToList();

    public void Clear()
    {
        lock (_gate)
        {
            _jobs.Clear();
        }
    }

    public Task<string> EnqueueAsync<TArgs>(TArgs args, BackgroundJobPriority priority = BackgroundJobPriority.Normal, TimeSpan? delay = null)
    {
        lock (_gate)
        {
            _jobs.Add(args!);
            return Task.FromResult(_jobs.Count.ToString());
        }
    }
}

/// <summary>
/// The real <see cref="PostgreSqlTenantDatabaseProvisioner"/> for everything that does not touch a server (build,
/// mask, parse, host location), against a fixed host connection string; the server calls are recorded and can be
/// told to fail. The SQLite test database is not a PostgreSQL connection string, so the production registration
/// cannot be used there.
/// </summary>
public sealed class FakeTenantDatabaseProvisioner : ITenantDatabaseProvisioner
{
    public const string HostConnectionString = "Host=db.example.test;Port=5432;Database=OpenTms;Username=postgres;Password=host-secret";

    private readonly PostgreSqlTenantDatabaseProvisioner _real = Create(HostConnectionString);

    public List<string> Ensured { get; } = new();

    public List<TenantDatabaseLocation> Dropped { get; } = new();

    public Exception? EnsureFailure { get; set; }

    public static PostgreSqlTenantDatabaseProvisioner Create(string hostConnectionString, TenantDatabaseOptions? options = null)
    {
        var connectionOptions = new AbpDbConnectionOptions();
        connectionOptions.ConnectionStrings.Default = hostConnectionString;
        return new PostgreSqlTenantDatabaseProvisioner(Options.Create(options ?? new TenantDatabaseOptions()), Options.Create(connectionOptions));
    }

    public string BuildConnectionString(string databaseAndRoleName, string password) => _real.BuildConnectionString(databaseAndRoleName, password);

    public string Mask(string connectionString) => _real.Mask(connectionString);

    public TenantDatabaseLocation? Parse(string connectionString) => _real.Parse(connectionString);

    public TenantDatabaseLocation GetHostLocation() => _real.GetHostLocation();

    public Task EnsureDatabaseAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        Ensured.Add(connectionString);
        return EnsureFailure == null ? Task.CompletedTask : Task.FromException(EnsureFailure);
    }

    public Task DropDatabaseAsync(TenantDatabaseLocation location, CancellationToken cancellationToken = default)
    {
        Dropped.Add(location);
        return Task.CompletedTask;
    }
}

/// <summary>Captures sent mail instead of dropping it like the development <c>NullEmailSender</c>.</summary>
public sealed class CapturingEmailSender : IEmailSender
{
    public sealed record SentMail(string To, string Subject, string Body);

    public List<SentMail> Sent { get; } = new();

    public Task SendAsync(string to, string? subject, string? body, bool isBodyHtml = true, AdditionalEmailSendingArgs? additionalEmailSendingArgs = null)
    {
        Sent.Add(new SentMail(to, subject ?? string.Empty, body ?? string.Empty));
        return Task.CompletedTask;
    }

    public Task SendAsync(string from, string to, string? subject, string? body, bool isBodyHtml = true, AdditionalEmailSendingArgs? additionalEmailSendingArgs = null) =>
        SendAsync(to, subject, body, isBodyHtml, additionalEmailSendingArgs);

    public Task SendAsync(System.Net.Mail.MailMessage mail, bool normalize = true) =>
        SendAsync(mail.To.ToString(), mail.Subject, mail.Body, mail.IsBodyHtml);

    public Task QueueAsync(string to, string subject, string body, bool isBodyHtml = true, AdditionalEmailSendingArgs? additionalEmailSendingArgs = null) =>
        SendAsync(to, subject, body, isBodyHtml, additionalEmailSendingArgs);

    public Task QueueAsync(string from, string to, string subject, string body, bool isBodyHtml = true, AdditionalEmailSendingArgs? additionalEmailSendingArgs = null) =>
        SendAsync(to, subject, body, isBodyHtml, additionalEmailSendingArgs);
}

/// <summary>
/// The SQLite test application for the Tenants and Editions apps: the production wiring, with only the two ports
/// that reach outside the process replaced — the job queue (<see cref="FakeBackgroundJobManager"/>) and the
/// database server (<see cref="FakeTenantDatabaseProvisioner"/>).
/// </summary>
[DependsOn(typeof(OpenTmsEntityFrameworkCoreTestModule))]
public class TenantAdministrationTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var jobs = new FakeBackgroundJobManager();
        context.Services.AddSingleton(jobs);
        context.Services.Replace(ServiceDescriptor.Singleton<IBackgroundJobManager>(jobs));

        var provisioner = new FakeTenantDatabaseProvisioner();
        context.Services.AddSingleton(provisioner);
        context.Services.Replace(ServiceDescriptor.Transient<ITenantDatabaseProvisioner>(_ => provisioner));
    }
}
