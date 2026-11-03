using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace OpenTms.Orders.Intake;

/// <summary>
/// The resolution of an intake (design 72 "Resolution", the <c>Resolution</c> jsonb): per field path its status, the row
/// it resolved to with that row's name at the time, the rule that resolved it, and up to
/// <see cref="IntakeConsts.MaxCandidates"/> candidates. In field-path order as the resolver visited them.
/// </summary>
public sealed class IntakeResolution
{
    public List<IntakeFieldResolution> Fields { get; set; } = new();

    /// <summary>The fields not Resolved, optional ones (product, temperature) included.</summary>
    public int OpenFieldCount => Fields.Count(f => f.Status != IntakeResolutionStatus.Resolved);

    /// <summary>
    /// The fields that keep the intake from becoming an order: not Resolved, not optional, and not to be created in Master
    /// Data when the order is created (<see cref="IntakeFieldResolution.Creatable"/>, build note 26).
    /// </summary>
    public List<string> BlockingFieldPaths()
        => Fields.Where(f => f.Status != IntakeResolutionStatus.Resolved && !f.Creatable && !IntakeFieldPaths.IsOptional(f.FieldPath)).Select(f => f.FieldPath).ToList();

    public IntakeFieldResolution? Find(string fieldPath) => Fields.FirstOrDefault(f => f.FieldPath == fieldPath);

    public static string ToJson(IntakeResolution resolution) => JsonSerializer.Serialize(resolution, OrderIntakeJson.Options);

    public static IntakeResolution FromJson(string? json)
        => string.IsNullOrWhiteSpace(json) ? new IntakeResolution() : JsonSerializer.Deserialize<IntakeResolution>(json, OrderIntakeJson.Options) ?? new IntakeResolution();
}

public sealed class IntakeFieldResolution
{
    public string FieldPath { get; set; } = string.Empty;

    public IntakeResolutionStatus Status { get; set; }

    public Guid? TargetId { get; set; }

    /// <summary>The target's name when it was resolved (a snapshot; the review shows it beside the received text).</summary>
    public string? TargetName { get; set; }

    public IntakeResolutionMethod? Method { get; set; }

    /// <summary>The name similarity (0–1, two decimals) of a <see cref="IntakeResolutionMethod.Fuzzy"/> resolution; null otherwise.</summary>
    public decimal? Score { get; set; }

    /// <summary>
    /// Unresolved, and nothing in Master Data is close enough: the row is created from the received values when the order
    /// is created (build note 26). Such a field does not block the order; it is Resolved with method Created afterwards.
    /// </summary>
    public bool Creatable { get; set; }

    public List<IntakeCandidate> Candidates { get; set; } = new();

    /// <summary>
    /// The business error code that refused the order (only on the pseudo-field <see cref="IntakeFieldPaths.Order"/>,
    /// design 72 build note 20); null on every other field.
    /// </summary>
    public string? ErrorCode { get; set; }
}

public sealed class IntakeCandidate
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Detail { get; set; }
}
