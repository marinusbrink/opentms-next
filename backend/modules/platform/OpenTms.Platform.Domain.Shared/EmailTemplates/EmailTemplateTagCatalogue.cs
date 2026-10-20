using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTms.Platform.EmailTemplates;

/// <summary>
/// A catalogued Scriban tag (design 49, "Tags and default content"). <see cref="Path"/> is the full path the sender's
/// typed model exposes ("company.email", "freight.product"); <see cref="Snippet"/> is the text the editor inserts: for
/// a value <c>{{ path }}</c>, for a child of a collection <c>{{ item.field }}</c> and for the collection itself the loop
/// block with every child field. Labels and descriptions are localized under <see cref="LabelKey"/> and
/// <see cref="DescriptionKey"/>.
/// </summary>
public sealed class EmailTemplateTag
{
    internal EmailTemplateTag(string path, EmailTemplateTagKind kind, string snippet, IReadOnlyList<EmailTemplateTag> children)
    {
        Path = path;
        Kind = kind;
        Snippet = snippet;
        Children = children;
    }

    public string Path { get; }
    public EmailTemplateTagKind Kind { get; }
    public string Snippet { get; }
    /// <summary>The child fields of a collection; empty for a value.</summary>
    public IReadOnlyList<EmailTemplateTag> Children { get; }
    public string LabelKey => EmailTemplateTagCatalogue.LabelKeyOf(Path);
    public string DescriptionKey => EmailTemplateTagCatalogue.DescriptionKeyOf(Path);
}

/// <summary>A row of the design's tag table: a group key, the purposes it applies to and its tags.</summary>
public sealed class EmailTemplateTagGroup
{
    internal EmailTemplateTagGroup(string key, IReadOnlyList<EmailTemplatePurpose> purposes, IReadOnlyList<EmailTemplateTag> tags)
    {
        Key = key;
        Purposes = purposes;
        Tags = tags;
    }

    public string Key { get; }
    public IReadOnlyList<EmailTemplatePurpose> Purposes { get; }
    public IReadOnlyList<EmailTemplateTag> Tags { get; }
    public string LabelKey => EmailTemplateTagCatalogue.GroupLabelKeyOf(Key);
}

/// <summary>
/// The one tag catalogue of the e-mail templates (design 49, "Tags and default content", PO 2026-10-20): the groups and
/// tags of the design table, as data. A group is a row of that table; a collection row ("freight", "invoice.lines",
/// "trips", "proforma.orders") is a group with one collection tag whose children are the row's fields. Paths are Scriban
/// member paths in snake_case. The module that sends the mail of a purpose exposes every path of
/// <see cref="GroupsFor"/> in its typed model (its run adds a contract test against this class); a path it cannot fill
/// renders empty.
/// Localization keys: group label <c>Platform:TagGroup.&lt;group&gt;</c>, tag label <c>Platform:Tag.&lt;path&gt;</c>,
/// tag description <c>Platform:TagDescription.&lt;path&gt;</c>; <see cref="AllLocalizationKeys"/> lists them all for
/// the parity test.
/// </summary>
public static class EmailTemplateTagCatalogue
{
    private const string GroupLabelPrefix = "Platform:TagGroup.";
    private const string LabelPrefix = "Platform:Tag.";
    private const string DescriptionPrefix = "Platform:TagDescription.";

    private static readonly IReadOnlyList<EmailTemplatePurpose> AllPurposes = Enum.GetValues<EmailTemplatePurpose>();

