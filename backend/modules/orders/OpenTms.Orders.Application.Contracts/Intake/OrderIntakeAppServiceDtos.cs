using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.Orders.TransportOrders;
using OpenTms.Platform.Grid;
using OpenTms.Platform.ReferenceData;
using Volo.Abp.Application.Services;

namespace OpenTms.Orders.Intake;

/// <summary>
/// The review queue of the Transport app's Intake view (design 72 "Review"), route root <c>/api/orders/order-intake</c>:
/// the list, one intake with its resolution, resolve (create the order with the clerk's picks), reject, and the learned
/// aliases (<c>GET /aliases</c>, <c>DELETE /aliases/{aliasId}</c>). The method names give these routes by ABP's
/// conventional-controller rules (the verb prefix is removed, a single <c>…Id</c> parameter follows the action name).
/// </summary>
public interface IOrderIntakeAppService : IApplicationService
{
    Task<GridResponse<OrderIntakeRowDto>> GetListAsync(OrderIntakeListRequestDto input);

    Task<OrderIntakeDetailDto> GetAsync(Guid id);

    /// <summary>Re-runs the resolver with the picks; every pick on an Ambiguous or Unresolved field is stored as an alias.</summary>
    Task<OrderIntakeDetailDto> ResolveAsync(Guid id, OrderIntakeResolveDto input);

    Task<OrderIntakeDetailDto> RejectAsync(Guid id, OrderIntakeRejectDto input);

    Task<GridResponse<IntakeAliasDto>> GetAliasesAsync(IntakeAliasListRequestDto input);

    /// <summary>Deletes one learned alias; named for its route <c>DELETE /api/orders/order-intake/aliases/{aliasId}</c>.</summary>
    Task DeleteAliasesAsync(Guid aliasId);
}

// ── list ───────────────────────────────────────────────────────────────────────

/// <summary>
/// The design-50 paging plus the intake filters. <see cref="ReferenceDataListRequestDto.WildcardSearch"/> searches the
/// sender, the subject, the file name and the customer reference.
/// </summary>
public class OrderIntakeListRequestDto : ReferenceDataListRequestDto
{
    /// <summary>Empty: every status. The view opens on NeedsReview.</summary>
    public List<OrderIntakeStatus> Status { get; set; } = new();

    /// <summary>On the day the intake was received.</summary>
    public DateOnly? ReceivedFrom { get; set; }

    public DateOnly? ReceivedTo { get; set; }
}

public class OrderIntakeRowDto
{
    public Guid Id { get; set; }

    /// <summary>When Orders received the intake (its creation time).</summary>
    public DateTime ReceivedAt { get; set; }

    public string Channel { get; set; } = string.Empty;

    public OrderSource Source { get; set; }

    public string? SenderAddress { get; set; }

    public string? Subject { get; set; }

    public string? FileName { get; set; }

    public string? CustomerReference { get; set; }

    public Guid? CustomerRelationId { get; set; }

    /// <summary>The resolved customer's name, else the name as received.</summary>
    public string? CustomerName { get; set; }

    public OrderIntakeStatus Status { get; set; }

    /// <summary>The fields still Ambiguous or Unresolved.</summary>
    public int OpenFieldCount { get; set; }

    public Guid? TransportOrderId { get; set; }

    public string? OrderNumber { get; set; }
}

// ── detail ─────────────────────────────────────────────────────────────────────

public class OrderIntakeDetailDto
{
    public Guid Id { get; set; }

    public OrderIntakeStatus Status { get; set; }

    public OrderIntakeSourceSummaryDto SourceSummary { get; set; } = new();

    /// <summary>The values as received.</summary>
    public OrderIntakeDto Payload { get; set; } = new();

    /// <summary>The clerk's corrected values, when a received value was corrected.</summary>
    public OrderIntakeDto? ReviewedPayload { get; set; }

    /// <summary>One entry per field the resolver looked at, in field-path order.</summary>
    public List<OrderIntakeFieldResolutionDto> Resolution { get; set; } = new();

