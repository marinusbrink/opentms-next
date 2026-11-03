using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using OpenTms.Integrations.OrderIntake.Profiles;
using OpenTms.Orders.Intake;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Tracing;
using Volo.Abp.Uow;

namespace OpenTms.Integrations.OrderIntake;

/// <summary>Conversions between Orders' canonical model and the document's stored mapping (<see cref="DocumentMapping"/>).</summary>
public static class IntakeMappings
{
    public static JsonObject ToPayload(OrderIntakeDto intake)
        => JsonSerializer.SerializeToNode(intake, IntakeJson.Options) as JsonObject ?? new JsonObject();

    public static OrderIntakeDto FromPayload(JsonObject? payload)
        => payload == null ? new OrderIntakeDto() : payload.Deserialize<OrderIntakeDto>(IntakeJson.Options) ?? new OrderIntakeDto();

    public static DocumentMapping ToMapping(IReadOnlyList<ProfileRunIntake> intakes, decimal confidence)
        => new()
        {
            Intakes = intakes.Select((intake, index) => new MappedIntake
            {
                Index = index,
                Payload = ToPayload(intake.Payload),
                Provenance = new Dictionary<string, SourceLocation>(intake.Provenance, StringComparer.Ordinal),
                Confidence = confidence
            }).ToList()
        };

    /// <summary>The API's JSON order: every field it carries is its own provenance (kind Json, the path itself).</summary>
    public static ProfileRunIntake Canonical(OrderIntakeDto order)
    {
        var intake = new ProfileRunIntake { Payload = order };
        foreach (var (path, value) in IntakeFieldPaths.Flatten(order))
        {
            intake.Provenance[path] = new SourceLocation
            {
                Kind = SourceLocationKind.Json,
                Ref = path,
                Snippet = value.Length <= OrderIntakeConsts.MaxSnippetLength ? value : value[..OrderIntakeConsts.MaxSnippetLength]
            };
        }

        return intake;
    }

