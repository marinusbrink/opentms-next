using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.MasterData.Published;
using OpenTms.Orders.TransportOrders;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace OpenTms.Orders.Intake;

/// <summary>
/// The deterministic resolution of an intake against master data (design 72 "Resolution"; no AI). Applies the design's
/// rules in their order per field — customer, order type, order date, and per line the transport unit, product and
/// temperature, per stop the address, waypoint type and date — and records for every field its status, the rule that
/// resolved it and the candidates. A clerk's picks override a field (method User). Reads Master Data through its published
/// match query and reference lookup (module boundary rule 3, stated call 5) and Orders' own types and aliases; every read
/// is batched per kind, address matches are cached per address within one intake. Writes nothing.
/// </summary>
public class OrderIntakeResolver : ITransientDependency
{
    private readonly IMasterDataMatchQuery _match;
    private readonly IMasterDataReferenceLookup _lookup;
    private readonly IRepository<TransportOrderType, Guid> _orderTypes;
    private readonly IRepository<WaypointType, Guid> _waypointTypes;
    private readonly IRepository<IntakeAlias, Guid> _aliases;

    public OrderIntakeResolver(
        IMasterDataMatchQuery match,
        IMasterDataReferenceLookup lookup,
        IRepository<TransportOrderType, Guid> orderTypes,
        IRepository<WaypointType, Guid> waypointTypes,
        IRepository<IntakeAlias, Guid> aliases)
    {
        _match = match;
        _lookup = lookup;
        _orderTypes = orderTypes;
        _waypointTypes = waypointTypes;
        _aliases = aliases;
    }

    public virtual async Task<IntakeResolveResult> ResolveAsync(IntakeResolveRequest request)
    {
        Check.NotNull(request, nameof(request));
        var run = new Run(request, CultureInfo.CurrentUICulture.Name);
        ParsePicks(run);
        await LoadTypesAsync(run);
        await LoadAliasesAsync(run);
        await LoadReferencesAsync(run);
        ValidatePicks(run);

        await ResolveCustomerAsync(run);
        ResolveOrderType(run);
        ResolveOrderDate(run);
        await ResolveLinesAsync(run);
        return run.Result;
    }

    // ── preparation ────────────────────────────────────────────────────────────

    private static void ParsePicks(Run run)
    {
        var payload = run.Payload;
        foreach (var (path, id) in run.Request.Picks)
        {
            var target = IntakeFieldPaths.ParsePickable(path);
            var exists = target != null
                         && id != Guid.Empty
                         && (target.Line == null || target.Line < payload.Lines.Count)
                         && (target.Stop == null || target.Stop < payload.Lines[target.Line!.Value].Stops.Count);
            if (!exists)
            {
                throw PickInvalid(path);
            }

            run.Picks[path] = (target!.Kind, id);
        }
    }

    private async Task LoadTypesAsync(Run run)
    {
        var orderTypes = await _orderTypes.GetListAsync(t => !t.IsArchived, includeDetails: true);
        run.OrderTypes = orderTypes.OrderByDescending(t => t.IsDefault).ThenBy(t => t.Code).Take(IntakeConsts.MaxTypes).ToDictionary(t => t.Id);
        var waypointTypes = await _waypointTypes.GetListAsync(t => !t.IsArchived);
        run.WaypointTypes = waypointTypes.OrderBy(t => t.Code).Take(IntakeConsts.MaxTypes).ToDictionary(t => t.Id);
    }

    private async Task LoadAliasesAsync(Run run)
    {
        var texts = new HashSet<string>(StringComparer.Ordinal);
        void Add(string text)
        {
            if (text.Length > 0)
            {
                texts.Add(text);
            }
        }

        Add(run.CustomerText);
        Add(IntakeTextNormalizer.Normalize(run.Payload.OrderTypeName));
        foreach (var line in run.Payload.Lines)
        {
            Add(IntakeTextNormalizer.Normalize(line.TransportUnitName));
            Add(IntakeTextNormalizer.Normalize(line.ProductName));
            Add(IntakeTextNormalizer.Normalize(line.TemperatureName));
            foreach (var stop in line.Stops)
            {
                Add(AddressText(stop.Address));
                Add(IntakeTextNormalizer.Normalize(stop.WaypointTypeName));
            }
        }

        foreach (var chunk in texts.Chunk(IMasterDataReferenceLookup.MaxKeys))
        {
            foreach (var alias in await _aliases.GetListAsync(a => chunk.Contains(a.NormalizedText)))
            {
                run.Aliases[(alias.Kind, alias.ScopeRelationId, alias.NormalizedText)] = alias;
            }
        }
    }

