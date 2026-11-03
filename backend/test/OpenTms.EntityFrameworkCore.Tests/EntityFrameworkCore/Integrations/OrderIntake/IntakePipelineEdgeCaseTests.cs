using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenTms.Features;
using OpenTms.Integrations.Features;
using OpenTms.Integrations.OrderIntake;
using OpenTms.Integrations.OrderIntake.Ai;
using OpenTms.Integrations.OrderIntake.Profiles;
using OpenTms.TestDoubles;
using Shouldly;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Writer;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Integrations.Intake;

using DocumentFormat = OpenTms.Integrations.OrderIntake.DocumentFormat;

/// <summary>
/// Design 72's edge cases through the pipeline: a scanned PDF gets one extraction call (extraction agent, the PDF attached,
/// no profile, confidence capped), a spreadsheet the model saw only in part without a verified profile needs attention and
/// is never partly submitted (build note 7), and the size limits (25 MB per document, 40 MB per mail) fail a document with
/// a step instead of storing it. Risk class: High (order chain, external input).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class IntakePipelineEdgeCaseTests : OpenTmsEntityFrameworkCoreTestBase
{
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly ICurrentTenant _currentTenant;

    public IntakePipelineEdgeCaseTests()
    {
        _currentTenant = GetRequiredService<ICurrentTenant>();
    }

    private ScriptedLanguageModelClient Ai => GetRequiredService<ScriptedLanguageModelClient>();

    private static IDisposable IntakeOn()
    {
        var a = TestFeatureValueProvider.Override(IntegrationsFeatures.OrderIntake, "true");
        var b = TestFeatureValueProvider.Override(IntegrationsFeatures.OrderIntakeAiMapping, "true");
        return new Both(a, b);
    }

    private sealed class Both(params IDisposable[] all) : IDisposable
    {
        public void Dispose()
        {
            foreach (var d in all.Reverse())
            {
                d.Dispose();
            }
        }
    }

    private Task<InboundMessageRecordResult> RecordAsync(string fileName, string contentType, byte[] content, DocumentFormat format, byte[]? raw = null)
        => WithUnitOfWorkAsync(() => GetRequiredService<IInboundMessageRecorder>().RecordAsync(new InboundMessageRecording
        {
            Channel = InboundChannel.Gmail, SourceId = Guid.NewGuid(), ExternalId = Guid.NewGuid().ToString("N"), From = "orders@acme.nl",
            ReceivedAt = DateTime.UtcNow, RawContent = raw,
            Documents = { new InboundDocumentRecording { Kind = InboundDocumentKind.Attachment, FileName = fileName, ContentType = contentType, Content = content, Format = format } }
        }));

    private Task<List<InboundDocument>> DocumentsAsync(Guid messageId) => WithUnitOfWorkAsync(() =>
        GetRequiredService<IRepository<InboundDocument, Guid>>().GetListAsync(d => d.InboundMessageId == messageId, includeDetails: true));

    private static byte[] ScannedPdf()
    {
        var builder = new PdfDocumentBuilder();
        builder.AddPage(PageSize.A4);
        return builder.Build();
    }

    [Fact]
    public async Task A_scanned_pdf_gets_one_extraction_call_with_the_pdf_attached_and_no_profile()
    {
        using var features = IntakeOn();
        using var tenant = _currentTenant.Change(_tenantA);
        Ai.Answer(new LearnedDocument
        {
            Index = 1, IsOrder = true, Confidence = 0.97m, Reason = "scan",
            Intakes = [new LearnedIntake { Fields = [new LearnedField { Path = "order.customerReference", Value = "SCAN-1", Source = "line 1" }] }],
            ProfileJson = ""
        });
        var recorded = await RecordAsync("scan.pdf", "application/pdf", ScannedPdf(), DocumentFormat.Pdf);

        await GetRequiredService<IInboundMessageProcessor>().ProcessAsync(recorded.InboundMessageId);

        var request = Ai.Requests.ShouldHaveSingleItem();
        request.Messages.SelectMany(m => m.Parts).ShouldContain(p => p.Kind == LanguageModelContentKind.Pdf && p.Data != null && p.Data.Length > 0, "layout and scans are visible to the model");
        var document = (await DocumentsAsync(recorded.InboundMessageId)).Single();
        document.Format.ShouldBe(DocumentFormat.ScannedPdf);
        document.MappingMethod.ShouldBe(MappingMethod.AiExtraction);
        document.Mapping!.Intakes.Single().Confidence.ShouldBeLessThanOrEqualTo(0.6m);
        (await WithUnitOfWorkAsync(() => GetRequiredService<IRepository<ImportProfile, Guid>>().CountAsync())).ShouldBe(0, "a scan gets no profile");
        using (_currentTenant.Change(null))
        {
            (await WithUnitOfWorkAsync(() => GetRequiredService<IRepository<AiUsageRecord, Guid>>().GetListAsync(r => r.InboundDocumentId == document.Id)))
                .ShouldHaveSingleItem().Agent.ShouldBe(OrderIntakeConsts.ExtractAgent);
        }
    }

    [Fact]
    public async Task A_spreadsheet_seen_in_part_without_a_verified_profile_needs_attention_and_is_never_partly_submitted()
    {
        using var features = IntakeOn();
        using var tenant = _currentTenant.Change(_tenantA);
        var csv = new StringBuilder("Ref;Van;Naar;Datum\n");
        for (var i = 1; i <= 60; i++)
        {
            csv.Append("R-").Append(i).Append(";1234AB;5678CD;04-11-2026\n");
        }

        Ai.Answer(new LearnedDocument
        {
            Index = 1, IsOrder = true, Confidence = 0.9m, Reason = "orders",
            Intakes = [new LearnedIntake { Fields = [new LearnedField { Path = "order.customerReference", Value = "R-1", Source = "A2" }] }],
            ProfileJson = ""
        });
        var recorded = await RecordAsync("orders.csv", "text/csv", Encoding.UTF8.GetBytes(csv.ToString()), DocumentFormat.Csv);

        await GetRequiredService<IInboundMessageProcessor>().ProcessAsync(recorded.InboundMessageId);

        var document = (await DocumentsAsync(recorded.InboundMessageId)).Single();
        document.Status.ShouldBe(InboundDocumentStatus.NeedsAttention);
        document.Steps.Select(s => s.Step).ShouldNotContain(IntakeStep.Submitted);
        (await WithUnitOfWorkAsync(() => GetRequiredService<IRepository<OpenTms.Orders.Intake.OrderIntake, Guid>>().CountAsync())).ShouldBe(0);
    }

    [Fact]
    public async Task A_document_over_25_megabytes_is_failed_with_a_step_and_not_stored()
    {
        using var tenant = _currentTenant.Change(_tenantA);
        var recorded = await RecordAsync("huge.csv", "text/csv", new byte[OrderIntakeConsts.MaxDocumentBytes + 1], DocumentFormat.Csv);

        var document = (await DocumentsAsync(recorded.InboundMessageId)).Single();
        document.Status.ShouldBe(InboundDocumentStatus.Failed);
        document.Steps.ShouldContain(s => s.Step == IntakeStep.Failed && s.Parameters["maxBytes"] == OrderIntakeConsts.MaxDocumentBytes.ToString());
        document.Steps.Select(s => s.Step).ShouldNotContain(IntakeStep.Stored);
        GetRequiredService<RecordingBackgroundJobManager>().Of<InboundMessageProcessingArgs>().ShouldNotContain(a => a.InboundMessageId == recorded.InboundMessageId, "nothing to process");
    }

    [Fact]
    public async Task A_mail_over_40_megabytes_stores_no_file_and_fails_every_document()
    {
        using var tenant = _currentTenant.Change(_tenantA);
        var recorded = await RecordAsync("small.csv", "text/csv", Encoding.UTF8.GetBytes("Ref\nR-1"), DocumentFormat.Csv, raw: new byte[OrderIntakeConsts.MaxMessageBytes + 1]);

        var message = await WithUnitOfWorkAsync(() => GetRequiredService<IRepository<InboundMessage, Guid>>().GetAsync(recorded.InboundMessageId));
        message.RawBlobName.ShouldBeNull();
        message.Status.ShouldBe(InboundMessageStatus.Failed);
        (await DocumentsAsync(recorded.InboundMessageId)).Single().Status.ShouldBe(InboundDocumentStatus.Failed);
    }
}
