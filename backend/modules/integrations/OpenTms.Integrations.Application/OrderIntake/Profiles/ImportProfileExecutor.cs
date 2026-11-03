using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using OpenTms.Integrations.OrderIntake.Extraction;
using OpenTms.Orders.Intake;
using Volo.Abp.DependencyInjection;

namespace OpenTms.Integrations.OrderIntake.Profiles;

/// <summary>One intake a profile run read, with the source of every field (the provenance of the trace).</summary>
public sealed class ProfileRunIntake
{
    public OrderIntakeDto Payload { get; init; } = new();

    public Dictionary<string, SourceLocation> Provenance { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>A rule that could not be applied, or a validation rule an intake breaks. <see cref="FieldPath"/> is the concrete path when known.</summary>
public sealed record ProfileRunError(string? FieldPath, string Code);

public sealed class ProfileRunResult
{
    public List<ProfileRunIntake> Intakes { get; } = new();

    public List<ProfileRunError> Errors { get; } = new();

    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Runs the rules of an import profile on an extracted document (design 72 §4 "Rules"): deterministic, no I/O, no clock.
/// The document splits into orders (document, rowGroup, xpath, textSplit), each order into lines (single, row, xpath,
/// textBlock), each line into stops (inherit, xpath; without stop rules one Pickup and one Delivery that read from the
/// line). Every field rule reads its source in its scope (column, cell, xpath, label, regex with a 100 ms timeout,
/// constant), applies its transforms in order and writes the canonical path, recording where it read. The result is
/// then validated (<see cref="ImportProfileValidator"/>).
/// </summary>
public class ImportProfileExecutor : ITransientDependency
{
    public static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>A run produces at most this many intakes (a spreadsheet of 5,000 rows of one order each).</summary>
    public const int MaxIntakes = 5000;

    public ProfileRunResult Run(ImportProfileRules rules, ExtractedDocument document, bool validate = true)
    {
        var result = new ProfileRunResult();
        try
        {
            var context = new RunContext(rules, document, result);
            foreach (var orderScope in context.OrderScopes().Take(MaxIntakes))
            {
                result.Intakes.Add(context.ReadOrder(orderScope));
            }
        }
        catch (RegexMatchTimeoutException)
        {
            result.Errors.Add(new ProfileRunError(null, "regexTimeout"));
        }
        catch (RuleException ex)
        {
            result.Errors.Add(new ProfileRunError(ex.FieldPath, ex.Code));
        }

        if (validate && result.Errors.Count == 0)
        {
            result.Errors.AddRange(ImportProfileValidator.Validate(result.Intakes.Select(i => i.Payload).ToList()));
        }

        return result;
    }

    private sealed class RuleException : Exception
    {
        public string Code { get; }

        public string? FieldPath { get; }

        public RuleException(string code, string? fieldPath = null)
            : base(code)
        {
            Code = code;
            FieldPath = fieldPath;
        }
    }

    /// <summary>A region of the document a rule reads in.</summary>
    private sealed class Scope
    {
        /// <summary>Tabular: the rows (the first is the scope row of column sources).</summary>
        public List<ExtractedRow>? Rows { get; init; }

        /// <summary>Text: (line number, text).</summary>
        public List<(int Number, string Text)>? Lines { get; init; }

        /// <summary>XML: the node.</summary>
        public XElement? Node { get; init; }

        /// <summary>Text: the lines before the first split, read when an order field is not found in the block.</summary>
        public Scope? Preamble { get; init; }
    }

    private sealed class RunContext
    {
        private readonly ImportProfileRules _rules;
        private readonly ExtractedDocument _document;
        private readonly ProfileRunResult _result;
        private readonly ExtractedSheet? _sheet;
        private readonly Dictionary<string, int> _headers = new(StringComparer.OrdinalIgnoreCase);
        private readonly int _headerRow;
        private readonly XmlNamespaceManager? _namespaces;
        private readonly Dictionary<string, int> _xmlLineNumbers = new(StringComparer.Ordinal);

        public RunContext(ImportProfileRules rules, ExtractedDocument document, ProfileRunResult result)
        {
            _rules = rules;
            _document = document;
            _result = result;
            if (document.Sheets.Count > 0)
            {
                _sheet = document.FindSheet(rules.Sheet) ?? throw new RuleException("sheetNotFound");
                _headerRow = rules.HeaderRow ?? _sheet.Rows.FirstOrDefault()?.RowNumber ?? 1;
                var header = _sheet.Rows.FirstOrDefault(r => r.RowNumber == _headerRow);
                foreach (var cell in header?.Cells ?? [])
                {
                    _headers.TryAdd(NormalizeHeader(cell.Text), cell.Column);
                }
            }

            if (document.Xml != null)
            {
                _namespaces = new XmlNamespaceManager(new NameTable());
                foreach (var (prefix, uri) in rules.Namespaces ?? new Dictionary<string, string>())
                {
                    if (!string.IsNullOrWhiteSpace(prefix) && !string.IsNullOrWhiteSpace(uri))
                    {
                        _namespaces.AddNamespace(prefix.Trim(), uri.Trim());
                    }
                }

                for (var i = 0; i < document.Lines.Count; i++)
                {
                    var colon = document.Lines[i].IndexOf(": ", StringComparison.Ordinal);
                    var bracket = document.Lines[i].IndexOf(" [", StringComparison.Ordinal);
                    var end = new[] { colon, bracket }.Where(p => p > 0).DefaultIfEmpty(document.Lines[i].Length).Min();
                    _xmlLineNumbers.TryAdd(document.Lines[i][..end], i + 1);
                }
            }
        }

        /* ---------- Scopes ---------- */

        public IEnumerable<Scope> OrderScopes()
        {
            var scope = _rules.Orders.Scope;
            switch (scope.Kind)
            {
                case OrderScopeKind.Document:
                    yield return WholeDocument();
                    break;
                case OrderScopeKind.RowGroup:
                    foreach (var group in RowGroups(scope.GroupBy ?? []))
                    {
                        yield return group;
                    }

                    break;
                case OrderScopeKind.Xpath:
                    RequireXml("orders.scope");
                    foreach (var node in SelectElements(_document.Xml!.Root!, scope.Value, "orders.scope", absolute: true))
                    {
                        yield return new Scope { Node = node };
                    }

                    break;
                case OrderScopeKind.TextSplit:
                    foreach (var block in TextBlocks(TextLines(WholeDocument()), scope.Value, "orders.scope", keepPreamble: true))
                    {
                        yield return block;
                    }

                    break;
                default:
                    throw new RuleException("unknownScope", "orders.scope");
            }
        }

        private Scope WholeDocument()
        {
            if (_sheet != null)
            {
                return new Scope { Rows = DataRows().ToList() };
            }

            if (_document.Xml?.Root != null)
            {
                return new Scope { Node = _document.Xml.Root };
            }

            return new Scope { Lines = _document.Lines.Select((text, i) => (i + 1, text)).Where(l => l.text != ExtractedDocument.PageBreak).ToList() };
        }

        private IEnumerable<ExtractedRow> DataRows() => _sheet!.Rows.Where(r => r.RowNumber > _headerRow);

        private IEnumerable<Scope> RowGroups(List<string> groupBy)
        {
            if (_sheet == null)
            {
                throw new RuleException("rowGroupNeedsTable", "orders.scope");
            }

            if (groupBy.Count == 0)
            {
                throw new RuleException("rowGroupNeedsGroupBy", "orders.scope");
            }

            var columns = groupBy.Select(h => Column(h, "orders.scope.groupBy")).ToList();
            var groups = new List<(string Key, List<ExtractedRow> Rows)>();
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var row in DataRows())
            {
                var key = string.Join("\u001F", columns.Select(c => row.Cells.FirstOrDefault(x => x.Column == c)?.Text ?? string.Empty));
                if (key.Replace("\u001F", string.Empty, StringComparison.Ordinal).Length == 0)
                {
                    /* A continuation row without the group's key belongs to the order above it. */
                    if (groups.Count > 0)
                    {
                        groups[^1].Rows.Add(row);
                    }

                    continue;
                }

                if (index.TryGetValue(key, out var position))
                {
                    groups[position].Rows.Add(row);
                }
                else
                {
                    index[key] = groups.Count;
                    groups.Add((key, new List<ExtractedRow> { row }));
                }
            }

            return groups.Select(g => new Scope { Rows = g.Rows });
        }

        private IEnumerable<Scope> TextBlocks(List<(int Number, string Text)> lines, string? pattern, string rulePath, bool keepPreamble)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                throw new RuleException("patternRequired", rulePath);
            }

            var regex = CreateRegex(pattern, rulePath);
            var preamble = new List<(int, string)>();
            List<(int, string)>? current = null;
            var blocks = new List<List<(int, string)>>();
            foreach (var line in lines)
            {
                if (regex.IsMatch(line.Text))
                {
                    current = new List<(int, string)> { line };
                    blocks.Add(current);
                }
                else if (current != null)
                {
                    current.Add(line);
                }
                else
                {
                    preamble.Add(line);
                }
            }

            var preambleScope = keepPreamble && preamble.Count > 0 ? new Scope { Lines = preamble } : null;
            return blocks.Select(b => new Scope { Lines = b, Preamble = preambleScope });
        }

        private List<Scope> LineScopes(Scope order)
        {
            var scope = _rules.Lines.Scope;
            switch (scope.Kind)
            {
                case LineScopeKind.Single:
                    return [order];
                case LineScopeKind.Row:
                    if (order.Rows != null)
                    {
                        return order.Rows.Select(r => new Scope { Rows = [r] }).ToList();
                    }

                    if (order.Node != null)
                    {
                        throw new RuleException("rowNeedsTableOrText", "lines.scope");
                    }

                    var regex = string.IsNullOrWhiteSpace(scope.Value) ? null : CreateRegex(scope.Value, "lines.scope");
                    return TextLines(order).Where(l => regex == null || regex.IsMatch(l.Text)).Select(l => new Scope { Lines = [l] }).ToList();
                case LineScopeKind.Xpath:
                    if (order.Node == null)
                    {
                        throw new RuleException("xpathNeedsXml", "lines.scope");
                    }

                    return SelectElements(order.Node, scope.Value, "lines.scope", absolute: false).Select(n => new Scope { Node = n }).ToList();
                case LineScopeKind.TextBlock:
                    if (order.Node != null)
                    {
                        throw new RuleException("textBlockNeedsText", "lines.scope");
                    }

                    return TextBlocks(TextLines(order), scope.Value, "lines.scope", keepPreamble: false).ToList();
                default:
                    throw new RuleException("unknownScope", "lines.scope");
            }
        }

        private List<(IntakeStopRole Role, Scope Scope)> StopScopes(Scope line)
        {
            var rules = _rules.Stops.Count > 0
                ? _rules.Stops
                : [new StopRule { Role = IntakeStopRole.Pickup }, new StopRule { Role = IntakeStopRole.Delivery }];
            var result = new List<(IntakeStopRole, Scope)>();
            foreach (var rule in rules)
            {
                if (rule.Scope.Kind == StopScopeKind.Xpath)
                {
                    if (line.Node == null)
                    {
                        throw new RuleException("xpathNeedsXml", "stops.scope");
                    }

                    result.AddRange(SelectElements(line.Node, rule.Scope.Value, "stops.scope", absolute: false).Select(n => (rule.Role, new Scope { Node = n })));
                }
                else
                {
                    result.Add((rule.Role, line));
                }
            }

            return result;
        }

        /* ---------- Reading ---------- */

        public ProfileRunIntake ReadOrder(Scope order)
        {
            var intake = new ProfileRunIntake();
            var lines = LineScopes(order);
            var stops = new List<List<(IntakeStopRole Role, Scope Scope)>>();
            for (var l = 0; l < lines.Count; l++)
            {
                var stopScopes = StopScopes(lines[l]);
                stops.Add(stopScopes);
                intake.Payload.Lines.Add(new OrderIntakeLineDto
                {
                    Stops = stopScopes.Select(s => new OrderIntakeStopDto { Role = s.Role }).ToList()
                });
            }

            foreach (var field in _rules.Fields)
            {
                var target = TargetParser.Parse(field.Target);
                switch (target.Level)
                {
                    case TargetLevel.Order:
                        ReadField(intake, field, order, target.ReferenceType != null ? null : "order." + target.Leaf, target, allowPreamble: true);
                        break;
                    case TargetLevel.Line:
                        for (var l = 0; l < lines.Count; l++)
                        {
                            ReadField(intake, field, lines[l], $"lines[{l}].{target.Leaf}", target, allowPreamble: false);
                        }

                        break;
                    case TargetLevel.Stop:
                        for (var l = 0; l < lines.Count; l++)
                        {
                            for (var s = 0; s < stops[l].Count; s++)
                            {
                                if (stops[l][s].Role == target.Role)
                                {
                                    ReadField(intake, field, stops[l][s].Scope, $"lines[{l}].stops[{s}].{target.Leaf}", target, allowPreamble: false);
                                }
                            }
                        }

                        break;
                    default:
                        _result.Errors.Add(new ProfileRunError(field.Target, IntakeValuePaths.UnknownPath));
                        break;
                }
            }

            return intake;
        }

        private void ReadField(ProfileRunIntake intake, FieldRule field, Scope scope, string? path, Target target, bool allowPreamble)
        {
            var values = Read(field.Source, scope, field.Target);
            if (values.Count == 0 && allowPreamble && scope.Preamble != null)
            {
                values = Read(field.Source, scope.Preamble, field.Target);
            }

            if (values.Count == 0)
            {
                return;
            }

            var occurrence = field.Source.Occurrence ?? 1;
            if (occurrence > 0)
            {
                if (values.Count < occurrence)
                {
                    return;
                }

                values = [values[occurrence - 1]];
            }

            var texts = values.Select(v => v.Text).ToList();
            foreach (var transform in field.Transforms)
            {
                var (transformed, error) = Transform(texts, transform);
                if (error != null)
                {
                    _result.Errors.Add(new ProfileRunError(path ?? field.Target, error));
                    return;
                }

                texts = transformed;
            }

            var text = texts.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            if (target.ReferenceType != null)
            {
                var references = intake.Payload.References;
                var index = references.FindIndex(r => string.Equals(r.Type, target.ReferenceType, StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                {
                    references.Add(new OrderIntakeReferenceDto { Type = target.ReferenceType });
                    index = references.Count - 1;
                }

                path = $"order.references[{index}].value";
            }

            var setError = IntakeValuePaths.TrySet(intake.Payload, path!, text);
            if (setError != null)
            {
                _result.Errors.Add(new ProfileRunError(path, setError));
                return;
            }

            var location = values[0].Location;
            intake.Provenance[path!] = new SourceLocation { Kind = location.Kind, Ref = location.Ref, Snippet = Snippet(values[0].Raw) };
        }

        private List<(string Text, string Raw, SourceLocation Location)> Read(FieldSource source, Scope scope, string target)
        {
            var result = new List<(string, string, SourceLocation)>();
            switch (source.Kind)
            {
                case FieldSourceKind.Constant:
                    result.Add((source.Value, source.Value, new SourceLocation { Kind = SourceLocationKind.Constant, Ref = "constant" }));
                    break;
                case FieldSourceKind.Column:
                {
                    if (scope.Rows == null)
                    {
                        throw new RuleException("columnNeedsTable", target);
                    }

                    var column = Column(source.Value, target);
                    foreach (var row in scope.Rows)
                    {
                        var cell = row.Cells.FirstOrDefault(c => c.Column == column);
                        if (cell != null && cell.Text.Length > 0)
                        {
                            result.Add((cell.Text, cell.Text, new SourceLocation { Kind = SourceLocationKind.Cell, Ref = cell.Reference }));
                        }
                    }

                    break;
                }
                case FieldSourceKind.Cell:
                {
                    if (_sheet == null)
                    {
                        throw new RuleException("cellNeedsTable", target);
                    }

                    if (!CellReference.TryParse(source.Value, out _, out _))
                    {
                        throw new RuleException("invalidCellReference", target);
                    }

                    var cell = _document.FindCell(_rules.Sheet, source.Value);
                    if (cell != null && cell.Text.Length > 0)
                    {
                        result.Add((cell.Text, cell.Text, new SourceLocation { Kind = SourceLocationKind.Cell, Ref = cell.Reference }));
                    }

                    break;
                }
                case FieldSourceKind.Xpath:
                {
                    if (scope.Node == null)
                    {
                        throw new RuleException("xpathNeedsXml", target);
                    }

                    result.AddRange(EvaluateXpath(scope.Node, source.Value, target));
                    break;
                }
                case FieldSourceKind.Label:
                    result.AddRange(ReadLabel(TextLines(scope), source.Value, target));
                    break;
                case FieldSourceKind.Regex:
                    result.AddRange(ReadRegex(TextLines(scope), source.Value, source.Group ?? 1, target));
                    break;
                default:
                    throw new RuleException("unknownSource", target);
            }

            return result;
        }

        private IEnumerable<(string, string, SourceLocation)> ReadLabel(List<(int Number, string Text)> lines, string label, string target)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                throw new RuleException("labelRequired", target);
            }

            var trimmed = label.Trim();
            for (var i = 0; i < lines.Count; i++)
            {
                var (number, text) = lines[i];
                var position = text.IndexOf(trimmed, StringComparison.OrdinalIgnoreCase);
                if (position < 0)
                {
                    continue;
                }

                var rest = text[(position + trimmed.Length)..].TrimStart(' ', ':', '\t', '=', '-');
                var lineNumber = number;
                if (rest.Length == 0)
                {
                    if (i + 1 >= lines.Count)
                    {
                        continue;
                    }

                    (lineNumber, rest) = (lines[i + 1].Number, lines[i + 1].Text);
                }

                var end = rest.IndexOf("  ", StringComparison.Ordinal);
                var value = (end >= 0 ? rest[..end] : rest).Trim();
                if (value.Length > 0)
                {
                    yield return (value, value, LineLocation(lineNumber));
                }
            }
        }

        private IEnumerable<(string, string, SourceLocation)> ReadRegex(List<(int Number, string Text)> lines, string pattern, int group, string target)
        {
            var regex = CreateRegex(pattern, target);
            var builder = new StringBuilder();
            var starts = new List<(int Offset, int Number)>();
            foreach (var (number, text) in lines)
            {
                starts.Add((builder.Length, number));
                builder.Append(text).Append('\n');
            }

            var whole = builder.ToString();
            var effectiveGroup = regex.GetGroupNumbers().Length > 1 ? group : 0;
            if (!regex.GetGroupNumbers().Contains(effectiveGroup))
            {
                throw new RuleException("invalidRegexGroup", target);
            }

            foreach (Match match in regex.Matches(whole))
            {
                var captured = match.Groups[effectiveGroup];
                if (!captured.Success || captured.Value.Trim().Length == 0)
                {
                    continue;
                }

                var lineNumber = starts.LastOrDefault(s => s.Offset <= captured.Index).Number;
                yield return (captured.Value.Trim(), captured.Value.Trim(), LineLocation(lineNumber));
            }
        }

        private IEnumerable<(string, string, SourceLocation)> EvaluateXpath(XElement node, string expression, string target)
        {
            object evaluated;
            try
            {
                evaluated = node.CreateNavigator().Evaluate(expression, _namespaces!);
            }
            catch (XPathException)
            {
                throw new RuleException("invalidXpath", target);
            }

            if (evaluated is XPathNodeIterator iterator)
            {
                var results = new List<(string, string, SourceLocation)>();
                while (iterator.MoveNext())
                {
                    var current = iterator.Current!;
                    var value = TextNormalizer.NormalizeLine(current.Value);
                    if (value.Length == 0)
                    {
                        continue;
                    }

                    var path = current.UnderlyingObject switch
                    {
                        XElement element => XmlPaths.PositionalPath(element),
                        XAttribute attribute when attribute.Parent != null => XmlPaths.PositionalPath(attribute.Parent) + "/@" + attribute.Name.LocalName,
                        XText text when text.Parent != null => XmlPaths.PositionalPath(text.Parent),
                        _ => XmlPaths.PositionalPath(node) + "/" + expression
                    };
                    results.Add((value, value, new SourceLocation { Kind = SourceLocationKind.Path, Ref = Cut(path, OrderIntakeConsts.MaxSourceRefLength) }));
                }

                return results;
            }

            var scalar = evaluated switch
            {
                double d when double.IsNaN(d) => string.Empty,
                double d => d.ToString(CultureInfo.InvariantCulture),
                bool b => b ? "true" : "false",
                _ => Convert.ToString(evaluated, CultureInfo.InvariantCulture) ?? string.Empty
            };
            scalar = TextNormalizer.NormalizeLine(scalar);
            return scalar.Length == 0
                ? []
                : [(scalar, scalar, new SourceLocation { Kind = SourceLocationKind.Path, Ref = Cut(XmlPaths.PositionalPath(node) + "/" + expression, OrderIntakeConsts.MaxSourceRefLength) })];
        }

        private IEnumerable<XElement> SelectElements(XElement context, string? expression, string rulePath, bool absolute)
        {
            if (string.IsNullOrWhiteSpace(expression))
            {
                throw new RuleException("xpathRequired", rulePath);
            }

            try
            {
                var navigator = absolute ? context.Document!.CreateNavigator() : context.CreateNavigator();
                var iterator = navigator.Select(expression, _namespaces!);
                var elements = new List<XElement>();
                while (iterator.MoveNext())
                {
                    if (iterator.Current?.UnderlyingObject is XElement element)
                    {
                        elements.Add(element);
                    }
                }

                return elements;
            }
            catch (XPathException)
            {
                throw new RuleException("invalidXpath", rulePath);
            }
        }

        private List<(int Number, string Text)> TextLines(Scope scope)
        {
            if (scope.Lines != null)
            {
                return scope.Lines;
            }

            if (scope.Rows != null)
            {
                return scope.Rows.Select(r => (r.LineNumber, r.Text)).ToList();
            }

            if (scope.Node != null)
            {
                return scope.Node.DescendantsAndSelf()
                    .Where(e => !e.HasElements && TextNormalizer.NormalizeLine(e.Value).Length > 0)
                    .Select(e =>
                    {
                        var path = XmlPaths.PositionalPath(e);
                        return (_xmlLineNumbers.GetValueOrDefault(path), path + ": " + TextNormalizer.NormalizeLine(e.Value));
                    })
                    .ToList();
            }

            return [];
        }

        private int Column(string header, string target)
        {
            if (_headers.TryGetValue(NormalizeHeader(header), out var column))
            {
                return column;
            }

            throw new RuleException("columnNotFound", target);
        }

        private void RequireXml(string rulePath)
        {
            if (_document.Xml?.Root == null)
            {
                throw new RuleException("xpathNeedsXml", rulePath);
            }
        }

        private static Regex CreateRegex(string pattern, string rulePath)
        {
            try
            {
                return new Regex(pattern, RegexOptions.None, RegexTimeout);
            }
            catch (ArgumentException)
            {
                throw new RuleException("invalidRegex", rulePath);
            }
        }

        private static SourceLocation LineLocation(int number)
            => new() { Kind = SourceLocationKind.Line, Ref = "line " + number.ToString(CultureInfo.InvariantCulture) };

        private static string NormalizeHeader(string header) => TextNormalizer.NormalizeLine(header).Replace("  ", " ", StringComparison.Ordinal);
    }

