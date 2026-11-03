using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using MimeKit;
using OpenTms.Controllers.Integrations;
using OpenTms.Integrations.Features;
using OpenTms.Integrations.OrderIntake.ApiClients;
using OpenTms.Integrations.OrderIntake.Channels;
using OpenTms.Integrations.OrderIntake.IntakeApi;
using OpenTms.Integrations.Permissions;
using OpenTms.Orders.Intake;
using OpenTms.Orders.Permissions;
using OpenTms.Platform.Features;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Features;
using Xunit;

namespace OpenTms.Integrations.OrderIntake;

/// <summary>
/// The permission and feature attributes of design 72's services and controllers, read by reflection (the SQLite tests run
/// with every policy granted to a signed-in user, so this is where a wrong or missing attribute is caught): "Permissions
/// (all new; full list)" per method, the intake API behind <c>Integrations.IntakeApi.Submit</c> and the intake feature,
/// the OAuth callback the only anonymous action, the published services in-process only. Risk class: Critical — authorization.
/// </summary>
public class OrderIntakeAuthorizationTests
{
    private static string? PolicyOf(Type type, string method)
        => type.GetMethods(BindingFlags.Public | BindingFlags.Instance).First(m => m.Name == method).GetCustomAttribute<AuthorizeAttribute>()?.Policy
           ?? type.GetCustomAttribute<AuthorizeAttribute>()?.Policy;

    [Theory]
    [InlineData(typeof(MailboxConnectionAppService), "GetListAsync", IntegrationsPermissions.Mailboxes.Default)]
    [InlineData(typeof(MailboxConnectionAppService), "GetAsync", IntegrationsPermissions.Mailboxes.Default)]
    [InlineData(typeof(MailboxConnectionAppService), "CreateAsync", IntegrationsPermissions.Mailboxes.Manage)]
    [InlineData(typeof(MailboxConnectionAppService), "ReconnectAsync", IntegrationsPermissions.Mailboxes.Manage)]
    [InlineData(typeof(MailboxConnectionAppService), "GetFoldersAsync", IntegrationsPermissions.Mailboxes.Manage)]
    [InlineData(typeof(MailboxConnectionAppService), "UpdateAsync", IntegrationsPermissions.Mailboxes.Manage)]
    [InlineData(typeof(MailboxConnectionAppService), "PauseAsync", IntegrationsPermissions.Mailboxes.Manage)]
    [InlineData(typeof(MailboxConnectionAppService), "ResumeAsync", IntegrationsPermissions.Mailboxes.Manage)]
    [InlineData(typeof(MailboxConnectionAppService), "DeleteAsync", IntegrationsPermissions.Mailboxes.Manage)]
    [InlineData(typeof(ApiClientAppService), "GetListAsync", IntegrationsPermissions.ApiClients.Default)]
    [InlineData(typeof(ApiClientAppService), "GetAsync", IntegrationsPermissions.ApiClients.Default)]
    [InlineData(typeof(ApiClientAppService), "CreateAsync", IntegrationsPermissions.ApiClients.Manage)]
    [InlineData(typeof(ApiClientAppService), "UpdateAsync", IntegrationsPermissions.ApiClients.Manage)]
    [InlineData(typeof(ApiClientAppService), "RegenerateSecretAsync", IntegrationsPermissions.ApiClients.Manage)]
    [InlineData(typeof(ApiClientAppService), "ActivateAsync", IntegrationsPermissions.ApiClients.Manage)]
    [InlineData(typeof(ApiClientAppService), "DeactivateAsync", IntegrationsPermissions.ApiClients.Manage)]
    [InlineData(typeof(ApiClientAppService), "DeleteAsync", IntegrationsPermissions.ApiClients.Manage)]
    [InlineData(typeof(ImportProfileAppService), "GetListAsync", IntegrationsPermissions.ImportProfiles.Default)]
    [InlineData(typeof(ImportProfileAppService), "GetAsync", IntegrationsPermissions.ImportProfiles.Default)]
    [InlineData(typeof(ImportProfileAppService), "RenameAsync", IntegrationsPermissions.ImportProfiles.Manage)]
    [InlineData(typeof(ImportProfileAppService), "ActivateAsync", IntegrationsPermissions.ImportProfiles.Manage)]
    [InlineData(typeof(ImportProfileAppService), "RetireAsync", IntegrationsPermissions.ImportProfiles.Manage)]
    [InlineData(typeof(ImportProfileAppService), "DeleteAsync", IntegrationsPermissions.ImportProfiles.Manage)]
    [InlineData(typeof(InboundMessageAppService), "GetListAsync", IntegrationsPermissions.InboundMessages.Default)]
    [InlineData(typeof(InboundMessageAppService), "GetAsync", IntegrationsPermissions.InboundMessages.Default)]
    [InlineData(typeof(InboundMessageAppService), "GetRawContentAsync", IntegrationsPermissions.InboundMessages.Default)]
    [InlineData(typeof(InboundMessageAppService), "GetDocumentContentAsync", IntegrationsPermissions.InboundMessages.Default)]
    [InlineData(typeof(InboundMessageAppService), "ReprocessDocumentAsync", IntegrationsPermissions.InboundMessages.Reprocess)]
    [InlineData(typeof(InboundDocumentsAppService), "ReprocessAsync", IntegrationsPermissions.InboundMessages.Reprocess)]
    [InlineData(typeof(IntakeApiAppService), "SubmitOrderAsync", IntegrationsPermissions.IntakeApi.Submit)]
    [InlineData(typeof(IntakeApiAppService), "SubmitDocumentAsync", IntegrationsPermissions.IntakeApi.Submit)]
    [InlineData(typeof(IntakeApiAppService), "GetByExternalIdAsync", IntegrationsPermissions.IntakeApi.Submit)]
    [InlineData(typeof(IntakeApiController), "SubmitOrderAsync", IntegrationsPermissions.IntakeApi.Submit)]
    [InlineData(typeof(IntakeApiController), "SubmitDocumentAsync", IntegrationsPermissions.IntakeApi.Submit)]
    [InlineData(typeof(IntakeApiController), "GetByExternalIdAsync", IntegrationsPermissions.IntakeApi.Submit)]
    [InlineData(typeof(OrderIntakeAppService), "GetListAsync", OrdersPermissions.OrderIntakes.Default)]
    [InlineData(typeof(OrderIntakeAppService), "GetAsync", OrdersPermissions.OrderIntakes.Default)]
    [InlineData(typeof(OrderIntakeAppService), "ResolveAsync", OrdersPermissions.OrderIntakes.Resolve)]
    [InlineData(typeof(OrderIntakeAppService), "RejectAsync", OrdersPermissions.OrderIntakes.Reject)]
    [InlineData(typeof(OrderIntakeAppService), "GetAliasesAsync", OrdersPermissions.OrderIntakes.Default)]
    [InlineData(typeof(OrderIntakeAppService), "DeleteAliasesAsync", OrdersPermissions.OrderIntakes.Resolve)]
    public void Every_method_carries_the_designed_permission(Type service, string method, string permission)
    {
        PolicyOf(service, method).ShouldBe(permission);
    }

