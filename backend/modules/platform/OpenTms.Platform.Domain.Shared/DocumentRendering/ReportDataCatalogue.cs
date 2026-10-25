using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using OpenTms.Platform.ReportDefinitions;

namespace OpenTms.Platform.DocumentRendering;

/// <summary>
/// The report data catalogue (design 55, "Report data catalogue"): per <see cref="ReportPurpose"/> the JSON a template
/// receives, in the OpenTMS envelope <c>{ CompanyLogo, Me, Entity, EntityType }</c> so FiLogic's existing templates
/// render unchanged. The catalogue is data: each OpenTMS payload class is defined once as a fragment
/// (<c>ReportDataCatalogue.Fragments.cs</c>) and composed per purpose; the field list, the sample payload and the
/// contract check all derive from those fragments.
/// </summary>
public static partial class ReportDataCatalogue
{
    private static readonly ConcurrentDictionary<ReportPurpose, PurposeModel> Models = new();

    /// <summary>
    /// The full flattened field list for the purpose: the envelope, then every field of <c>Me</c> and <c>Entity</c>,
    /// nested objects and array elements included, in payload order. Each call returns fresh sample nodes.
    /// </summary>
    public static IReadOnlyList<ReportDataField> FieldsFor(ReportPurpose purpose) =>
        ModelFor(purpose).Fields
            .Select(field => field with { Sample = field.Sample?.DeepClone() })
            .ToList();

    /// <summary>
    /// A complete sample payload for the purpose: every catalogue field present, collections with two elements
    /// (except where OpenTMS derived a collection from others, see the field descriptions), Dutch sample data.
    /// Used when a template has no sample data of its own. Each call returns a fresh object.
    /// </summary>
    public static JsonObject SampleFor(ReportPurpose purpose) => ModelFor(purpose).Sample.DeepClone().AsObject();

    /// <summary>
    /// The contract check: every place where <paramref name="payload"/> deviates from the purpose's catalogue
    /// (unknown fields, wrong JSON kinds, wrong date/time/GUID formats, an <c>EntityType</c> other than
    /// <see cref="EntityTypeOf"/>). Null values and missing fields are allowed: OpenTMS omitted or nulled what an entity
    /// did not have. An empty list means the payload conforms.
    /// </summary>
    public static IReadOnlyList<ReportDataDeviation> Validate(ReportPurpose purpose, JsonNode? payload)
    {
        var model = ModelFor(purpose);
        var deviations = new List<ReportDataDeviation>();
        if (payload is not JsonObject root)
        {
            deviations.Add(new ReportDataDeviation("$", $"expected a JSON object, got {KindOf(payload)}"));
            return deviations;
        }

        ValidateObject(root, string.Empty, string.Empty, model.Index, deviations);

        if (root["EntityType"] is JsonValue entityType
            && entityType.GetValueKind() == JsonValueKind.String
            && entityType.GetValue<string>() != EntityTypeOf(purpose))
        {
            deviations.Add(new ReportDataDeviation(
                "EntityType", $"expected \"{EntityTypeOf(purpose)}\", got \"{entityType.GetValue<string>()}\""));
        }

        return deviations;
    }

    /// <summary>
    /// The entity type name OpenTMS put in <c>EntityType</c> (the CLR name of its payload class). The transport-unit
    /// balance keeps OpenTMS's extra wrapper, whose generic type name OpenTMS sent as is.
    /// </summary>
    public static string EntityTypeOf(ReportPurpose purpose) => purpose switch
    {
        ReportPurpose.TransportOrderConfirmation => "TransportOrder",
        ReportPurpose.TransportFile => "TransportFile",
        ReportPurpose.Invoice => "Invoice",
        ReportPurpose.PurchaseOrder => "PurchaseOrder",
        ReportPurpose.TransportUnitBalance => "GetSingleOutput`1",
        ReportPurpose.ConsignmentNote => "ConsignmentNote",
        _ => purpose.ToString()
    };

    private static PurposeModel ModelFor(ReportPurpose purpose) => Models.GetOrAdd(purpose, BuildModel);

    private static PurposeModel BuildModel(ReportPurpose purpose)
    {
        var envelope = Fragments.EnvelopeFor(purpose, EntityTypeOf(purpose));
        var sample = SampleBuilder.BuildObject(envelope, null);
        var fields = new List<ReportDataField>();
        Flatten(envelope, string.Empty, sample, fields);
        var index = fields.ToDictionary(field => field.Path, field => field.Type, StringComparer.Ordinal);
        return new PurposeModel(sample, fields, index);
    }

