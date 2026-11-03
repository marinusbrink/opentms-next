using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Integrations.OrderIntake.Profiles;

/// <summary>
/// A learned document layout (design 72 "Import profiles"): the rules that read one sender's (or any sender's) documents
/// of one format without AI, and the signature (headers, element paths or labels) a document must hold for the profile
/// to apply. Starts Proposed; becomes Active after enough confirmations without a correction; a correction sends an
/// Active profile back to Proposed. A repaired version replaces the rules on the same row (Version + 1, counters reset,
/// the previous rules kept for the trace). Only an Active profile may lead to an automatically confirmed order.
/// </summary>
public class ImportProfile : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public string Name { get; private set; } = default!;

    /// <summary>An address, <c>@domain</c>, or null for any sender.</summary>
    public string? SenderKey { get; private set; }

    public DocumentFormat Format { get; private set; }

    /// <summary><see cref="OrderIntakeConsts.TransportOrderTarget"/>.</summary>
    public string Target { get; private set; } = default!;

    public ImportProfileStatus Status { get; private set; }

    public int Version { get; private set; }

    public ProfileOrigin Origin { get; private set; }

    public string? ModelId { get; private set; }

    public ImportProfileRules Rules { get; private set; } = default!;

    /// <summary>The rules of the version before this one; null on version 1.</summary>
    public ImportProfileRules? PreviousRules { get; private set; }

    public List<string> Signature { get; private set; } = new();

    public int ConfirmedCount { get; private set; }

    public int CorrectedCount { get; private set; }

    public DateTime? LastUsedAt { get; private set; }

    /// <summary>The inbound document the profile was learned from.</summary>
    public Guid SourceDocumentId { get; private set; }

    protected ImportProfile()
    {
    }

    public ImportProfile(
        Guid id, Guid? tenantId, string name, string? senderKey, DocumentFormat format, string target, ImportProfileRules rules,
        IEnumerable<string> signature, ProfileOrigin origin, string? modelId, Guid sourceDocumentId)
        : base(id)
    {
        TenantId = tenantId;
        Rename(name);
        SenderKey = string.IsNullOrWhiteSpace(senderKey)
            ? null
            : Check.Length(senderKey.Trim().ToLowerInvariant(), nameof(senderKey), OrderIntakeConsts.MaxSenderKeyLength);
        Format = format;
        Target = Check.NotNullOrWhiteSpace(target, nameof(target), OrderIntakeConsts.MaxTargetLength);
        SourceDocumentId = sourceDocumentId;
        Status = ImportProfileStatus.Proposed;
        Version = 1;
        SetVersion(rules, signature, origin, modelId);
    }

    /// <summary>A retired profile is no longer matched.</summary>
    public bool IsCandidate => Status != ImportProfileStatus.Retired;

    public void Rename(string name) => Name = Check.NotNullOrWhiteSpace(name, nameof(name), OrderIntakeConsts.MaxProfileNameLength).Trim();

    /// <summary>
    /// An order read by this profile was confirmed without a correction. A Proposed profile with
    /// <paramref name="activationThreshold"/> confirmations and no correction becomes Active.
    /// </summary>
    public void RecordConfirmation(int activationThreshold)
    {
        Check.Range(activationThreshold, nameof(activationThreshold), OrderIntakeConsts.MinProfileActivationThreshold, OrderIntakeConsts.MaxProfileActivationThreshold);
        ConfirmedCount++;
        if (Status == ImportProfileStatus.Proposed && ConfirmedCount >= activationThreshold && CorrectedCount == 0)
        {
            Status = ImportProfileStatus.Active;
        }
    }

    /// <summary>A clerk corrected a value this profile read: an Active profile goes back to Proposed.</summary>
    public void RecordCorrection()
    {
        CorrectedCount++;
        if (Status == ImportProfileStatus.Active)
        {
            Status = ImportProfileStatus.Proposed;
        }
    }

    /// <summary>A repaired (or relearned) version: the old rules are kept, the counters reset, the profile is Proposed again.</summary>
    public void NextVersion(ImportProfileRules rules, IEnumerable<string> signature, ProfileOrigin origin, string? modelId)
    {
        EnsureStatus(Status != ImportProfileStatus.Retired);
        PreviousRules = Rules;
        Version++;
        ConfirmedCount = 0;
        CorrectedCount = 0;
        Status = ImportProfileStatus.Proposed;
        SetVersion(rules, signature, origin, modelId);
    }

    /// <summary>A user trusts the profile by hand (also a retired one, to use it again).</summary>
    public void Activate()
    {
        EnsureStatus(Status != ImportProfileStatus.Active);
        Status = ImportProfileStatus.Active;
    }

    public void Retire()
    {
        EnsureStatus(Status != ImportProfileStatus.Retired);
        Status = ImportProfileStatus.Retired;
    }

    public void RecordUse(DateTime at) => LastUsedAt = at;

    private void SetVersion(ImportProfileRules rules, IEnumerable<string> signature, ProfileOrigin origin, string? modelId)
    {
        Rules = Check.NotNull(rules, nameof(rules));
        var entries = Check.NotNull(signature, nameof(signature))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (entries.Count > OrderIntakeConsts.MaxSignatureEntries || entries.Any(s => s.Length > OrderIntakeConsts.MaxSignatureEntryLength))
        {
            throw new ArgumentException(
                $"A signature holds at most {OrderIntakeConsts.MaxSignatureEntries} entries of at most {OrderIntakeConsts.MaxSignatureEntryLength} characters.",
                nameof(signature));
        }

        Signature = entries;
        Origin = origin;
        ModelId = Check.Length(modelId, nameof(modelId), OrderIntakeConsts.MaxModelIdLength);
    }

    private void EnsureStatus(bool allowed)
    {
        if (!allowed)
        {
            throw new BusinessException(IntegrationsErrorCodes.ImportProfileStateInvalid).WithData("status", Status);
        }
    }
}