    /// <summary>Every id the intake names, picks and aliases point at, read once per kind; names for the given texts once per kind.</summary>
    private async Task LoadReferencesAsync(Run run)
    {
        var payload = run.Payload;
        var settings = run.Request.Settings;
        var picksOf = (IntakeAliasKind kind) => run.Picks.Values.Where(p => p.Kind == kind).Select(p => p.Id);
        var aliasesOf = (IntakeAliasKind kind) => run.Aliases.Values.Where(a => a.Kind == kind).Select(a => a.TargetId);

        var relationIds = new[] { settings.FixedCustomerRelationId, settings.DefaultCustomerRelationId, payload.Customer?.RelationId }
            .Concat(payload.Lines.SelectMany(l => l.Stops).Select(s => s.RelationId))
            .OfType<Guid>()
            .Concat(picksOf(IntakeAliasKind.Customer))
            .Concat(aliasesOf(IntakeAliasKind.Customer));
        foreach (var chunk in Keys(relationIds).Chunk(IMasterDataReferenceLookup.MaxKeys))
        {
            foreach (var relation in await _lookup.GetRelationsAsync(chunk))
            {
                run.Relations[relation.Id] = relation;
            }
        }

        var addressIds = payload.Lines.SelectMany(l => l.Stops).Select(s => s.RelationAddressId).OfType<Guid>()
            .Concat(picksOf(IntakeAliasKind.Address))
            .Concat(aliasesOf(IntakeAliasKind.Address));
        foreach (var chunk in Keys(addressIds).Chunk(IMasterDataReferenceLookup.MaxKeys))
        {
            foreach (var address in await _lookup.GetRelationAddressesAsync(chunk))
            {
                run.Addresses[address.Id] = address;
            }
        }

        await LoadKindAsync(run, IntakeAliasKind.TransportUnit, MasterDataReferenceKind.TransportUnit, payload.Lines.Select(l => l.TransportUnitId), payload.Lines.Select(l => l.TransportUnitName));
        await LoadKindAsync(run, IntakeAliasKind.Product, MasterDataReferenceKind.Product, [], payload.Lines.Select(l => l.ProductName));
        await LoadKindAsync(run, IntakeAliasKind.Temperature, MasterDataReferenceKind.Temperature, [], payload.Lines.Select(l => l.TemperatureName));
    }