    public static IReadOnlyList<EmailTemplateTagGroup> Groups { get; } =
    [
        Values("company", AllPurposes,
            "name", "address", "postal_code", "city", "country", "phone", "email", "invoice_email", "website", "logo_url", "brand_color"),
        Values("user", AllPurposes, "full_name", "email"),
        Values("recipient", AllPurposes, "name", "email", "invoice_email", "contact_person", "contact_person_email", "salutation"),
        Values("general", AllPurposes, "today", "now", "year"),
        Values("order", [EmailTemplatePurpose.TransportOrder, EmailTemplatePurpose.EtaNotification, EmailTemplatePurpose.DigitalArchive],
            "number", "date", "reference", "customer_reference", "status", "loading_date", "unloading_date", "remarks",
            "origin.name", "origin.address", "origin.postal_code", "origin.city", "origin.country",
            "destination.name", "destination.address", "destination.postal_code", "destination.city", "destination.country",
            "shipper", "type_name"),
        Collection("freight", "line", [EmailTemplatePurpose.TransportOrder, EmailTemplatePurpose.EtaNotification],
            "product", "transport_unit", "quantity", "colli", "weight_kg", "volume_m3", "length_cm", "width_cm", "height_cm",
            "loading_meters", "container_number", "pincode", "invoice_reference", "origin_reference", "destination_reference", "shipper", "remarks"),
        Values("trip", [EmailTemplatePurpose.TransportFile, EmailTemplatePurpose.DriverInstructions, EmailTemplatePurpose.EtaNotification],
            "number", "date", "reference", "vehicle", "licence_plate", "driver", "driver_email", "carrier", "departure_date", "type_name"),
        Values("eta", [EmailTemplatePurpose.EtaNotification],
            "day_of_arrival", "time_of_arrival", "margin_from", "margin_till", "margin", "track_and_trace_url"),
        Values("invoice", [EmailTemplatePurpose.Invoice, EmailTemplatePurpose.InvoiceReminder],
            "number", "date", "due_date", "reference", "period", "total_amount", "vat_amount", "currency", "open_amount", "days_overdue", "type_name"),
        Collection("invoice.lines", "line", [EmailTemplatePurpose.Invoice, EmailTemplatePurpose.InvoiceReminder],
            "description", "quantity", "amount"),
        Values("purchase_order", [EmailTemplatePurpose.PurchaseOrder],
            "number", "date", "reference", "carrier", "total_amount", "currency", "type_name"),
        Values("driver", [EmailTemplatePurpose.DriverInstructions], "name", "email", "instructions_url"),
        Collection("trips", "trip", [EmailTemplatePurpose.DriverInstructions],
            "number", "date", "vehicle", "first_stop", "last_stop"),
        Values("document", [EmailTemplatePurpose.DigitalArchive], "file_name", "document_type", "received_at"),
        Values("proforma", [EmailTemplatePurpose.ProformaOverview], "period_from", "period_till", "total_amount", "currency"),
        Collection("proforma.orders", "order", [EmailTemplatePurpose.ProformaOverview],
            "number", "date", "reference", "amount"),
    ];

    /// <summary>The groups that apply to a purpose, in the design table's order.</summary>
    public static IReadOnlyList<EmailTemplateTagGroup> GroupsFor(EmailTemplatePurpose purpose)
        => Groups.Where(g => g.Purposes.Contains(purpose)).ToList();

    /// <summary>Every tag of a purpose, collections' children included (the sender's contract test).</summary>
    public static IEnumerable<EmailTemplateTag> TagsFor(EmailTemplatePurpose purpose)
        => GroupsFor(purpose).SelectMany(g => g.Tags).SelectMany(Flatten);

    /// <summary>Every localization key the catalogue needs, for the parity test.</summary>
    public static IEnumerable<string> AllLocalizationKeys()
    {
        foreach (var group in Groups)
        {
            yield return group.LabelKey;
            foreach (var tag in group.Tags.SelectMany(Flatten))
            {
                yield return tag.LabelKey;
                yield return tag.DescriptionKey;
            }
        }
    }

    public static string GroupLabelKeyOf(string group) => GroupLabelPrefix + group;
    public static string LabelKeyOf(string path) => LabelPrefix + path;
    public static string DescriptionKeyOf(string path) => DescriptionPrefix + path;

    private static IEnumerable<EmailTemplateTag> Flatten(EmailTemplateTag tag)
    {
        yield return tag;
        foreach (var child in tag.Children)
        {
            yield return child;
        }
    }

    private static EmailTemplateTagGroup Values(string key, IReadOnlyList<EmailTemplatePurpose> purposes, params string[] paths)
        => new(key, purposes, paths.Select(p => new EmailTemplateTag(key + "." + p, EmailTemplateTagKind.Value, "{{ " + key + "." + p + " }}", [])).ToList());

    /// <param name="item">The loop variable of the inserted block ("line", "trip", "order").</param>
    private static EmailTemplateTagGroup Collection(string key, string item, IReadOnlyList<EmailTemplatePurpose> purposes, params string[] fields)
    {
        var children = fields.Select(f => new EmailTemplateTag(key + "." + f, EmailTemplateTagKind.Value, "{{ " + item + "." + f + " }}", [])).ToList();
        var snippet = "{{ for " + item + " in " + key + " }}\n" + string.Join("\n", children.Select(c => c.Snippet)) + "\n{{ end }}";
        return new EmailTemplateTagGroup(key, purposes, [new EmailTemplateTag(key, EmailTemplateTagKind.Collection, snippet, children)]);
    }
}