    public decimal MappingConfidence { get; set; }

    public bool ProfileTrusted { get; set; }

    public bool AutoConfirm { get; set; }

    public bool Corrected { get; set; }

    public string? RejectReason { get; set; }

    public Guid? TransportOrderId { get; set; }

    public string? OrderNumber { get; set; }

    public DateTime CreationTime { get; set; }

    public DateTime? LastModificationTime { get; set; }

    public Guid? LastModifierId { get; set; }
}

/// <summary>Where the intake came from; <see cref="SourceDocumentId"/> opens Integrations' trace.</summary>
public class OrderIntakeSourceSummaryDto
{
    public Guid SourceDocumentId { get; set; }

    public int IntakeIndex { get; set; }

    public string Channel { get; set; } = string.Empty;

    public OrderSource Source { get; set; }

    public string? SenderAddress { get; set; }

    public string? Subject { get; set; }

    public string? FileName { get; set; }
}

/// <summary>The resolution of one field (design 72 "Resolution"): a field path such as <c>lines[0].stops[1].address</c>.</summary>
public class OrderIntakeFieldResolutionDto
{
    public string FieldPath { get; set; } = string.Empty;

    public IntakeResolutionStatus Status { get; set; }

    public Guid? TargetId { get; set; }

    public string? TargetName { get; set; }

    public IntakeResolutionMethod? Method { get; set; }

    /// <summary>At most <see cref="IntakeConsts.MaxCandidates"/>, best first.</summary>
    public List<OrderIntakeCandidateDto> Candidates { get; set; } = new();
}

public class OrderIntakeCandidateDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>What tells candidates apart: a city, a postal code, a code.</summary>
    public string? Detail { get; set; }
}

// ── commands ───────────────────────────────────────────────────────────────────

public class OrderIntakeResolveDto : IValidatableObject
{
    /// <summary>Field path → the picked row's id (relation, address, type, unit, product, temperature).</summary>
    public Dictionary<string, Guid> Picks { get; set; } = new();

    /// <summary>The received values as the clerk corrected them; null when nothing was corrected.</summary>
    public OrderIntakeDto? ReviewedPayload { get; set; }

    /// <summary>Create and confirm (the Confirm rules apply) instead of creating a Draft.</summary>
    public bool Confirm { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Picks.Count > IntakeConsts.MaxPicks)
        {
            yield return new ValidationResult($"At most {IntakeConsts.MaxPicks} picks.", [nameof(Picks)]);
        }

        foreach (var path in Picks.Keys)
        {
            if (string.IsNullOrWhiteSpace(path) || path.Length > IntakeConsts.MaxFieldPathLength)
            {
                yield return new ValidationResult($"A field path is 1 to {IntakeConsts.MaxFieldPathLength} characters.", [nameof(Picks)]);
                yield break;
            }
        }
    }
}

public class OrderIntakeRejectDto
{
    [Required, MaxLength(IntakeConsts.MaxRejectReasonLength)]
    public string Reason { get; set; } = string.Empty;
}

// ── aliases ────────────────────────────────────────────────────────────────────

/// <summary>The learned aliases; <see cref="ReferenceDataListRequestDto.WildcardSearch"/> searches the alias text.</summary>
public class IntakeAliasListRequestDto : ReferenceDataListRequestDto
{
    public IntakeAliasKind? Kind { get; set; }
}

public class IntakeAliasDto
{
    public Guid Id { get; set; }

    public IntakeAliasKind Kind { get; set; }

    /// <summary>The customer the alias applies to; null for a tenant-wide alias.</summary>
    public Guid? ScopeRelationId { get; set; }

    public string? ScopeRelationName { get; set; }

    public string NormalizedText { get; set; } = string.Empty;

    public Guid TargetId { get; set; }

    public string? TargetName { get; set; }

    /// <summary>The relation of an address alias's target.</summary>
    public Guid? TargetRelationId { get; set; }

    public int UseCount { get; set; }

    public DateTime CreationTime { get; set; }
}