    /* ---------- Transforms ---------- */

    private static (List<string> Values, string? Error) Transform(List<string> values, FieldTransform transform)
    {
        if (transform.Kind == FieldTransformKind.Join)
        {
            return ([string.Join(transform.Separator ?? " ", values.Where(v => v.Length > 0))], null);
        }

        var result = new List<string>(values.Count);
        foreach (var value in values)
        {
            string? output;
            string? error = null;
            switch (transform.Kind)
            {
                case FieldTransformKind.Trim:
                    output = TextNormalizer.NormalizeLine(value).Replace("  ", " ", StringComparison.Ordinal);
                    break;
                case FieldTransformKind.Upper:
                    output = value.ToUpperInvariant();
                    break;
                case FieldTransformKind.Date:
                    (output, error) = ValueParsers.Date(value, transform.Format, transform.Culture);
                    break;
                case FieldTransformKind.Time:
                    (output, error) = ValueParsers.Time(value, transform.Format);
                    break;
                case FieldTransformKind.Number:
                    (output, error) = ValueParsers.Number(StripUnits(value), transform.Culture);
                    break;
                case FieldTransformKind.Map:
                    output = transform.Map?.FirstOrDefault(kv => string.Equals(kv.Key.Trim(), value.Trim(), StringComparison.OrdinalIgnoreCase)).Value ?? value;
                    break;
                case FieldTransformKind.Split:
                {
                    var parts = value.Split(string.IsNullOrEmpty(transform.Separator) ? " " : transform.Separator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                    var index = transform.Index ?? 0;
                    if (index < 0)
                    {
                        index = parts.Length + index;
                    }

                    output = index >= 0 && index < parts.Length ? parts[index] : string.Empty;
                    break;
                }
                default:
                    return (values, "unknownTransform");
            }

            if (error != null)
            {
                return (values, error);
            }

            result.Add(output ?? string.Empty);
        }

        return (result, null);
    }

    /// <summary>"1.250 kg" → "1.250": a number transform reads the number and drops a trailing unit.</summary>
    private static string StripUnits(string value)
    {
        var trimmed = value.Trim();
        var end = trimmed.Length;
        while (end > 0 && (char.IsLetter(trimmed[end - 1]) || trimmed[end - 1] is ' ' or '³' or '²'))
        {
            end--;
        }

        var start = 0;
        while (start < end && (char.IsLetter(trimmed[start]) || trimmed[start] is ' ' or '€' or '$'))
        {
            start++;
        }

        return trimmed[start..end];
    }

    private static string Snippet(string raw) => Cut(raw, OrderIntakeConsts.MaxSnippetLength);

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..max];
}

