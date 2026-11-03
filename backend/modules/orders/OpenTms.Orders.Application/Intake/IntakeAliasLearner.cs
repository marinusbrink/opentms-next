using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTms.MasterData.Published;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Orders.Intake;

/// <summary>
/// Learns from confirmations (PO decision on design 72): when a user confirms, unedited, a Draft that came from an intake,
/// every field of that intake resolved by <see cref="IntakeResolutionMethod.Match"/> (the customer by e-mail domain or
/// normalised name, an address by name and city) becomes an alias of its received text, with the alias rules of a review
/// pick (an address alias scoped to the intake's customer). Idempotent (constitution rule 3): an alias that exists only
/// has its use counter raised, its target is never changed here.
/// </summary>
public class IntakeAliasLearner : ITransientDependency
{
    private readonly IRepository<IntakeAlias, Guid> _aliases;
    private readonly IMasterDataReferenceLookup _lookup;
    private readonly IGuidGenerator _guids;
    private readonly ICurrentTenant _currentTenant;

    public ILogger<IntakeAliasLearner> Logger { get; set; } = NullLogger<IntakeAliasLearner>.Instance;

    public IntakeAliasLearner(IRepository<IntakeAlias, Guid> aliases, IMasterDataReferenceLookup lookup, IGuidGenerator guids, ICurrentTenant currentTenant)
    {
        _aliases = aliases;
        _lookup = lookup;
        _guids = guids;
        _currentTenant = currentTenant;
    }

    public virtual async Task LearnMatchesAsync(OrderIntake intake)
    {
        var matched = intake.Resolution.Fields
            .Where(f => f.Status == IntakeResolutionStatus.Resolved && f.Method == IntakeResolutionMethod.Match && f.TargetId != null)
            .ToList();
        if (matched.Count == 0)
        {
            return;
        }

        var payload = OrderIntakeProcessor.Deserialize(intake.ReviewedPayload ?? intake.Payload);
        var learned = new List<IntakeAliasToLearn>();
        foreach (var field in matched)
        {
            var target = IntakeFieldPaths.ParsePickable(field.FieldPath);
            if (target == null)
            {
                continue;
            }

            switch (target.Kind)
            {
                case IntakeAliasKind.Customer:
                    learned.Add(new IntakeAliasToLearn(IntakeAliasKind.Customer, null, OrderIntakeResolver.CustomerText(payload.Customer, intake.SenderAddress), field.TargetId!.Value, null));
                    break;
                case IntakeAliasKind.Address when target.Line < payload.Lines.Count && target.Stop < payload.Lines[target.Line!.Value].Stops.Count:
                    var stop = payload.Lines[target.Line!.Value].Stops[target.Stop!.Value];
                    learned.Add(new IntakeAliasToLearn(IntakeAliasKind.Address, intake.CustomerRelationId, OrderIntakeResolver.AddressText(stop.Address), field.TargetId!.Value, null));
                    break;
            }
        }

        learned = learned.Where(a => a.Text.Length > 0).DistinctBy(a => (a.Kind, a.ScopeRelationId, a.Text)).ToList();
        if (learned.Count == 0)
        {
            return;
        }

        var addressIds = learned.Where(a => a.Kind == IntakeAliasKind.Address).Select(a => a.TargetId).Distinct().ToList();
        var addressRelations = addressIds.Count == 0
            ? new Dictionary<Guid, Guid>()
            : (await _lookup.GetRelationAddressesAsync(addressIds)).ToDictionary(a => a.Id, a => a.RelationId);

        foreach (var alias in learned)
        {
            var existing = await _aliases.FindAsync(a => a.Kind == alias.Kind && a.ScopeRelationId == alias.ScopeRelationId && a.NormalizedText == alias.Text);
            if (existing != null)
            {
                existing.Used();
                await _aliases.UpdateAsync(existing);
                continue;
            }

            if (alias.Kind == IntakeAliasKind.Address && !addressRelations.ContainsKey(alias.TargetId))
            {
                continue; /* the address disappeared since the intake was resolved */
            }

            await _aliases.InsertAsync(new IntakeAlias(_guids.Create(), _currentTenant.Id, alias.Kind, alias.ScopeRelationId, alias.Text, alias.TargetId,
                alias.Kind == IntakeAliasKind.Address ? addressRelations[alias.TargetId] : null));
        }

        Logger.LogInformation("Order intake {IntakeId}: {Count} matched fields learned as aliases on confirmation.", intake.Id, learned.Count);
    }
}