    private static void Flatten(CatalogueFragment fragment, string prefix, JsonObject? sample, List<ReportDataField> into)
    {
        foreach (var member in fragment.Members)
        {
            var path = Join(prefix, member.Name);
            var node = sample?[member.Name];
            switch (member.Type)
            {
                case ReportDataFieldType.Object:
                    into.Add(new ReportDataField(path, member.Type, member.Description, null));
                    if (member.Fragment != null)
                    {
                        Flatten(member.Fragment, path, node as JsonObject, into);
                    }
                    break;
                case ReportDataFieldType.Array:
                    into.Add(new ReportDataField(path, member.Type, member.Description, null));
                    var first = (node as JsonArray)?.FirstOrDefault();
                    if (member.Fragment != null)
                    {
                        Flatten(member.Fragment, path + "[]", first as JsonObject, into);
                    }
                    else
                    {
                        into.Add(new ReportDataField(
                            path + "[]", member.ElementType!.Value, "One element of " + member.Name + ".", first?.DeepClone()));
                    }
                    break;
                default:
                    into.Add(new ReportDataField(path, member.Type, member.Description, node?.DeepClone()));
                    break;
            }
        }
    }

    private static string Join(string prefix, string name) => prefix.Length == 0 ? name : prefix + "." + name;

    private static void ValidateObject(
        JsonObject obj, string cataloguePrefix, string concretePrefix,
        IReadOnlyDictionary<string, ReportDataFieldType> index, List<ReportDataDeviation> deviations)
    {
        foreach (var (name, node) in obj)
        {
            var cataloguePath = Join(cataloguePrefix, name);
            var concretePath = Join(concretePrefix, name);
            if (!index.TryGetValue(cataloguePath, out var type))
            {
                deviations.Add(new ReportDataDeviation(concretePath, "unknown field: not in the catalogue"));
                continue;
            }

            ValidateValue(node, type, cataloguePath, concretePath, index, deviations);
        }
    }

    private static void ValidateValue(
        JsonNode? node, ReportDataFieldType type, string cataloguePath, string concretePath,
        IReadOnlyDictionary<string, ReportDataFieldType> index, List<ReportDataDeviation> deviations)
    {
        if (node is null)
        {
            return;
        }

        switch (type)
        {
            case ReportDataFieldType.Object:
                if (node is JsonObject obj)
                {
                    ValidateObject(obj, cataloguePath, concretePath, index, deviations);
                }
                else
                {
                    deviations.Add(WrongKind(concretePath, type, node));
                }
                return;
            case ReportDataFieldType.Array:
                if (node is not JsonArray array)
                {
                    deviations.Add(WrongKind(concretePath, type, node));
                    return;
                }

                var elementPath = cataloguePath + "[]";
                var hasPlainElements = index.TryGetValue(elementPath, out var elementType);
                for (var i = 0; i < array.Count; i++)
                {
                    var element = array[i];
                    var elementConcretePath = concretePath + "[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                    if (hasPlainElements)
                    {
                        ValidateValue(element, elementType, elementPath, elementConcretePath, index, deviations);
                    }
                    else if (element is JsonObject elementObject)
                    {
                        ValidateObject(elementObject, elementPath, elementConcretePath, index, deviations);
                    }
                    else if (element is not null)
                    {
                        deviations.Add(WrongKind(elementConcretePath, ReportDataFieldType.Object, element));
                    }
                }
                return;
        }

        if (node is not JsonValue value)
        {
            deviations.Add(WrongKind(concretePath, type, node));
            return;
        }

        var kind = value.GetValueKind();
        var ok = type switch
        {
            ReportDataFieldType.String => kind == JsonValueKind.String,
            ReportDataFieldType.Integer or ReportDataFieldType.Enum =>
                kind == JsonValueKind.Number && long.TryParse(value.ToJsonString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _),
            ReportDataFieldType.Decimal => kind == JsonValueKind.Number,
            ReportDataFieldType.Boolean => kind is JsonValueKind.True or JsonValueKind.False,
            ReportDataFieldType.Date => kind == JsonValueKind.String && IsDate(value.GetValue<string>()),
            ReportDataFieldType.DateTime => kind == JsonValueKind.String && IsDateTime(value.GetValue<string>()),
            ReportDataFieldType.TimeSpan => kind == JsonValueKind.String && IsTimeSpan(value.GetValue<string>()),
            ReportDataFieldType.Guid => kind == JsonValueKind.String && System.Guid.TryParseExact(value.GetValue<string>(), "D", out _),
            _ => false
        };

        if (!ok)
        {
            deviations.Add(WrongKind(concretePath, type, node));
        }
    }