public enum TargetLevel
{
    Unknown = 0,
    Order = 1,
    Line = 2,
    Stop = 3
}

/// <summary>A rule's target: <c>order.customerReference</c>, <c>order.references[CMR]</c>, <c>line.weightKg</c>, <c>stop[Pickup].address.postalCode</c>.</summary>
public sealed record Target(TargetLevel Level, string Leaf, IntakeStopRole Role = IntakeStopRole.Pickup, string? ReferenceType = null);

public static partial class TargetParser
{
    public static Target Parse(string target)
    {
        var text = (target ?? string.Empty).Trim();
        if (text.StartsWith("order.", StringComparison.OrdinalIgnoreCase))
        {
            var leaf = text[6..];
            var reference = ReferenceTarget().Match(leaf);
            if (reference.Success)
            {
                return new Target(TargetLevel.Order, "references", ReferenceType: reference.Groups["type"].Value.Trim());
            }

            return IntakeValuePaths.IsOrderField(leaf) ? new Target(TargetLevel.Order, Canonical(IntakeValuePaths.OrderFields, leaf)) : new Target(TargetLevel.Unknown, leaf);
        }

        if (text.StartsWith("line.", StringComparison.OrdinalIgnoreCase))
        {
            var leaf = text[5..];
            return IntakeValuePaths.IsLineField(leaf) ? new Target(TargetLevel.Line, Canonical(IntakeValuePaths.LineFields, leaf)) : new Target(TargetLevel.Unknown, leaf);
        }

        var stop = StopTarget().Match(text);
        if (stop.Success && Enum.TryParse<IntakeStopRole>(stop.Groups["role"].Value, ignoreCase: true, out var role) && Enum.IsDefined(role))
        {
            var leaf = stop.Groups["leaf"].Value;
            if (IntakeValuePaths.IsStopField(leaf) && !string.Equals(leaf, "role", StringComparison.OrdinalIgnoreCase))
            {
                return new Target(TargetLevel.Stop, Canonical(IntakeValuePaths.StopFields, leaf), role);
            }
        }

        return new Target(TargetLevel.Unknown, text);
    }

    private static string Canonical(IReadOnlyCollection<string> names, string leaf)
        => names.FirstOrDefault(n => string.Equals(n, leaf, StringComparison.OrdinalIgnoreCase)) ?? leaf;

    [GeneratedRegex(@"^references\[(?<type>[^\]]{1,32})\]$", RegexOptions.IgnoreCase, 1000)]
    private static partial Regex ReferenceTarget();

    [GeneratedRegex(@"^stop\[(?<role>[A-Za-z]+)\]\.(?<leaf>[A-Za-z]+(?:\.[A-Za-z]+)?)$", RegexOptions.None, 1000)]
    private static partial Regex StopTarget();
}
