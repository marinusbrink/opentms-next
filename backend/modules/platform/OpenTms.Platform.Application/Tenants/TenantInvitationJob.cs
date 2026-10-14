using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;

namespace OpenTms.Platform.Tenants;

/// <summary>"Resend invitation": a new set-password link for the tenant administrator, sent from a job, never in the request.</summary>
public class TenantInvitationJob : AsyncBackgroundJob<TenantInvitationArgs>, ITransientDependency
{
    private readonly TenantStateWriter _stateWriter;
    private readonly TenantInvitationSender _invitationSender;

    public TenantInvitationJob(TenantStateWriter stateWriter, TenantInvitationSender invitationSender)
    {
        _stateWriter = stateWriter;
        _invitationSender = invitationSender;
    }

    public override async Task ExecuteAsync(TenantInvitationArgs args)
    {
        var tenant = await _stateWriter.FindAsync(args.TenantId);
        if (tenant == null)
        {
            Logger.LogWarning("Tenant {TenantId} no longer exists; invitation skipped", args.TenantId);
            return;
        }

        /* Idempotent (rule 3): every "Resend invitation" carries its own request id, recorded together with the
         * sent mark once the mail went out, so a redelivery of a sent request sends nothing, while one that failed
         * may try again. */
        if (tenant.GetProperty<Guid?>(TenantProperties.InvitationRequestId) == args.RequestId)
        {
            Logger.LogInformation("Invitation request for tenant {TenantId} already handled; nothing sent", tenant.Id);
            return;
        }

        /* Not rethrown: a Hangfire retry after a sent mail whose mark failed would send it twice. A failure is
         * logged by exception type only (a mail server's message often contains the address, rule 5); the user
         * resends from the Tenants list. */
        try
        {
            await _invitationSender.SendAsync(tenant.Id, tenant.Name);
            await _stateWriter.MarkInvitationSentAsync(tenant.Id, args.RequestId);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(
                "The invitation for tenant {TenantId} could not be sent ({ExceptionType})", tenant.Id, ex.GetType().Name);
        }
    }
}