    [Theory]
    [InlineData(typeof(MailboxConnectionAppService))]
    [InlineData(typeof(ApiClientAppService))]
    [InlineData(typeof(ImportProfileAppService))]
    [InlineData(typeof(InboundMessageAppService))]
    [InlineData(typeof(InboundDocumentsAppService))]
    [InlineData(typeof(IntakeApiAppService))]
    [InlineData(typeof(OrderIntakeAppService))]
    public void Every_order_intake_service_sits_behind_the_tenant_feature(Type service)
    {
        service.GetCustomAttributes<RequiresFeatureAttribute>().SelectMany(a => a.Features).ShouldContain(IntegrationsFeatures.OrderIntake);
    }

    [Theory]
    [InlineData(typeof(MailboxConnectionAppService), AppFeatures.MasterData.Integrations.Intake.Mailboxes)]
    [InlineData(typeof(ApiClientAppService), AppFeatures.MasterData.Integrations.Intake.ApiClients)]
    [InlineData(typeof(ImportProfileAppService), AppFeatures.MasterData.Integrations.Intake.ImportProfiles)]
    [InlineData(typeof(InboundMessageAppService), AppFeatures.IntegrationsApp.Received)]
    [InlineData(typeof(OrderIntakeAppService), AppFeatures.TransportApp.Intake)]
    public void Every_screen_service_sits_behind_its_nav_node_feature_with_all_required(Type service, string node)
    {
        var attribute = service.GetCustomAttributes<RequiresFeatureAttribute>().Single();
        attribute.Features.ShouldContain(node);
        attribute.RequiresAll.ShouldBeTrue();
    }