    private async Task LoadKindAsync(Run run, IntakeAliasKind aliasKind, MasterDataReferenceKind kind, IEnumerable<Guid?> givenIds, IEnumerable<string?> names)
    {
        var rows = run.References[kind] = new Dictionary<Guid, MasterDataReferenceDto>();
        var ids = givenIds.OfType<Guid>()
            .Concat(run.Picks.Values.Where(p => p.Kind == aliasKind).Select(p => p.Id))
            .Concat(run.Aliases.Values.Where(a => a.Kind == aliasKind).Select(a => a.TargetId));
        foreach (var chunk in Keys(ids).Chunk(IMasterDataReferenceLookup.MaxKeys))
        {
            foreach (var row in await _lookup.GetReferencesAsync(kind, chunk))
            {
                rows[row.Id] = row;
            }
        }

        var matches = run.NameMatches[kind] = new Dictionary<string, List<ReferenceNameMatchDto>>(StringComparer.OrdinalIgnoreCase);
        var texts = names.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var chunk in texts.Chunk(IMasterDataMatchQuery.MaxNames))
        {
            foreach (var match in await _match.FindReferencesByNameAsync(new ReferenceNameMatchQuery { Kind = kind, Names = chunk.ToList() }))
            {
                if (!matches.TryGetValue(match.MatchedText, out var list))
                {
                    matches[match.MatchedText] = list = new List<ReferenceNameMatchDto>();
                }

                if (list.All(m => m.Id != match.Id))
                {
                    list.Add(match);
                }
            }
        }
    }

    private static void ValidatePicks(Run run)
    {
        foreach (var (path, (kind, id)) in run.Picks)
        {
            var valid = kind switch
            {
                IntakeAliasKind.Customer => run.ActiveRelation(id) != null,
                IntakeAliasKind.Address => run.Addresses.ContainsKey(id),
                IntakeAliasKind.OrderType => run.OrderTypes.ContainsKey(id),
                IntakeAliasKind.WaypointType => run.WaypointTypes.ContainsKey(id),
                IntakeAliasKind.TransportUnit => run.ActiveReference(MasterDataReferenceKind.TransportUnit, id) != null,
                IntakeAliasKind.Product => run.ActiveReference(MasterDataReferenceKind.Product, id) != null,
                IntakeAliasKind.Temperature => run.ActiveReference(MasterDataReferenceKind.Temperature, id) != null,
                _ => false
            };
            if (!valid)
            {
                throw PickInvalid(path);
            }
        }
    }

    // ── customer ───────────────────────────────────────────────────────────────

    private async Task ResolveCustomerAsync(Run run)
    {
        var customer = run.Payload.Customer;
        var settings = run.Request.Settings;
        var field = run.Field(IntakeFieldPaths.Customer);

        if (run.ActiveRelation(settings.FixedCustomerRelationId) is { } fixedRelation)
        {
            Resolve(field, fixedRelation.Id, fixedRelation.Name, IntakeResolutionMethod.Given);
        }
        else if (run.ActiveRelation(customer?.RelationId) is { } given)
        {
            Resolve(field, given.Id, given.Name, IntakeResolutionMethod.Given);
        }
        else if (run.Alias(IntakeAliasKind.Customer, null, run.CustomerText) is { } alias && run.ActiveRelation(alias.TargetId) is { } aliased)
        {
            Resolve(field, aliased.Id, aliased.Name, IntakeResolutionMethod.Alias);
            run.Result.UsedAliases.Add(alias);
        }
        else
        {
            var query = new RelationMatchQuery
            {
                Code = customer?.Code,
                VatNumber = customer?.VatNumber,
                ChamberOfCommerceNumber = customer?.ChamberOfCommerceNumber,
                /* A mail's sender says who ordered when the document names no address (design 72 edge case: several addresses). */
                EmailAddress = Present(customer?.EmailAddress) ?? Present(settings.SenderAddress),
                Name = customer?.Name
            };
            var matches = HasAny(query.Code, query.VatNumber, query.ChamberOfCommerceNumber, query.EmailAddress, query.Name)
                ? await _match.FindRelationsAsync(query)
                : [];

            RelationMatchDto? chosen = null;
            var method = IntakeResolutionMethod.Exact;
            foreach (var kind in new[] { RelationMatchKind.Code, RelationMatchKind.VatNumber, RelationMatchKind.ChamberOfCommerceNumber, RelationMatchKind.EmailAddress, RelationMatchKind.EmailDomain, RelationMatchKind.Name })
            {
                var ofKind = matches.Where(m => m.MatchKind == kind).ToList();
                if (ofKind.Count == 1)
                {
                    chosen = ofKind[0];
                    /* Conservative (PO decision on design 72): only a unique key match is Exact; the e-mail domain and the
                     * normalised name are a Match, which never leads to an automatic confirm (learned on confirmation). */
                    method = kind is RelationMatchKind.EmailDomain or RelationMatchKind.Name ? IntakeResolutionMethod.Match : IntakeResolutionMethod.Exact;
                    break;
                }
            }

            if (chosen != null)
            {
                Resolve(field, chosen.Id, chosen.Name, method);
            }
            else if (run.ActiveRelation(settings.DefaultCustomerRelationId) is { } fallback)
            {
                Resolve(field, fallback.Id, fallback.Name, IntakeResolutionMethod.Default);
            }
            else
            {
                Open(field, matches.Select(m => new IntakeCandidate { Id = m.Id, Name = m.Name, Detail = m.Detail }));
            }
        }

        ApplyPick(run, field, run.CustomerText, null, (id, _) => (run.ActiveRelation(id)!.Name, null));
        run.Result.CustomerRelationId = field.Status == IntakeResolutionStatus.Resolved ? field.TargetId : null;
        run.Result.CustomerName = field.Status == IntakeResolutionStatus.Resolved ? field.TargetName : customer?.Name;
    }

    // ── order type and order date ──────────────────────────────────────────────

    private static void ResolveOrderType(Run run)
    {
        var payload = run.Payload;
        var field = run.Field(IntakeFieldPaths.OrderType);
        var text = IntakeTextNormalizer.Normalize(payload.OrderTypeName);

        if (payload.TransportOrderTypeId is { } givenId && run.OrderTypes.TryGetValue(givenId, out var given))
        {
            Resolve(field, given.Id, run.NameOf(given.Name), IntakeResolutionMethod.Given);
        }
        else if (run.Alias(IntakeAliasKind.OrderType, null, text) is { } alias && run.OrderTypes.TryGetValue(alias.TargetId, out var aliased))
        {
            Resolve(field, aliased.Id, run.NameOf(aliased.Name), IntakeResolutionMethod.Alias);
            run.Result.UsedAliases.Add(alias);
        }
        else
        {
            var byName = text.Length == 0 ? [] : run.OrderTypes.Values.Where(t => NameMatches(t.Name.Texts.Values, text)).ToList();
            if (byName.Count == 1)
            {
                Resolve(field, byName[0].Id, run.NameOf(byName[0].Name), IntakeResolutionMethod.Exact);
            }
            else if (byName.Count == 0 && run.Request.Settings.DefaultTransportOrderTypeId is { } defaultId && run.OrderTypes.TryGetValue(defaultId, out var channelDefault))
            {
                Resolve(field, channelDefault.Id, run.NameOf(channelDefault.Name), IntakeResolutionMethod.Default);
            }
            else if (byName.Count == 0 && run.OrderTypes.Count == 1)
            {
                var only = run.OrderTypes.Values.Single();
                Resolve(field, only.Id, run.NameOf(only.Name), IntakeResolutionMethod.Default);
            }
            else
            {
                var candidates = byName.Count > 0 ? byName : run.OrderTypes.Values.ToList();
                Open(field, candidates.Select(t => new IntakeCandidate { Id = t.Id, Name = run.NameOf(t.Name), Detail = t.Code }));
            }
        }

        ApplyPick(run, field, text, null, (id, _) => (run.NameOf(run.OrderTypes[id].Name), null));
        run.OrderType = field.Status == IntakeResolutionStatus.Resolved ? run.OrderTypes[field.TargetId!.Value] : null;
        run.Result.TransportOrderTypeId = run.OrderType?.Id;
    }

    private static void ResolveOrderDate(Run run)
    {
        var text = run.Payload.OrderDate;
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (TryDate(text, out var date))
        {
            run.Result.OrderDate = date;
            return;
        }

        Open(run.Field(IntakeFieldPaths.OrderDate), []);
    }

    // ── lines and stops ────────────────────────────────────────────────────────

    private async Task ResolveLinesAsync(Run run)
    {
        var payload = run.Payload;
        if (payload.Lines.Count == 0)
        {
            Open(run.Field(IntakeFieldPaths.Lines), []);
            return;
        }

        for (var i = 0; i < payload.Lines.Count; i++)
        {
            var line = payload.Lines[i];
            var resolved = new ResolvedIntakeLine
            {
                TransportUnitId = ResolveReference(run, IntakeFieldPaths.Line(i, IntakeFieldPaths.TransportUnitField), IntakeAliasKind.TransportUnit, MasterDataReferenceKind.TransportUnit,
                    line.TransportUnitId, line.TransportUnitName, required: true),
                ProductId = ResolveReference(run, IntakeFieldPaths.Line(i, IntakeFieldPaths.ProductField), IntakeAliasKind.Product, MasterDataReferenceKind.Product,
                    null, line.ProductName, required: false),
                TemperatureId = ResolveReference(run, IntakeFieldPaths.Line(i, IntakeFieldPaths.TemperatureField), IntakeAliasKind.Temperature, MasterDataReferenceKind.Temperature,
                    null, line.TemperatureName, required: false)
            };

            if (line.Stops.Count == 0)
            {
                Open(run.Field(IntakeFieldPaths.Line(i, IntakeFieldPaths.StopsField)), []);
            }

            for (var j = 0; j < line.Stops.Count; j++)
            {
                resolved.Stops.Add(await ResolveStopAsync(run, i, j, line.Stops[j]));
            }

            run.Result.Lines.Add(resolved);
        }
    }

    /// <summary>A transport unit, product or temperature: given id → alias → name exact → unresolved (design 72).</summary>
    private static Guid? ResolveReference(Run run, string path, IntakeAliasKind aliasKind, MasterDataReferenceKind kind, Guid? givenId, string? name, bool required)
    {
        var text = IntakeTextNormalizer.Normalize(name);
        if (!required && givenId == null && text.Length == 0 && !run.Picks.ContainsKey(path))
        {
            return null;
        }

        var field = run.Field(path);
        if (run.ActiveReference(kind, givenId) is { } given)
        {
            Resolve(field, given.Id, given.Name, IntakeResolutionMethod.Given);
        }
        else if (run.Alias(aliasKind, null, text) is { } alias && run.ActiveReference(kind, alias.TargetId) is { } aliased)
        {
            Resolve(field, aliased.Id, aliased.Name, IntakeResolutionMethod.Alias);
            run.Result.UsedAliases.Add(alias);
        }
        else
        {
            var byName = string.IsNullOrWhiteSpace(name) ? [] : run.NameMatches[kind].GetValueOrDefault(name.Trim()) ?? [];
            if (byName.Count == 1)
            {
                Resolve(field, byName[0].Id, byName[0].Name, IntakeResolutionMethod.Exact);
            }
            else
            {
                Open(field, byName.Select(m => new IntakeCandidate { Id = m.Id, Name = m.Name }));
            }
        }

        ApplyPick(run, field, text, null, (id, _) => (run.ActiveReference(kind, id)!.Name, null));
        return field.Status == IntakeResolutionStatus.Resolved ? field.TargetId : null;
    }

    private async Task<ResolvedIntakeStop> ResolveStopAsync(Run run, int lineIndex, int stopIndex, OrderIntakeStopDto stop)
    {
        var resolved = new ResolvedIntakeStop();

        /* The place: given ids → alias (the customer's, then tenant-wide) → postcode + house number → name + city. */
        var addressField = run.Field(IntakeFieldPaths.Stop(lineIndex, stopIndex, IntakeFieldPaths.AddressField));
        var addressText = AddressText(stop.Address);
        var customerId = run.Result.CustomerRelationId;
        if (stop.RelationAddressId is { } givenAddressId && run.Addresses.TryGetValue(givenAddressId, out var given)
            && (stop.RelationId == null || stop.RelationId == given.RelationId) && run.ActiveRelation(given.RelationId) != null)
        {
            ResolveAddress(run, addressField, given, IntakeResolutionMethod.Given);
        }
        else if ((run.Alias(IntakeAliasKind.Address, customerId, addressText) ?? run.Alias(IntakeAliasKind.Address, null, addressText)) is { } alias
                 && run.Addresses.TryGetValue(alias.TargetId, out var aliased))
        {
            ResolveAddress(run, addressField, aliased, IntakeResolutionMethod.Alias);
            run.Result.UsedAliases.Add(alias);
        }
        else
        {
            var matches = await MatchAddressAsync(run, stop.Address, addressText);
            var exact = matches.Where(m => m.MatchKind == AddressMatchKind.PostalCodeAndHouseNumber).ToList();
            if (exact.Count > 1)
            {
                /* Several relations at one address (a business park): the name on the document tells them apart. */
                var nameKey = IntakeTextNormalizer.Normalize(stop.Address.Name);
                var named = nameKey.Length == 0 ? [] : exact.Where(m => IntakeTextNormalizer.Normalize(m.Name) == nameKey).ToList();
                exact = named.Count == 1 ? named : exact;
            }

            var byNameAndCity = matches.Where(m => m.MatchKind == AddressMatchKind.NameAndCity).ToList();
            var chosen = exact.Count == 1 ? exact[0] : exact.Count == 0 && byNameAndCity.Count == 1 ? byNameAndCity[0] : null;
            if (chosen != null)
            {
                /* Postcode + house number is Exact; name + city is a Match (PO decision on design 72), never auto-confirmed. */
                Resolve(addressField, chosen.Id, chosen.Name + ", " + chosen.Detail,
                    chosen.MatchKind == AddressMatchKind.PostalCodeAndHouseNumber ? IntakeResolutionMethod.Exact : IntakeResolutionMethod.Match);
                run.AddressRelations[chosen.Id] = chosen.RelationId;
            }
            else
            {
                Open(addressField, matches.Select(m => new IntakeCandidate { Id = m.Id, Name = m.Name, Detail = m.Detail }));
                foreach (var m in matches)
                {
                    run.AddressRelations[m.Id] = m.RelationId;
                }
            }
        }

        ApplyPick(run, addressField, addressText, customerId, (id, _) =>
        {
            var picked = run.Addresses[id];
            run.AddressRelations[id] = picked.RelationId;
            return (Describe(picked), picked.RelationId);
        });
        if (addressField.Status == IntakeResolutionStatus.Resolved)
        {
            resolved.RelationAddressId = addressField.TargetId;
            resolved.RelationId = run.AddressRelations[addressField.TargetId!.Value];
        }

        resolved.WaypointTypeId = ResolveWaypointType(run, lineIndex, stopIndex, stop);
        resolved.Date = ResolveStopDate(run, lineIndex, stopIndex, stop);
        resolved.WindowFrom = ResolveTime(run, IntakeFieldPaths.Stop(lineIndex, stopIndex, IntakeFieldPaths.TimeFromField), stop.TimeFrom);
        resolved.WindowTill = ResolveTime(run, IntakeFieldPaths.Stop(lineIndex, stopIndex, IntakeFieldPaths.TimeTillField), stop.TimeTill);
        return resolved;
    }

    private static void ResolveAddress(Run run, IntakeFieldResolution field, MasterDataAddressReferenceDto address, IntakeResolutionMethod method)
    {
        Resolve(field, address.Id, Describe(address), method);
        run.AddressRelations[address.Id] = address.RelationId;
    }

    private async Task<List<RelationAddressMatchDto>> MatchAddressAsync(Run run, OrderIntakeAddressDto address, string addressText)
    {
        var searchable = !string.IsNullOrWhiteSpace(address.PostalCode) || (!string.IsNullOrWhiteSpace(address.Name) && !string.IsNullOrWhiteSpace(address.City));
        if (!searchable)
        {
            return [];
        }

        if (run.AddressMatches.TryGetValue(addressText, out var cached))
        {
            return cached;
        }

        var country = address.CountryCode?.Trim();
        var matches = await _match.FindRelationAddressesAsync(new AddressMatchQuery
        {
            PostalCode = address.PostalCode,
            HouseNumber = address.HouseNumber,
            CountryCode = country is { Length: 2 } && country.All(char.IsAsciiLetter) ? country : null,
            Name = address.Name,
            City = address.City,
            Street = address.Street
        });
        run.AddressMatches[addressText] = matches;
        return matches;
    }

    /// <summary>Given name or alias → the order type's pattern (Pickup: first Load, Delivery: first Unload) → the one type with that activity.</summary>
    private static Guid? ResolveWaypointType(Run run, int lineIndex, int stopIndex, OrderIntakeStopDto stop)
    {
        var field = run.Field(IntakeFieldPaths.Stop(lineIndex, stopIndex, IntakeFieldPaths.WaypointTypeField));
        var text = IntakeTextNormalizer.Normalize(stop.WaypointTypeName);
        var activity = stop.Role == IntakeStopRole.Delivery ? WaypointActivity.Unload : WaypointActivity.Load;

        if (run.Alias(IntakeAliasKind.WaypointType, null, text) is { } alias && run.WaypointTypes.TryGetValue(alias.TargetId, out var aliased))
        {
            Resolve(field, aliased.Id, run.NameOf(aliased.Name), IntakeResolutionMethod.Alias);
            run.Result.UsedAliases.Add(alias);
        }
        else if (text.Length > 0 && run.WaypointTypes.Values.Where(t => NameMatches(t.Name.Texts.Values, text)).ToList() is { Count: 1 } byName)
        {
            Resolve(field, byName[0].Id, run.NameOf(byName[0].Name), IntakeResolutionMethod.Exact);
        }
        else
        {
            var fromPattern = run.OrderType?.DefaultWaypoints
                .OrderBy(w => w.Position)
                .Select(w => run.WaypointTypes.GetValueOrDefault(w.WaypointTypeId))
                .FirstOrDefault(w => w != null && w.Activity == activity);
            var withActivity = run.WaypointTypes.Values.Where(t => t.Activity == activity).ToList();
            var chosen = fromPattern ?? (withActivity.Count == 1 ? withActivity[0] : null);
            if (chosen != null)
            {
                Resolve(field, chosen.Id, run.NameOf(chosen.Name), IntakeResolutionMethod.Pattern);
            }
            else
            {
                Open(field, withActivity.Select(t => new IntakeCandidate { Id = t.Id, Name = run.NameOf(t.Name), Detail = t.Code }));
            }
        }

        ApplyPick(run, field, text, null, (id, _) => (run.NameOf(run.WaypointTypes[id].Name), null));
        return field.Status == IntakeResolutionStatus.Resolved ? field.TargetId : null;
    }

    /// <summary>The stop's own date, else the order date when the document gave one (design 72 "Dates").</summary>
    private static DateOnly? ResolveStopDate(Run run, int lineIndex, int stopIndex, OrderIntakeStopDto stop)
    {
        var field = run.Field(IntakeFieldPaths.Stop(lineIndex, stopIndex, IntakeFieldPaths.DateField));
        if (!string.IsNullOrWhiteSpace(stop.Date))
        {
            if (TryDate(stop.Date, out var date))
            {
                Resolve(field, null, date.ToString(IntakeConsts.DateFormat, CultureInfo.InvariantCulture), IntakeResolutionMethod.Given);
                return date;
            }

            Open(field, []);
            return null;
        }

        if (run.Result.OrderDate is { } orderDate)
        {
            Resolve(field, null, orderDate.ToString(IntakeConsts.DateFormat, CultureInfo.InvariantCulture), IntakeResolutionMethod.Default);
            return orderDate;
        }

        Open(field, []);
        return null;
    }

    /// <summary>A time is optional; only one that does not read is reported (and blocks: a clerk corrects it).</summary>
    private static TimeOnly? ResolveTime(Run run, string path, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (TimeOnly.TryParseExact(text.Trim(), ["HH:mm", "H:mm", "HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            return time;
        }

        Open(run.Field(path), []);
        return null;
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private static void Resolve(IntakeFieldResolution field, Guid? targetId, string? targetName, IntakeResolutionMethod method)
    {
        field.Status = IntakeResolutionStatus.Resolved;
        field.TargetId = targetId;
        field.TargetName = targetName;
        field.Method = method;
        field.Candidates.Clear();
    }

    private static void Open(IntakeFieldResolution field, IEnumerable<IntakeCandidate> candidates)
    {
        field.Candidates = candidates.GroupBy(c => c.Id).Select(g => g.First()).Take(IntakeConsts.MaxCandidates).ToList();
        foreach (var candidate in field.Candidates)
        {
            candidate.Detail = candidate.Detail is { Length: > IntakeConsts.MaxCandidateDetailLength } d ? d[..IntakeConsts.MaxCandidateDetailLength] : candidate.Detail;
        }

        field.Status = field.Candidates.Count > 0 ? IntakeResolutionStatus.Ambiguous : IntakeResolutionStatus.Unresolved;
        field.TargetId = null;
        field.TargetName = null;
        field.Method = null;
    }

    /// <summary>
    /// A clerk's pick wins over the rules (method User). A pick on a field the rules left Ambiguous or Unresolved is learned
    /// as an alias of the received text (design 72 "Review"), scoped to the customer for an address.
    /// </summary>
    private static void ApplyPick(Run run, IntakeFieldResolution field, string text, Guid? scopeRelationId, Func<Guid, IntakeAliasKind, (string Name, Guid? RelationId)> describe)
    {
        if (!run.Picks.TryGetValue(field.FieldPath, out var pick))
        {
            return;
        }

        var wasOpen = field.Status != IntakeResolutionStatus.Resolved;
        var (name, relationId) = describe(pick.Id, pick.Kind);
        Resolve(field, pick.Id, name, IntakeResolutionMethod.User);
        if (wasOpen && text.Length > 0)
        {
            run.Result.AliasesToLearn.Add(new IntakeAliasToLearn(pick.Kind, pick.Kind == IntakeAliasKind.Address ? scopeRelationId : null, text, pick.Id, relationId));
        }
    }

    private static bool NameMatches(IEnumerable<string> names, string normalized) => names.Any(n => IntakeTextNormalizer.Normalize(n) == normalized);

    private static bool TryDate(string text, out DateOnly date)
        => DateOnly.TryParseExact(text.Trim(), IntakeConsts.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static string Describe(MasterDataAddressReferenceDto a)
    {
        var street = string.IsNullOrEmpty(a.HouseNumber) ? a.Street : a.Street + " " + a.HouseNumber;
        return a.RelationName + ", " + street + ", " + a.PostalCode + " " + a.City;
    }

    /// <summary>The text an address alias matches: the parts as the document gave them, else the address as one text.</summary>
    public static string AddressText(OrderIntakeAddressDto? address)
    {
        if (address == null)
        {
            return string.Empty;
        }

        var parts = IntakeTextNormalizer.Join(address.Name, address.Street, address.HouseNumber, address.PostalCode, address.City, address.CountryCode);
        return parts.Length > 0 ? parts : IntakeTextNormalizer.Normalize(address.FullText);
    }

    /// <summary>The text a customer alias matches: the strongest thing the document says about the party, else the sender.</summary>
    public static string CustomerText(OrderIntakeCustomerDto? customer, string? senderAddress)
        => IntakeTextNormalizer.Normalize(Present(customer?.Name) ?? Present(customer?.EmailAddress) ?? Present(customer?.Code)
                                          ?? Present(customer?.VatNumber) ?? Present(customer?.ChamberOfCommerceNumber) ?? Present(senderAddress));

    private static string? Present(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool HasAny(params string?[] values) => values.Any(v => !string.IsNullOrWhiteSpace(v));

    private static List<Guid> Keys(IEnumerable<Guid> ids) => ids.Where(id => id != Guid.Empty).Distinct().ToList();

    private static BusinessException PickInvalid(string path)
        => new BusinessException(OrdersErrorCodes.IntakePickInvalid).WithData("field", path.Length > IntakeConsts.MaxFieldPathLength ? path[..IntakeConsts.MaxFieldPathLength] : path);

    /// <summary>The state of one resolver run.</summary>
    private sealed class Run
    {
        public Run(IntakeResolveRequest request, string culture)
        {
            Request = request;
            Culture = culture;
            CustomerText = OrderIntakeResolver.CustomerText(request.Payload.Customer, request.Settings.SenderAddress);
        }

        public IntakeResolveRequest Request { get; }

        public OrderIntakeDto Payload => Request.Payload;

        public string Culture { get; }

        public string CustomerText { get; }

        public IntakeResolveResult Result { get; } = new();

        public Dictionary<string, (IntakeAliasKind Kind, Guid Id)> Picks { get; } = new(StringComparer.Ordinal);

        public Dictionary<Guid, TransportOrderType> OrderTypes { get; set; } = new();

        public Dictionary<Guid, WaypointType> WaypointTypes { get; set; } = new();

        public TransportOrderType? OrderType { get; set; }

        public Dictionary<(IntakeAliasKind Kind, Guid? Scope, string Text), IntakeAlias> Aliases { get; } = new();

        public Dictionary<Guid, MasterDataReferenceDto> Relations { get; } = new();

        public Dictionary<Guid, MasterDataAddressReferenceDto> Addresses { get; } = new();

        public Dictionary<Guid, Guid> AddressRelations { get; } = new();

        public Dictionary<string, List<RelationAddressMatchDto>> AddressMatches { get; } = new(StringComparer.Ordinal);

        public Dictionary<MasterDataReferenceKind, Dictionary<Guid, MasterDataReferenceDto>> References { get; } = new();

        public Dictionary<MasterDataReferenceKind, Dictionary<string, List<ReferenceNameMatchDto>>> NameMatches { get; } = new();

        public IntakeFieldResolution Field(string path)
        {
            var field = new IntakeFieldResolution { FieldPath = path, Status = IntakeResolutionStatus.Unresolved };
            Result.Resolution.Fields.Add(field);
            return field;
        }

        public IntakeAlias? Alias(IntakeAliasKind kind, Guid? scope, string text)
            => text.Length == 0 ? null : Aliases.GetValueOrDefault((kind, scope, text));

        public MasterDataReferenceDto? ActiveRelation(Guid? id)
            => id is { } key && Relations.TryGetValue(key, out var relation) && !relation.IsArchived ? relation : null;

        public MasterDataReferenceDto? ActiveReference(MasterDataReferenceKind kind, Guid? id)
            => id is { } key && References.TryGetValue(kind, out var rows) && rows.TryGetValue(key, out var row) && !row.IsArchived ? row : null;

        public string NameOf(OpenTms.Platform.Localization.LocalizedName name) => name.Resolve(Culture);
    }
}

/// <summary>What the resolver resolves: the values (as received or as reviewed), the channel's settings and a clerk's picks.</summary>
public sealed record IntakeResolveRequest(OrderIntakeDto Payload, OrderIntakeChannelSettings Settings, IReadOnlyDictionary<string, Guid> Picks);

/// <summary>The outcome of one resolver run: the resolution to store and the ids the order is built from.</summary>
public sealed class IntakeResolveResult
{
    /// <summary>The methods that may lead to an automatically confirmed order (design 72).</summary>
    private static readonly HashSet<IntakeResolutionMethod> Trusted =
        [IntakeResolutionMethod.Given, IntakeResolutionMethod.Exact, IntakeResolutionMethod.Alias, IntakeResolutionMethod.Pattern, IntakeResolutionMethod.Default];

    public IntakeResolution Resolution { get; } = new();

    public Guid? CustomerRelationId { get; set; }

    /// <summary>The resolved customer's name, else the name as received.</summary>
    public string? CustomerName { get; set; }

    public Guid? TransportOrderTypeId { get; set; }

    /// <summary>The order date the document gave; null when it gave none.</summary>
    public DateOnly? OrderDate { get; set; }

    /// <summary>One per payload line, in order.</summary>
    public List<ResolvedIntakeLine> Lines { get; } = new();

    /// <summary>The aliases the run resolved a field with (their use counter goes up when the resolution is kept).</summary>
    public List<IntakeAlias> UsedAliases { get; } = new();

    /// <summary>The picks on fields the rules left open: learned as aliases when the order is created.</summary>
    public List<IntakeAliasToLearn> AliasesToLearn { get; } = new();

    /// <summary>Nothing blocks the order: every required field is resolved (product and temperature may be open).</summary>
    public bool IsComplete => Resolution.BlockingFieldPaths().Count == 0;

    /// <summary>Every field resolved, and only by Given, Exact, Alias, Pattern or Default (design 72 auto-confirm rule).</summary>
    public bool ResolvedByTrustedRules => Resolution.Fields.All(f => f.Status == IntakeResolutionStatus.Resolved && f.Method is { } m && Trusted.Contains(m));
}

public sealed class ResolvedIntakeLine
{
    public Guid? TransportUnitId { get; set; }

    public Guid? ProductId { get; set; }

    public Guid? TemperatureId { get; set; }

    public List<ResolvedIntakeStop> Stops { get; } = new();
}

public sealed class ResolvedIntakeStop
{
    public Guid? WaypointTypeId { get; set; }

    public Guid? RelationId { get; set; }

    public Guid? RelationAddressId { get; set; }

    public DateOnly? Date { get; set; }

    public TimeOnly? WindowFrom { get; set; }

    public TimeOnly? WindowTill { get; set; }
}

/// <summary>An alias to store: the received text of a field a clerk picked a row for.</summary>
public sealed record IntakeAliasToLearn(IntakeAliasKind Kind, Guid? ScopeRelationId, string Text, Guid TargetId, Guid? TargetRelationId);
