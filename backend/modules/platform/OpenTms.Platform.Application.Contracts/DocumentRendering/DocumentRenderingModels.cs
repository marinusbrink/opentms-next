using System;
using System.Collections.Generic;
using System.Text.Json;

namespace OpenTms.Platform.DocumentRendering;

/// <summary>What a provider renders: one template per record, the output format, the file name and the test flag.</summary>
public class RenderRequest
{
    public IReadOnlyList<RenderTemplate> Templates { get; init; } = [];

    public ReportRenderFormat Format { get; init; } = ReportRenderFormat.Pdf;

    /// <summary>The composed file name, extension included.</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>A test render: the provider marks it as such and does not bill it.</summary>
    public bool IsTest { get; init; }
}

/// <summary>One provider template with the data it renders.</summary>
public class RenderTemplate
{
    public string TemplateId { get; init; } = string.Empty;

    public JsonElement Data { get; init; }
}

/// <summary>A rendered document: the bytes, its file name and content type. The caller stores it (AD-09).</summary>
public class RenderedDocument
{
    public byte[] Content { get; init; } = Array.Empty<byte>();

    public string FileName { get; init; } = string.Empty;

    public string ContentType { get; init; } = string.Empty;
}

/// <summary>A provider's state as far as the deployment knows it, without a call to the provider.</summary>
public class ReportRenderingProviderStatus
{
    /// <summary>The deployment holds the provider's configuration (for pdfgeneratorapi: key and secret).</summary>
    public bool IsConfigured { get; init; }

    /// <summary>Whether the provider answered; null when not checked (the health check is the connection test).</summary>
    public bool? IsReachable { get; init; }

    /// <summary>A technical explanation for developers and operators; never a credential.</summary>
    public string? Message { get; init; }
}

/// <summary>A provider template as the current workspace sees it.</summary>
public class ProviderTemplate
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    /// <summary>The current workspace owns the template; false for FiLogic's shared (organization) templates.</summary>
    public bool OwnedByWorkspace { get; init; }
}
