using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using OpenTms.Platform.Localization;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Platform.DocumentTypes;

/// <summary>
/// A kind of document the digital archive files under (design 49; OpenTMS <c>BinaryObjectType</c>, UI "Soort digitale
/// documenten"). The upper-case code is the natural key the seed upserts on; the name is per language. The
/// transformations are a typed pipeline (each kind at most once) that the attachments run executes (GAP-05).
/// </summary>
public class DocumentType : PlatformMasterDataAggregateRoot
{
    /// <summary>Upper case, unique per tenant.</summary>
    public string Code { get; private set; } = default!;

    public LocalizedName Name { get; private set; } = default!;

    /// <summary>Shown in portals and exported in OTM.</summary>
    public bool IsFreightDocument { get; private set; }

    /// <summary>Attached to the invoice mail.</summary>
    public bool IncludeWithInvoice { get; private set; }

    /// <summary>OpenTMS <c>IncludeInGenericListView</c>.</summary>
    public bool ShowInListIndicator { get; private set; }

    /// <summary>Hidden from pickers.</summary>
    public bool IsArchived { get; private set; }

    public virtual ICollection<DocumentTypeTransformation> Transformations { get; private set; } = new List<DocumentTypeTransformation>();

    protected DocumentType()
    {
    }

    public DocumentType(Guid id, Guid? tenantId, string code, LocalizedName name)
        : base(id, tenantId)
    {
        SetCode(code);
        SetName(name);
    }

    public static string NormalizeCode(string code)
        => Check.NotNullOrWhiteSpace(code, nameof(code), PlatformMasterDataConsts.MaxCodeLength).Trim().ToUpperInvariant();

    public void SetCode(string code) => Code = NormalizeCode(code);

    public void SetName(LocalizedName name) => Name = Check.NotNull(name, nameof(name));

    public void SetFlags(bool isFreightDocument, bool includeWithInvoice, bool showInListIndicator)
    {
        IsFreightDocument = isFreightDocument;
        IncludeWithInvoice = includeWithInvoice;
        ShowInListIndicator = showInListIndicator;
    }

    public void Archive() => IsArchived = true;

    public void Unarchive() => IsArchived = false;

    /// <summary>The kinds whose argument is required.</summary>
    public static bool RequiresArgument(DocumentTransformationKind kind)
        => kind is DocumentTransformationKind.RenameByParentProperty or DocumentTransformationKind.SplitPdfByReferencePattern;

    /// <summary>
    /// Replaces the transformations in the given order (positions 1, 2, …): each kind at most once (so at most four), the
    /// argument required for RenameByParentProperty and SplitPdfByReferencePattern, a SplitPdfByReferencePattern argument
    /// a valid .NET regular expression (compiled with a one-second match timeout; never run here). Validated before
    /// anything changes. No argument in the exception data.
    /// </summary>
    public void SetTransformations(IReadOnlyList<(DocumentTransformationKind Kind, string? Argument)> ordered)
    {
        var normalized = new List<(DocumentTransformationKind Kind, string? Argument)>();
        foreach (var (kind, argument) in ordered)
        {
            Defined(kind, nameof(kind));
            if (normalized.Any(t => t.Kind == kind))
            {
                throw new BusinessException(PlatformErrorCodes.DuplicateTransformation).WithData("kind", kind.ToString());
            }

            var value = Optional(argument, nameof(argument), PlatformMasterDataConsts.MaxTransformationArgumentLength);
            if (value == null && RequiresArgument(kind))
            {
                throw new BusinessException(PlatformErrorCodes.TransformationArgumentRequired).WithData("kind", kind.ToString());
            }

            if (kind == DocumentTransformationKind.SplitPdfByReferencePattern && !IsValidRegex(value!))
            {
                throw new BusinessException(PlatformErrorCodes.InvalidRegex).WithData("kind", kind.ToString());
            }

            normalized.Add((kind, value));
        }

        var wanted = normalized.Select(t => t.Kind).ToHashSet();
        foreach (var stale in Transformations.Where(t => !wanted.Contains(t.Kind)).ToList())
        {
            Transformations.Remove(stale);
        }

        for (var i = 0; i < normalized.Count; i++)
        {
            var (kind, argument) = normalized[i];
            var transformation = Transformations.FirstOrDefault(t => t.Kind == kind);
            if (transformation == null)
            {
                transformation = new DocumentTypeTransformation(TenantId, Id, kind);
                Transformations.Add(transformation);
            }

            transformation.Set(i + 1, argument);
        }
    }

    /// <summary>True when <paramref name="pattern"/> compiles as a .NET regular expression with the one-second match timeout.</summary>
    public static bool IsValidRegex(string pattern)
    {
        try
        {
            _ = new Regex(pattern, RegexOptions.None, PlatformMasterDataConsts.RegexMatchTimeout);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

/// <summary>A step of a document type's pipeline at a position (1-based); composite key (document type, kind).</summary>
public class DocumentTypeTransformation : Entity, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public Guid DocumentTypeId { get; private set; }

    public DocumentTransformationKind Kind { get; private set; }

    public int Position { get; private set; }

    public string? Argument { get; private set; }

    protected DocumentTypeTransformation()
    {
    }

    public DocumentTypeTransformation(Guid? tenantId, Guid documentTypeId, DocumentTransformationKind kind)
    {
        TenantId = tenantId;
        DocumentTypeId = documentTypeId;
        Kind = kind;
    }

    public void Set(int position, string? argument)
    {
        Position = position;
        Argument = argument;
    }

    public override object[] GetKeys() => [DocumentTypeId, Kind];
}