    private static readonly Regex IsoDateTime = new(
        @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|[+-]\d{2}:\d{2})?$", RegexOptions.CultureInvariant);

    // TimeSpan.ToString() as Newtonsoft wrote it: [-][d.]hh:mm:ss[.fffffff]; the "c" parser alone would also accept "2:10".
    private static readonly Regex InvariantTimeSpan = new(
        @"^-?(\d+\.)?\d{2}:\d{2}:\d{2}(\.\d{1,7})?$", RegexOptions.CultureInvariant);

    private static bool IsTimeSpan(string text) =>
        InvariantTimeSpan.IsMatch(text) && System.TimeSpan.TryParseExact(text, "c", CultureInfo.InvariantCulture, out _);

    private static bool IsDate(string text) =>
        System.DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    private static bool IsDateTime(string text) =>
        IsoDateTime.IsMatch(text)
        && System.DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _);

    private static ReportDataDeviation WrongKind(string path, ReportDataFieldType expected, JsonNode? node) =>
        new(path, $"expected {Expected(expected)}, got {KindOf(node)}");

    private static string Expected(ReportDataFieldType type) => type switch
    {
        ReportDataFieldType.String => "a string",
        ReportDataFieldType.Integer => "an integer",
        ReportDataFieldType.Decimal => "a number",
        ReportDataFieldType.Boolean => "a boolean",
        ReportDataFieldType.Date => "a date string yyyy-MM-dd",
        ReportDataFieldType.DateTime => "an ISO 8601 date-time string",
        ReportDataFieldType.TimeSpan => "a duration string hh:mm:ss",
        ReportDataFieldType.Guid => "a GUID string",
        ReportDataFieldType.Enum => "an enum integer",
        ReportDataFieldType.Object => "an object",
        ReportDataFieldType.Array => "an array",
        _ => type.ToString()
    };

    private static string KindOf(JsonNode? node) => node switch
    {
        null => "null",
        JsonObject => "an object",
        JsonArray => "an array",
        JsonValue value => value.GetValueKind() switch
        {
            JsonValueKind.String => "the string " + value.ToJsonString(),
            JsonValueKind.Number => "the number " + value.ToJsonString(),
            JsonValueKind.True or JsonValueKind.False => "a boolean",
            var other => other.ToString()
        },
        _ => node.GetType().Name
    };

    private sealed record PurposeModel(
        JsonObject Sample,
        IReadOnlyList<ReportDataField> Fields,
        IReadOnlyDictionary<string, ReportDataFieldType> Index);

    /// <summary>One OpenTMS payload class: its members in payload order.</summary>
    internal sealed class CatalogueFragment(string name, IEnumerable<CatalogueMember> members)
    {
        public string Name { get; } = name;

        public IReadOnlyList<CatalogueMember> Members { get; } = members.ToList();
    }

    /// <summary>
    /// One member of a fragment. Objects and arrays of objects point at their fragment; an object without fragment is
    /// a recursion stop (catalogued without children). <see cref="Derive"/> computes the sample from the already built
    /// siblings, as OpenTMS's get-only properties computed it.
    /// </summary>
    internal sealed record CatalogueMember(string Name, ReportDataFieldType Type, string Description)
    {
        /// <summary>Default sample value: string, int, long, double, bool or null.</summary>
        public object? Sample { get; init; }

        public CatalogueFragment? Fragment { get; init; }

        /// <summary>Element type of an array of plain values.</summary>
        public ReportDataFieldType? ElementType { get; init; }

        /// <summary>Sample elements of an array of plain values.</summary>
        public IReadOnlyList<object?>? Elements { get; init; }

        /// <summary>Sample overrides for an object member.</summary>
        public SampleSet? Overrides { get; init; }

        /// <summary>Sample overrides per element of an array of objects; the count is the element count (default 2).</summary>
        public IReadOnlyList<SampleSet?>? Items { get; init; }

        public Func<JsonObject, JsonNode?>? Derive { get; init; }

        public CatalogueMember From(Func<JsonObject, JsonNode?> derive) => this with { Derive = derive };

        public CatalogueMember With(SampleSet overrides) => this with { Overrides = overrides };

        public CatalogueMember WithItems(params SampleSet?[] items) => this with { Items = items };
    }

    /// <summary>
    /// Sample values for a fragment's members by name: a plain value, a nested <see cref="SampleSet"/> for an object,
    /// a <see cref="SampleSet"/> array for an array of objects, or an object array for an array of plain values.
    /// </summary>
    internal sealed class SampleSet : Dictionary<string, object?>
    {
        public SampleSet() : base(StringComparer.Ordinal)
        {
        }

        public static SampleSet? Merge(SampleSet? inner, SampleSet? outer)
        {
            if (inner is null) return outer;
            if (outer is null) return inner;
            var merged = new SampleSet();
            foreach (var (key, value) in inner) merged[key] = value;
            foreach (var (key, value) in outer)
            {
                merged[key] = value is SampleSet outerSet && merged.TryGetValue(key, out var existing) && existing is SampleSet innerSet
                    ? Merge(innerSet, outerSet)
                    : value;
            }
            return merged;
        }
    }

    private static class SampleBuilder
    {
        public static JsonObject BuildObject(CatalogueFragment fragment, SampleSet? overrides)
        {
            var obj = new JsonObject();
            var pending = new List<CatalogueMember>();
            foreach (var member in fragment.Members)
            {
                object? overrideValue = null;
                var hasOverride = overrides != null && overrides.TryGetValue(member.Name, out overrideValue);
                if (!hasOverride && (member.Derive != null || (member.Type == ReportDataFieldType.Guid && member.Sample is null)))
                {
                    obj[member.Name] = null;
                    pending.Add(member);
                    continue;
                }

                obj[member.Name] = BuildMember(member, hasOverride, overrideValue);
            }

            foreach (var member in pending)
            {
                obj[member.Name] = member.Derive != null ? member.Derive(obj) : StableGuid(fragment.Name, member.Name, obj);
            }

            return obj;
        }

        private static JsonNode? BuildMember(CatalogueMember member, bool hasOverride, object? overrideValue)
        {
            switch (member.Type)
            {
                case ReportDataFieldType.Object:
                    if (member.Fragment is null || (hasOverride && overrideValue is null))
                    {
                        return hasOverride ? ToNode(overrideValue) : null;
                    }
                    return BuildObject(member.Fragment, SampleSet.Merge(member.Overrides, overrideValue as SampleSet));
                case ReportDataFieldType.Array:
                    if (hasOverride && overrideValue is null)
                    {
                        return null;
                    }
                    var array = new JsonArray();
                    if (member.Fragment != null)
                    {
                        var outer = overrideValue as IReadOnlyList<SampleSet?>;
                        var count = outer?.Count ?? member.Items?.Count ?? 2;
                        for (var i = 0; i < count; i++)
                        {
                            var inner = member.Items != null && i < member.Items.Count ? member.Items[i] : null;
                            var item = outer != null && i < outer.Count ? outer[i] : null;
                            array.Add(BuildObject(member.Fragment, SampleSet.Merge(inner, item)));
                        }
                    }
                    else
                    {
                        var elements = hasOverride ? (IReadOnlyList<object?>)overrideValue! : member.Elements ?? [];
                        foreach (var element in elements) array.Add(ToNode(element));
                    }
                    return array;
                default:
                    return ToNode(hasOverride ? overrideValue : member.Sample);
            }
        }

        public static JsonNode? ToNode(object? value) => value switch
        {
            null => null,
            JsonNode node => node.DeepClone(),
            string text => JsonValue.Create(text),
            int number => JsonValue.Create(number),
            long number => JsonValue.Create(number),
            double number => JsonValue.Create(number),
            bool flag => JsonValue.Create(flag),
            _ => throw new InvalidOperationException($"Unsupported sample value type {value.GetType().Name}.")
        };

        /// <summary>
        /// A GUID derived from the fragment, the member and the plain sibling values: the same sample entity gets the
        /// same id wherever it appears, different entities get different ids.
        /// </summary>
        private static JsonNode StableGuid(string fragment, string member, JsonObject siblings)
        {
            var text = new StringBuilder(fragment).Append('.').Append(member);
            foreach (var (name, node) in siblings)
            {
                if (node is JsonValue) text.Append('|').Append(name).Append('=').Append(node.ToJsonString());
            }
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())).AsSpan(0, 16).ToArray();
            bytes[7] = (byte)((bytes[7] & 0x0F) | 0x40);
            bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
            return JsonValue.Create(new Guid(bytes).ToString("D"));
        }
    }
}
