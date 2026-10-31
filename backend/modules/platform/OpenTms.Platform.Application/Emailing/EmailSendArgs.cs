using System;
using Volo.Abp.BackgroundJobs;

namespace OpenTms.Platform.Emailing;

/// <summary>Ids only: Hangfire stores job arguments in plain text (design 64, like design 49).</summary>
[BackgroundJobName("OpenTms.Platform.EmailSend")]
public class EmailSendArgs
{
    public Guid? TenantId { get; set; }

    public Guid EmailMessageId { get; set; }
}
