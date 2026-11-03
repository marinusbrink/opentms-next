using System;
using System.IO;
using System.Linq;
using System.Text;

namespace OpenTms.Integrations.OrderIntake;

/// <summary>
/// The format of a received file by its content and its name (design 72 §2): pdf, xlsx, csv, txt, xml and html are read;
/// everything else (images, calendar invites, an attached mail, old binary Office files) is listed as Skipped. Content
/// wins over the name where they disagree on a binary format (a PDF named ".txt" is a PDF).
/// </summary>
public static class DocumentFormatDetector
{
    public static DocumentFormat Detect(byte[] content, string? fileName, string? contentType)
    {
        var extension = Path.GetExtension(fileName ?? string.Empty).TrimStart('.').ToLowerInvariant();
        var type = (contentType ?? string.Empty).Split(';')[0].Trim().ToLowerInvariant();

        if (StartsWith(content, "%PDF-"u8))
        {
            return DocumentFormat.Pdf;
        }

        var isZip = content.Length > 4 && content[0] == 0x50 && content[1] == 0x4B && content[2] == 0x03 && content[3] == 0x04;
        if (isZip)
        {
            return extension == "xlsx" || extension == "xlsm" || type == "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                ? DocumentFormat.Xlsx
                : DocumentFormat.Unknown;
        }

        if (!LooksLikeText(content))
        {
            return DocumentFormat.Unknown;
        }

        switch (extension)
        {
            case "csv":
                return DocumentFormat.Csv;
            case "xml":
                return DocumentFormat.Xml;
            case "htm" or "html":
                return DocumentFormat.Html;
            case "txt" or "text":
                return DocumentFormat.Text;
            case "json":
                return DocumentFormat.Unknown;
            case "ics" or "eml" or "vcf" or "msg":
                return DocumentFormat.Unknown;
        }

        switch (type)
        {
            case "text/csv":
                return DocumentFormat.Csv;
            case "application/xml" or "text/xml":
                return DocumentFormat.Xml;
            case "text/html":
                return DocumentFormat.Html;
            case "text/plain":
                return DocumentFormat.Text;
        }

        var head = Encoding.UTF8.GetString(content, 0, Math.Min(content.Length, 512)).TrimStart('﻿', ' ', '\r', '\n', '\t');
        if (head.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
        {
            return DocumentFormat.Xml;
        }

        return head.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase) || head.StartsWith("<html", StringComparison.OrdinalIgnoreCase)
            ? DocumentFormat.Html
            : DocumentFormat.Unknown;
    }

    /// <summary>The content type stored for a format when the sender gave none.</summary>
    public static string DefaultContentType(DocumentFormat format) => format switch
    {
        DocumentFormat.Pdf or DocumentFormat.ScannedPdf => "application/pdf",
        DocumentFormat.Xlsx => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        DocumentFormat.Csv => "text/csv",
        DocumentFormat.Xml => "application/xml",
        DocumentFormat.Html => "text/html",
        DocumentFormat.Text => "text/plain",
        DocumentFormat.Json => "application/json",
        _ => "application/octet-stream"
    };

    private static bool StartsWith(byte[] content, ReadOnlySpan<byte> prefix)
    {
        var start = 0;
        while (start < content.Length && start < 1024 && content[start] is 0x20 or 0x0D or 0x0A or 0x09)
        {
            start++;
        }

        return content.AsSpan(start).StartsWith(prefix);
    }

    /// <summary>No NUL bytes in the first 8 KB (UTF-16 with a BOM counts as text).</summary>
    private static bool LooksLikeText(byte[] content)
    {
        if (content.Length >= 2 && ((content[0] == 0xFF && content[1] == 0xFE) || (content[0] == 0xFE && content[1] == 0xFF)))
        {
            return true;
        }

        return !content.Take(8192).Contains((byte)0);
    }
}