    /// <summary>The API's stored body: an <c>IntakeOrderRequestDto</c> (its <c>order</c>) or an <c>OrderIntakeDto</c> itself.</summary>
    public static OrderIntakeDto? ParseApiBody(byte[] content)
    {
        try
        {
            var node = JsonNode.Parse(content, new JsonNodeOptions { PropertyNameCaseInsensitive = true });
            if (node is not JsonObject body)
            {
                return null;
            }

            var order = body.FirstOrDefault(p => string.Equals(p.Key, "order", StringComparison.OrdinalIgnoreCase)).Value as JsonObject ?? body;
            return order.Deserialize<OrderIntakeDto>(new JsonSerializerOptions(IntakeJson.Options) { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// What became of a message's intakes, read from the step log (design 72 "Intake API": the stored result of an
/// ExternalId sent again, and <c>GET /messages/{externalId}</c>).
/// </summary>
public static class IntakeResultBuilder
{
    public static IntakeResultDto Build(InboundMessage message, IEnumerable<InboundDocument> documents)
    {
        var result = new IntakeResultDto { InboundMessageId = message.Id, Status = message.Status };
        foreach (var document in documents.OrderBy(d => d.CreationTime).ThenBy(d => d.Id))
        {
            var indexes = document.Steps.Where(s => s.IntakeIndex != null).Select(s => s.IntakeIndex!.Value).Distinct().OrderBy(i => i);
            foreach (var index in indexes)
            {
                var steps = document.Steps.Where(s => s.IntakeIndex == index).OrderBy(s => s.At).ThenBy(s => s.Step).ToList();
                var item = new IntakeResultItemDto
                {
                    IntakeId = steps.LastOrDefault(s => s.IntakeId != null)?.IntakeId,
                    TransportOrderId = steps.LastOrDefault(s => s.TransportOrderId != null)?.TransportOrderId,
                    OrderNumber = steps.LastOrDefault(s => s.OrderNumber != null)?.OrderNumber
                };
                var last = steps.LastOrDefault(s => s.Step is IntakeStep.OrderCreated or IntakeStep.NeedsReview or IntakeStep.Rejected);
                item.Status = last?.Step switch
                {
                    IntakeStep.OrderCreated => OrderIntakeStatus.OrderCreated,
                    IntakeStep.NeedsReview => OrderIntakeStatus.NeedsReview,
                    IntakeStep.Rejected => OrderIntakeStatus.Rejected,
                    _ => null
                };
                if (last?.Step == IntakeStep.NeedsReview && last.Parameters.TryGetValue("unresolved", out var unresolved) && unresolved.Length > 0)
                {
                    item.Unresolved = unresolved.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                }

                result.Intakes.Add(item);
            }
        }

        return result;
    }
}

/// <summary>Ids only: Hangfire stores job arguments in plain text.</summary>
[BackgroundJobName("OpenTms.Integrations.InboundMessageProcessing")]
public class InboundMessageProcessingArgs
{
    public Guid? TenantId { get; set; }

    public Guid InboundMessageId { get; set; }

    public string? CorrelationId { get; set; }
}

/// <summary>Ids only: Hangfire stores job arguments in plain text.</summary>
[BackgroundJobName("OpenTms.Integrations.ImportProfileRepair")]
public class ImportProfileRepairArgs
{
    public Guid? TenantId { get; set; }

    public Guid ImportProfileId { get; set; }

    /// <summary>The version that collected the correction; a newer version is not repaired again.</summary>
    public int ProfileVersion { get; set; }

    public Guid InboundDocumentId { get; set; }

    public Guid IntakeId { get; set; }

    public int IntakeIndex { get; set; }

    public string? CorrelationId { get; set; }
}

/// <summary>Enqueues the pipeline jobs after the current unit of work commits (the job must find the rows it was given).</summary>
public class OrderIntakeJobQueue : ITransientDependency
{
    private readonly IBackgroundJobManager _jobs;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICorrelationIdProvider _correlationIds;

    public OrderIntakeJobQueue(IBackgroundJobManager jobs, IUnitOfWorkManager unitOfWorkManager, ICurrentTenant currentTenant, ICorrelationIdProvider correlationIds)
    {
        _jobs = jobs;
        _unitOfWorkManager = unitOfWorkManager;
        _currentTenant = currentTenant;
        _correlationIds = correlationIds;
    }

    public Task EnqueueProcessingAsync(Guid inboundMessageId)
        => EnqueueAfterCommitAsync(new InboundMessageProcessingArgs
        {
            TenantId = _currentTenant.Id,
            InboundMessageId = inboundMessageId,
            CorrelationId = _correlationIds.Get()
        });

    public Task EnqueueRepairAsync(Guid profileId, int profileVersion, Guid documentId, Guid intakeId, int intakeIndex)
        => EnqueueAfterCommitAsync(new ImportProfileRepairArgs
        {
            TenantId = _currentTenant.Id,
            ImportProfileId = profileId,
            ProfileVersion = profileVersion,
            InboundDocumentId = documentId,
            IntakeId = intakeId,
            IntakeIndex = intakeIndex,
            CorrelationId = _correlationIds.Get()
        });

    private async Task EnqueueAfterCommitAsync<TArgs>(TArgs args)
    {
        var unitOfWork = _unitOfWorkManager.Current;
        if (unitOfWork == null)
        {
            await _jobs.EnqueueAsync(args);
            return;
        }

        unitOfWork.OnCompleted(async () => await _jobs.EnqueueAsync(args));
    }
}

/// <summary>The language model was unavailable (transient) and the attempts are not used up: the job throws so Hangfire retries with back-off.</summary>
public class IntakeAiRetryException : Exception
{
    public int Attempt { get; }

    public IntakeAiRetryException(int attempt, Exception innerException)
        : base($"The language model is unavailable (attempt {attempt.ToString(CultureInfo.InvariantCulture)}); retried.", innerException)
    {
        Attempt = attempt;
    }
}
