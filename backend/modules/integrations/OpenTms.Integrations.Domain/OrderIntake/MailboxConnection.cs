using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Integrations.OrderIntake;

/// <summary>
/// A connected order mailbox (design 72 "Channels"): one Gmail label or Microsoft 365 folder of one account, read-only.
/// Created waiting for the provider's consent; the OAuth callback stores the account's address and the encrypted refresh
/// token (it never leaves the backend, never a DTO or a log line); choosing a folder and taking the provider's current
/// cursor activates it, so only mail arriving afterwards is imported. A refused refresh token (<c>invalid_grant</c>) puts
/// it in Reconnect: the cursor stays, nothing is lost. Unique per (tenant, provider, address, folder) among undeleted rows.
/// </summary>
public class MailboxConnection : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public MailProvider Provider { get; private set; }

    public string DisplayName { get; private set; } = default!;

    /// <summary>The account's address, from the provider after consent.</summary>
    public string? EmailAddress { get; private set; }

    public string? FolderId { get; private set; }

    /// <summary>The label or folder path as the provider shows it ("Inbox/Orders").</summary>
    public string? FolderName { get; private set; }

    /// <summary>Encrypted with <c>IStringEncryptionService</c>.</summary>
    public string? RefreshTokenEncrypted { get; private set; }

    public MailboxStatus Status { get; private set; }

    /// <summary>Gmail's historyId or Graph's delta link: where the next sync continues.</summary>
    public string? SyncCursor { get; private set; }

    public DateTime? ConnectedAt { get; private set; }

    public DateTime? LastSyncAt { get; private set; }

    public string? LastErrorCode { get; private set; }

    public DateTime? LastErrorAt { get; private set; }

    public Guid? DefaultCustomerRelationId { get; private set; }

    public Guid? DefaultTransportOrderTypeId { get; private set; }

    /// <summary>Orders from a trusted profile that resolve completely are confirmed at once.</summary>
    public bool AutoConfirm { get; private set; }

    protected MailboxConnection()
    {
    }

    public MailboxConnection(Guid id, Guid? tenantId, MailProvider provider, string displayName)
        : base(id)
    {
        TenantId = tenantId;
        Provider = provider;
        Rename(displayName);
        Status = MailboxStatus.AwaitingConsent;
    }

    /// <summary>Only an Active connection is synchronised.</summary>
    public bool IsSyncable => Status == MailboxStatus.Active;

    /// <summary>The OAuth callback accepts a connection only in these states.</summary>
    public bool AwaitsConsent => Status is MailboxStatus.AwaitingConsent or MailboxStatus.Reconnect;

    public void Rename(string displayName)
        => DisplayName = Check.NotNullOrWhiteSpace(displayName, nameof(displayName), OrderIntakeConsts.MaxDisplayNameLength).Trim();

    public void SetDefaults(Guid? defaultCustomerRelationId, Guid? defaultTransportOrderTypeId, bool autoConfirm)
    {
        DefaultCustomerRelationId = defaultCustomerRelationId;
        DefaultTransportOrderTypeId = defaultTransportOrderTypeId;
        AutoConfirm = autoConfirm;
    }

    /// <summary>
    /// The provider's consent arrived: the account's address and the encrypted refresh token. A first consent waits for a
    /// folder; a reconnect of a connection that had its folder and cursor resumes at once, and must be the same account.
    /// </summary>
    public void CompleteConsent(string emailAddress, string refreshTokenEncrypted, DateTime at)
    {
        EnsureStatus(AwaitsConsent);
        var address = Check.NotNullOrWhiteSpace(emailAddress, nameof(emailAddress), OrderIntakeConsts.MaxEmailAddressLength).Trim().ToLowerInvariant();
        if (Status == MailboxStatus.Reconnect && EmailAddress != null && !string.Equals(EmailAddress, address, StringComparison.Ordinal))
        {
            throw new BusinessException(IntegrationsErrorCodes.MailboxAccountMismatch).WithData("emailAddress", EmailAddress);
        }

        EmailAddress = address;
        RefreshTokenEncrypted = Check.NotNullOrWhiteSpace(refreshTokenEncrypted, nameof(refreshTokenEncrypted), OrderIntakeConsts.MaxRefreshTokenLength);
        ConnectedAt = at;
        LastErrorCode = null;
        LastErrorAt = null;
        Status = FolderId != null && SyncCursor != null ? MailboxStatus.Active : MailboxStatus.AwaitingFolder;
    }

    /// <summary>The provider rotated the refresh token (Microsoft does on every refresh): store the new one.</summary>
    public void ReplaceRefreshToken(string refreshTokenEncrypted)
        => RefreshTokenEncrypted = Check.NotNullOrWhiteSpace(refreshTokenEncrypted, nameof(refreshTokenEncrypted), OrderIntakeConsts.MaxRefreshTokenLength);

    /// <summary>
    /// A (new) folder was chosen: the old cursor no longer applies; the connection is Activating until the activation job
    /// took the provider's cursor and called <see cref="Activate"/> (design 72 build note 18). The sync skips it meanwhile.
    /// </summary>
    public void ChooseFolder(string folderId, string folderName)
    {
        EnsureStatus(Status is MailboxStatus.AwaitingFolder or MailboxStatus.Activating or MailboxStatus.Active or MailboxStatus.Paused);
        FolderId = Check.NotNullOrWhiteSpace(folderId, nameof(folderId), OrderIntakeConsts.MaxFolderIdLength);
        FolderName = Check.NotNullOrWhiteSpace(folderName, nameof(folderName), OrderIntakeConsts.MaxFolderNameLength);
        SyncCursor = null;
        Status = MailboxStatus.Activating;
    }

    /// <summary>The provider's current cursor of the chosen folder: from here on, arriving mail is imported.</summary>
    public void Activate(string cursor, DateTime at)
    {
        EnsureStatus(Status == MailboxStatus.Activating && FolderId != null && RefreshTokenEncrypted != null);
        SyncCursor = Check.NotNullOrWhiteSpace(cursor, nameof(cursor), OrderIntakeConsts.MaxSyncCursorLength);
        LastSyncAt = at;
        LastErrorCode = null;
        LastErrorAt = null;
        Status = MailboxStatus.Active;
    }

    /// <summary>A sync stored its messages up to <paramref name="cursor"/>; a previous error is cleared.</summary>
    public void RecordSync(string cursor, DateTime at)
    {
        SyncCursor = Check.NotNullOrWhiteSpace(cursor, nameof(cursor), OrderIntakeConsts.MaxSyncCursorLength);
        LastSyncAt = at;
        LastErrorCode = null;
        LastErrorAt = null;
    }

    /// <summary>A sync failed for a reason that may pass; it is retried next period.</summary>
    public void RecordError(string errorCode, DateTime at)
    {
        LastErrorCode = Check.NotNullOrWhiteSpace(errorCode, nameof(errorCode), OrderIntakeConsts.MaxErrorCodeLength);
        LastErrorAt = at;
    }

    /// <summary>The provider refused the refresh token: a user must consent again. The cursor stays.</summary>
    public void MarkReconnect(string errorCode, DateTime at)
    {
        RecordError(errorCode, at);
        Status = MailboxStatus.Reconnect;
    }

    /// <summary>
    /// The connection is deleted: its refresh token and cursor are forgotten before the soft delete, so the kept row holds
    /// no lasting access (design 72 build note 21). Nothing is revoked at the provider.
    /// </summary>
    public void Forget()
    {
        RefreshTokenEncrypted = null;
        SyncCursor = null;
    }

    public void Pause()
    {
        EnsureStatus(Status == MailboxStatus.Active);
        Status = MailboxStatus.Paused;
    }

    public void Resume()
    {
        EnsureStatus(Status == MailboxStatus.Paused);
        Status = MailboxStatus.Active;
    }

    private void EnsureStatus(bool allowed)
    {
        if (!allowed)
        {
            throw new BusinessException(IntegrationsErrorCodes.MailboxStateInvalid).WithData("status", Status);
        }
    }
}