    [Fact]
    public void The_oauth_callback_is_the_only_anonymous_action_and_the_published_services_stay_in_process()
    {
        typeof(MailboxOAuthCallbackController).GetMethod(nameof(MailboxOAuthCallbackController.CallbackAsync))!.GetCustomAttribute<AllowAnonymousAttribute>().ShouldNotBeNull();
        typeof(IntakeApiController).GetCustomAttribute<AllowAnonymousAttribute>().ShouldBeNull();
        typeof(IntakeApiAppService).GetCustomAttribute<RemoteServiceAttribute>()!.IsEnabled.ShouldBeFalse("served only by IntakeApiController");
        typeof(OrderIntakeService).GetCustomAttribute<RemoteServiceAttribute>()!.IsEnabled.ShouldBeFalse();
        typeof(OpenTms.MasterData.Published.MasterDataMatchQuery).GetCustomAttribute<RemoteServiceAttribute>()!.IsEnabled.ShouldBeFalse();
        typeof(InboundMessageAppService).GetMethod(nameof(InboundMessageAppService.ReprocessDocumentAsync))!.GetCustomAttribute<RemoteServiceAttribute>()!.IsEnabled.ShouldBeFalse();
    }

    [Theory]
    [InlineData("order.pdf", "application/pdf", DocumentFormat.Pdf)]
    [InlineData("o.XLSX", null, DocumentFormat.Xlsx)]
    [InlineData("noname", "text/csv; charset=utf-8", DocumentFormat.Csv)]
    [InlineData(null, "application/json", DocumentFormat.Json)]
    public void The_intake_api_reads_the_format_from_the_name_then_the_type(string? fileName, string? contentType, DocumentFormat expected)
    {
        IntakeApiAppService.FormatOf(fileName, contentType).ShouldBe(expected);
    }

    [Fact]
    public void The_intake_api_refuses_an_unknown_format()
    {
        Should.Throw<BusinessException>(() => IntakeApiAppService.FormatOf("x.exe", "application/x-msdownload")).Code.ShouldBe(IntegrationsErrorCodes.UnsupportedFormat);
    }

    [Fact]
    public void Client_ids_and_secrets_have_the_designed_shape_and_are_random()
    {
        var id = ApiClientApplicationRegistrar.NewClientId();
        id.ShouldStartWith("otms_");
        id.Length.ShouldBe(5 + 24);
        id[5..].ShouldAllBe(c => "abcdefghijklmnopqrstuvwxyz234567".Contains(char.ToLowerInvariant(c)), "base32 (the design names no case)");
        ApiClientApplicationRegistrar.NewSecret().Length.ShouldBe(40);
        Enumerable.Range(0, 50).Select(_ => ApiClientApplicationRegistrar.NewSecret()).Distinct().Count().ShouldBe(50);
    }
}

/// <summary>
/// Turning a raw mail into documents (design 72 §2 "Body documents"): the body is a document only with more than 40
/// characters after quote and signature, supported attachments become documents, unsupported ones and attached mails are
/// listed as Skipped, inline images are ignored, the external id is the channel's, else the Message-ID, else a hash.
/// Risk class: High — external integration (a mail that becomes no document is an order nobody sees).
/// </summary>
public class MailMessageParsingTests
{
    private static byte[] Raw(MimeMessage message)
    {
        using var stream = new MemoryStream();
        message.WriteTo(stream);
        return stream.ToArray();
    }

