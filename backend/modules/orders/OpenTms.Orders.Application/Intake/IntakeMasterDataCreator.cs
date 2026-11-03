using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTms.MasterData.Published;
using OpenTms.MasterData.Relations;
using Volo.Abp.DependencyInjection;

namespace OpenTms.Orders.Intake;

/// <summary>
/// Carries out the resolver's creation plans (design 72 build note 26, PO decision 2026-11-03) through Master Data's
/// published <see cref="IMasterDataIntakeWriter"/> — the synchronous cross-module call of build note 27: the order needs
/// the new ids at once. Runs in the caller's unit of work, only when nothing else keeps the intake from becoming an order,
/// customer first (its id scopes the address aliases), then the places, then transport units, products and temperatures.
/// Every created field becomes Resolved with method <see cref="IntakeResolutionMethod.Created"/> and its new id, and its
/// received text is learned as an alias once the order exists. A refused write leaves its field Unresolved (no longer
/// creatable); the first refused required field stops the run, so the intake goes to review with as little written as
/// possible. Logs ids and field paths only (constitution rule 5).
/// </summary>
public class IntakeMasterDataCreator : ITransientDependency
{
    private readonly IMasterDataIntakeWriter _writer;

    public ILogger<IntakeMasterDataCreator> Logger { get; set; } = NullLogger<IntakeMasterDataCreator>.Instance;

    public IntakeMasterDataCreator(IMasterDataIntakeWriter writer)
    {
        _writer = writer;
    }

    public virtual async Task CreateAsync(OrderIntake intake, IntakeResolveResult result)
    {
        if (result.Creations.Count == 0)
        {
            return;
        }

        (Guid Id, string Name)? createdCustomer = null;
        var plans = result.Creations
            .OrderBy(p => p.Kind switch
            {
                IntakeCreationKind.Customer => 0,
                IntakeCreationKind.StopRelation or IntakeCreationKind.StopAddress => 1,
                _ => 2
            })
            .ToList();
        result.Creations.Clear();

        foreach (var plan in plans)
        {
            var written = await WriteAsync(intake, plan, createdCustomer);
            if (!written.Succeeded)
            {
                foreach (var path in plan.FieldPaths)
                {
                    if (result.Resolution.Find(path) is { } field)
                    {
                        field.Creatable = false;
                    }
                }

                Logger.LogInformation("Order intake {IntakeId}: Master Data refused to create {Kind} for {FieldPath} ({Code}).",
                    intake.Id, plan.Kind, plan.FieldPaths.FirstOrDefault(), written.RefusedCode);
                if (plan.FieldPaths.Any(p => !IntakeFieldPaths.IsOptional(p)))
                {
                    break;
                }

                continue;
            }

            Apply(result, plan, written);
            if (plan.Kind == IntakeCreationKind.Customer)
            {
                createdCustomer = (written.Id!.Value, written.Name ?? plan.Name);
            }

            Logger.LogInformation("Order intake {IntakeId}: {Kind} {TargetId} {Outcome} for {FieldCount} fields.",
                intake.Id, plan.Kind, written.RelationAddressId ?? written.Id, written.Created ? "created" : "found", plan.FieldPaths.Count);
        }
    }

    private async Task<IntakeWriteResultDto> WriteAsync(OrderIntake intake, IntakeCreationPlan plan, (Guid Id, string Name)? createdCustomer)
    {
        /* The external identifier of a new relation: the intake and the field that created it (unique per tenant), so a
         * repeated run of the same intake returns the relation it created before. */
        var reference = intake.Id.ToString("N") + " " + plan.FieldPaths.First();
        switch (plan.Kind)
        {
            case IntakeCreationKind.Customer:
                return await _writer.CreateRelationAsync(new IntakeRelationCreateDto
                {
                    Name = plan.Name,
                    Roles = RelationRoles.Customer,
                    EmailAddress = plan.EmailAddress,
                    VatNumber = plan.VatNumber,
                    ChamberOfCommerceNumber = plan.ChamberOfCommerceNumber,
                    SourceReference = reference
                });

            case IntakeCreationKind.StopAddress or IntakeCreationKind.StopRelation:
            {
                /* The relation the name matched, or the customer this intake just created under the same name (a customer
                 * that loads at its own premises), takes the address as its default address when it has none yet. */
                var owner = plan.RelationId
                            ?? (createdCustomer is { } customer && IntakeTextNormalizer.Normalize(customer.Name) == IntakeTextNormalizer.Normalize(plan.Name) ? customer.Id : null);
                if (owner != null)
                {
                    var added = await _writer.AddRelationAddressAsync(new IntakeRelationAddressAddDto { RelationId = owner.Value, Address = Address(plan) });
                    /* Taken meanwhile, archived or gone: the place becomes a relation of its own; a refused address stays refused. */
                    if (added.Succeeded || added.RefusedCode is not (IMasterDataIntakeWriter.RefusedDefaultAddressTaken or IMasterDataIntakeWriter.RefusedArchived or IMasterDataIntakeWriter.RefusedNotFound))
                    {
                        return added;
                    }
                }

                /* A relation has no "loading place" role and needs one role (Master Data rule): the place is a customer-role
                 * relation, marked as created by the intake (build note 26). */
                return await _writer.CreateRelationAsync(new IntakeRelationCreateDto
                {
                    Name = plan.Name,
                    Roles = RelationRoles.Customer,
                    Address = Address(plan),
                    SourceReference = reference
                });
            }

            default:
                return await _writer.CreateReferenceAsync(new IntakeReferenceCreateDto { Kind = plan.ReferenceKind!.Value, Name = plan.Name });
        }
    }

