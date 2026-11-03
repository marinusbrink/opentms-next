using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenTms.Orders.Intake;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;

namespace OpenTms.Integrations.OrderIntake.IntakeApi;

/// <summary>
/// The stored answer of an intake API message (design 72 "Intake API": the same ExternalId again, and
/// <c>GET messages/{externalId}</c>), read from the message's status and its documents' step log: per intake index the
/// intake, its latest status, the order and the field paths still unresolved. The unresolved paths come from the
/// <see cref="IntakeStep.NeedsReview"/> step's parameter <see cref="UnresolvedParameter"/> (comma-separated), which the
/// pipeline writes with Orders' answer.
/// </summary>
public class IntakeResultBuilder : ITransientDependency
{
    /// <summary>The NeedsReview step's parameter holding the unresolved field paths, comma-separated.</summary>
    public const string UnresolvedParameter = "unresolved";

    private readonly IRepository<InboundDocument, Guid> _documents;
    private readonly IAsyncQueryableExecuter _asyncExecuter;

    public IntakeResultBuilder(IRepository<InboundDocument, Guid> documents, IAsyncQueryableExecuter asyncExecuter)
    {
        _documents = documents;
        _asyncExecuter = asyncExecuter;
    }

    public async Task<IntakeResultDto> BuildAsync(InboundMessage message, CancellationToken cancellationToken = default)
    {
        /* An API message has one document; bounded anyway (a mail may have a handful). */
        var documents = await _asyncExecuter.ToListAsync(
            (await _documents.WithDetailsAsync(d => d.Steps))
                .Where(d => d.InboundMessageId == message.Id)
                .OrderBy(d => d.CreationTime)
                .Take(50),
            cancellationToken);

        var result = new IntakeResultDto { InboundMessageId = message.Id, Status = message.Status };
        foreach (var document in documents)
        {
            foreach (var steps in document.Steps.Where(s => s.IntakeIndex != null).GroupBy(s => s.IntakeIndex!.Value).OrderBy(g => g.Key))
            {
                result.Intakes.Add(ToItem(steps.OrderBy(s => s.At).ToList()));
            }
        }

        return result;
    }

    private static IntakeResultItemDto ToItem(IReadOnlyList<InboundDocumentStep> steps)
    {
        var item = new IntakeResultItemDto
        {
            IntakeId = steps.LastOrDefault(s => s.IntakeId != null)?.IntakeId,
            TransportOrderId = steps.LastOrDefault(s => s.TransportOrderId != null)?.TransportOrderId,
            OrderNumber = steps.LastOrDefault(s => s.OrderNumber != null)?.OrderNumber
        };

        var last = steps.LastOrDefault(s => StatusOf(s.Step) != null);
        item.Status = last == null ? null : StatusOf(last.Step);
        if (item.Status == OrderIntakeStatus.NeedsReview && last!.Parameters.TryGetValue(UnresolvedParameter, out var unresolved))
        {
            item.Unresolved = unresolved.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }

        return item;
    }

    private static OrderIntakeStatus? StatusOf(IntakeStep step) => step switch
    {
        IntakeStep.NeedsReview => OrderIntakeStatus.NeedsReview,
        IntakeStep.OrderCreated or IntakeStep.Resolved or IntakeStep.OrderConfirmed => OrderIntakeStatus.OrderCreated,
        IntakeStep.Rejected => OrderIntakeStatus.Rejected,
        _ => null
    };
}
