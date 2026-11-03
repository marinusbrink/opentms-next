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
/// <see cref="IntakeStep.NeedsReview"/> step's parameter <see cref="IntakeResultMapper.UnresolvedParameter"/> (comma-separated), which the
/// pipeline writes with Orders' answer.
/// </summary>
public class IntakeResultBuilder : ITransientDependency
{
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

        return IntakeResultMapper.Build(message, documents);
    }
}