    private static IntakeAddressInputDto Address(IntakeCreationPlan plan) => new()
    {
        Street = plan.Address?.Street,
        HouseNumber = plan.Address?.HouseNumber,
        PostalCode = plan.Address?.PostalCode,
        City = plan.Address?.City,
        CountryCode = plan.Address?.CountryCode
    };

    /// <summary>Resolves the plan's fields with the new row and puts its id where the order is built from.</summary>
    private static void Apply(IntakeResolveResult result, IntakeCreationPlan plan, IntakeWriteResultDto written)
    {
        var id = written.Id!.Value;
        var isPlace = plan.Kind is IntakeCreationKind.StopRelation or IntakeCreationKind.StopAddress;
        foreach (var path in plan.FieldPaths)
        {
            var field = result.Resolution.Find(path);
            if (field == null)
            {
                continue;
            }

            field.Status = IntakeResolutionStatus.Resolved;
            field.TargetId = isPlace ? written.RelationAddressId : id;
            field.TargetName = isPlace && written.Detail != null ? written.Name + ", " + written.Detail : written.Name;
            field.Method = IntakeResolutionMethod.Created;
            field.Score = null;
            field.Creatable = false;
            field.Candidates.Clear();

            var target = IntakeFieldPaths.ParsePickable(path);
            switch (plan.Kind)
            {
                case IntakeCreationKind.Customer:
                    result.CustomerRelationId = id;
                    result.CustomerName = written.Name;
                    break;
                case IntakeCreationKind.StopRelation or IntakeCreationKind.StopAddress when target is { Line: { } l, Stop: { } s } && l < result.Lines.Count && s < result.Lines[l].Stops.Count:
                    result.Lines[l].Stops[s].RelationId = id;
                    result.Lines[l].Stops[s].RelationAddressId = written.RelationAddressId;
                    break;
                case IntakeCreationKind.TransportUnit when target is { Line: { } l } && l < result.Lines.Count:
                    result.Lines[l].TransportUnitId = id;
                    break;
                case IntakeCreationKind.Product when target is { Line: { } l } && l < result.Lines.Count:
                    result.Lines[l].ProductId = id;
                    break;
                case IntakeCreationKind.Temperature when target is { Line: { } l } && l < result.Lines.Count:
                    result.Lines[l].TemperatureId = id;
                    break;
            }
        }

        if (plan.Text.Length == 0)
        {
            return;
        }

        switch (plan.Kind)
        {
            case IntakeCreationKind.Customer:
                result.AliasesToLearn.Add(new IntakeAliasToLearn(IntakeAliasKind.Customer, null, plan.Text, id, null));
                /* The address aliases of this intake were scoped to a customer that did not exist yet: now it does. */
                for (var i = 0; i < result.AliasesToLearn.Count; i++)
                {
                    if (result.AliasesToLearn[i] is { Kind: IntakeAliasKind.Address, ScopeRelationId: null } unscoped)
                    {
                        result.AliasesToLearn[i] = unscoped with { ScopeRelationId = id };
                    }
                }

                break;
            case IntakeCreationKind.StopRelation or IntakeCreationKind.StopAddress when written.RelationAddressId is { } addressId:
                result.AliasesToLearn.Add(new IntakeAliasToLearn(IntakeAliasKind.Address, result.CustomerRelationId, plan.Text, addressId, id));
                break;
            case IntakeCreationKind.TransportUnit:
                result.AliasesToLearn.Add(new IntakeAliasToLearn(IntakeAliasKind.TransportUnit, null, plan.Text, id, null));
                break;
            case IntakeCreationKind.Product:
                result.AliasesToLearn.Add(new IntakeAliasToLearn(IntakeAliasKind.Product, null, plan.Text, id, null));
                break;
            case IntakeCreationKind.Temperature:
                result.AliasesToLearn.Add(new IntakeAliasToLearn(IntakeAliasKind.Temperature, null, plan.Text, id, null));
                break;
        }
    }
}