    private static MimeMessage Mail(string body, Action<BodyBuilder>? attach = null, string? messageId = "abc@acme.nl")
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("ACME Orders", "orders@acme.nl"));
        message.To.Add(new MailboxAddress("Planning", "planning@tenant.nl"));
        message.Subject = "Transportopdracht 778";
        if (messageId != null)
        {
            message.MessageId = messageId;
        }

        var builder = new BodyBuilder { TextBody = body };
        attach?.Invoke(builder);
        message.Body = builder.ToMessageBody();
        return message;
    }

    [Fact]
    public void A_see_attachment_body_with_two_pdfs_and_an_image_gives_two_documents_and_a_skipped_image()
    {
        var mail = Mail("Zie bijlage.\r\n-- \r\nJan Jansen\r\nACME Orders, 06-12345678, www.acme.nl", b =>
        {
            b.Attachments.Add("order-778.pdf", Encoding.ASCII.GetBytes("%PDF-1.4 order"), new ContentType("application", "pdf"));
            b.Attachments.Add("algemene-voorwaarden.pdf", Encoding.ASCII.GetBytes("%PDF-1.4 terms"), new ContentType("application", "pdf"));
            b.Attachments.Add("logo.png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0], new ContentType("image", "png"));
            var inline = b.LinkedResources.Add("signature.png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 1]);
            inline.ContentId = "sig1";
        });

        var recording = MailMessageRecorder.Parse(InboundChannel.Gmail, Guid.NewGuid(), "gmail-id-1", Raw(mail), DateTime.UtcNow);

        recording.ExternalId.ShouldBe("gmail-id-1", "Gmail's id wins");
        recording.From.ShouldBe("orders@acme.nl");
        recording.Subject.ShouldBe("Transportopdracht 778");
        recording.Documents.ShouldNotContain(d => d.Kind == InboundDocumentKind.MailBody, "'Zie bijlage.' is not more than 40 characters once the signature is gone");
        recording.Documents.Where(d => !d.Skipped).Select(d => d.FileName).ShouldBe(["order-778.pdf", "algemene-voorwaarden.pdf"]);
        recording.Documents.Where(d => !d.Skipped).ShouldAllBe(d => d.Format == DocumentFormat.Pdf && d.Kind == InboundDocumentKind.Attachment);
        recording.Documents.Where(d => d.Skipped).Select(d => d.FileName).ShouldBe(["logo.png"], "the inline signature image is ignored, the attached one listed");
        recording.RawContent.ShouldNotBeNull();
    }

    [Fact]
    public void A_body_with_an_order_in_it_is_a_document_and_quoted_replies_do_not_count()
    {
        var order = Mail("Graag morgen 3 pallets ophalen bij DC Venlo, Celsiusweg 12, en leveren in Utrecht.\r\n> eerder bericht\r\n> nog meer");
        MailMessageRecorder.Parse(InboundChannel.Microsoft365, Guid.NewGuid(), null, Raw(order), DateTime.UtcNow).Documents
            .ShouldHaveSingleItem().Kind.ShouldBe(InboundDocumentKind.MailBody);

        var quotedOnly = Mail("Akkoord.\r\n> Graag morgen 3 pallets ophalen bij DC Venlo, Celsiusweg 12, en leveren in Utrecht.");
        MailMessageRecorder.Parse(InboundChannel.Microsoft365, Guid.NewGuid(), null, Raw(quotedOnly), DateTime.UtcNow).Documents.ShouldBeEmpty();
    }

    [Fact]
    public void A_spreadsheet_csv_and_xml_attachment_are_documents_and_an_attached_mail_or_invite_is_skipped()
    {
        var mail = Mail("zie bijlage", b =>
        {
            b.Attachments.Add("orders.csv", Encoding.UTF8.GetBytes("Ref;Van\nR-1;1234AB"), new ContentType("text", "csv"));
            b.Attachments.Add("orders.xml", Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><Orders/>"), new ContentType("application", "xml"));
            b.Attachments.Add("invite.ics", Encoding.UTF8.GetBytes("BEGIN:VCALENDAR"), new ContentType("text", "calendar"));
            b.Attachments.Add(new MessagePart { Message = Mail("forwarded order body that is long enough to count as a body text", messageId: "fwd@acme.nl") });
        });

        var documents = MailMessageRecorder.Parse(InboundChannel.Gmail, Guid.NewGuid(), "g", Raw(mail), DateTime.UtcNow).Documents;

        documents.Where(d => !d.Skipped).Select(d => d.Format).ShouldBe([DocumentFormat.Csv, DocumentFormat.Xml]);
        documents.Count(d => d.Skipped).ShouldBe(2, "the invite and the attached mail are listed, never read");
    }

    [Fact]
    public void Without_a_channel_id_the_message_id_is_the_external_id_else_a_hash_of_the_raw_mail()
    {
        var withHeader = Raw(Mail("x"));
        MailMessageRecorder.Parse(InboundChannel.Microsoft365, Guid.NewGuid(), null, withHeader, DateTime.UtcNow).ExternalId.ShouldBe("abc@acme.nl");

        var withoutHeader = Encoding.ASCII.GetBytes("From: a@b.nl\r\nSubject: x\r\n\r\nbody");
        var first = MailMessageRecorder.Parse(InboundChannel.Microsoft365, Guid.NewGuid(), null, withoutHeader, DateTime.UtcNow).ExternalId;
        first.ShouldStartWith("sha256:");
        MailMessageRecorder.Parse(InboundChannel.Microsoft365, Guid.NewGuid(), null, withoutHeader, DateTime.UtcNow).ExternalId.ShouldBe(first, "the same mail, the same key");
    }
}
