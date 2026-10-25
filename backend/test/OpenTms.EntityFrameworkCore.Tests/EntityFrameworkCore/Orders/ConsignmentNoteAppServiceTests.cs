using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenTms.EntityFrameworkCore.Platform.DocumentRendering;
using OpenTms.Features;
using OpenTms.Orders;
using OpenTms.Orders.ConsignmentNotes;
using OpenTms.Orders.TransportOrders;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Platform.DocumentRendering;
using OpenTms.Platform.Features;
using OpenTms.Platform.ReferenceData;
using OpenTms.Platform.ReportDefinitions;
using OpenTms.TransportRun;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Modularity;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Orders;

/// <summary>The SQLite test application with the rendering provider replaced by the scriptable fake of design 55's tests.</summary>
[DependsOn(typeof(OpenTmsEntityFrameworkCoreTestModule))]
public class ConsignmentNoteTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.RemoveAll<IReportRenderingProvider>();
        var provider = new FakeReportRenderingProvider();
        context.Services.AddSingleton(provider);
        context.Services.AddSingleton<IReportRenderingProvider>(provider);
    }
}

/// <summary>
/// The consignment note (design 58 "CMR", PO decision Q11; routing-co2-cmr.md scenarios 27–30, 34) on SQLite: the print
/// composes the catalogue payload, renders it through Platform's renderer with the definition of purpose ConsignmentNote,
/// draws one number per document, stores the blob and one row; the list, the detail and the download; the refusals
/// (no definition, nothing to print); a trip print per order; profiles with the one default and fixed descriptions with
/// their in-use rule; tenant isolation. Risk class: High.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class ConsignmentNoteAppServiceTests : TransportOrderTestBase<ConsignmentNoteTestModule>
{
    private IConsignmentNoteAppService Notes => GetRequiredService<IConsignmentNoteAppService>();

    private IConsignmentNoteProfileAppService Profiles => GetRequiredService<IConsignmentNoteProfileAppService>();

    private IConsignmentNoteFixedDescriptionAppService FixedDescriptions => GetRequiredService<IConsignmentNoteFixedDescriptionAppService>();

    private FakeReportRenderingProvider Provider => GetRequiredService<FakeReportRenderingProvider>();

    private static IDisposable RenderingOn() => TestFeatureValueProvider.Override(PlatformFeatures.DocumentRendering, "true");

    private Task<ReportDefinitionDto> DefinitionAsync(bool isDefault = true, string culture = "nl")
        => GetRequiredService<IReportDefinitionAppService>().CreateAsync(new ReportDefinitionCreateDto
        {
            Name = "CMR " + culture, Purpose = ReportPurpose.ConsignmentNote, Culture = culture, IsDefault = isDefault, Provider = ReportRenderingProvider.PdfGeneratorApi,
            ProviderTemplateId = "tpl-1", FileNamePattern = "cmr-{{ number }}.pdf"
        });

    private Task<ConsignmentNoteProfileDto> ProfileAsync(string name = "Standaard", Guid? reportDefinitionId = null, List<Guid>? fixedDescriptionIds = null)
        => Profiles.CreateAsync(new ConsignmentNoteProfileCreateDto
        {
            Name = name, Copies = 4, ShowBorders = true, ShowQrCode = true, Box6Columns = ConsignmentNoteProfile.OpenTmsDefaultColumns.ToList(),
            Box13Items = [CmrInstructionItem.ShipperInstructions, CmrInstructionItem.FixedDescriptions], Box18Items = [CmrRemarkItem.OrderNumber, CmrRemarkItem.Vehicles],
            FixedDescriptionIds = fixedDescriptionIds ?? [], ReportDefinitionId = reportDefinitionId
        });

    [Fact]
    public async Task Print_composes_renders_numbers_and_stores_one_document_per_print_scenario_27_34()
    {
        using var _ = InTenantA();
        using var on = RenderingOn();
        var f = await FixtureAsync();
        var definition = await DefinitionAsync();
        var text = await FixedDescriptions.CreateAsync(new ConsignmentNoteFixedDescriptionCreateDto { Name = "Breekbaar", Text = "Voorzichtig behandelen" });
        var profile = await ProfileAsync(fixedDescriptionIds: [text.Id]);
        var order = await Orders.CreateAsync(CreateDto(f, confirm: true));
        Provider.Document = Encoding.UTF8.GetBytes("%PDF-1.7 cmr");

        var documents = await Notes.PrintAsync(new PrintConsignmentNoteDto { OrderLineIds = order.Lines.Select(l => l.Id!.Value).ToList() });

        var document = documents.ShouldHaveSingleItem();
        document.Number.ShouldBe($"CMR-{Today.Year}-000001");
        document.OrderId.ShouldBe(order.Id);
        document.OrderNumber.ShouldBe(order.Number);
        document.LineIds.ShouldBe(order.Lines.Select(l => l.Id!.Value));
        document.ProfileId.ShouldBe(profile.Id);
        document.ReportDefinitionId.ShouldBe(definition.Id);
        document.FileName.ShouldBe($"cmr-CMR-{Today.Year}-000001.pdf", "the definition's pattern filled with the CMR number");
        document.ContentType.ShouldBe("application/pdf");
        document.SizeBytes.ShouldBe(12);
        document.Sha256.Length.ShouldBe(64);

        /* The provider got the catalogue payload: the parties per box, the goods rows, the fixed description. */
        var render = Provider.Calls.Single(c => c.Operation == "render");
        var payload = JsonNode.Parse(((RenderRequest)render.Argument!).Templates.Single().Data.GetRawText())!;
        ReportDataCatalogue.Validate(ReportPurpose.ConsignmentNote, payload).ShouldBeEmpty();
        var entity = payload["Entity"]!;
        entity["Sender"]!["Name"]!.GetValue<string>().ShouldBe("Klant Foods B.V.");
        entity["Sender"]!["Address"]!["City"]!.GetValue<string>().ShouldBe("Barendrecht");
        entity["Consignee"]!["Name"]!.GetValue<string>().ShouldBe("Supermarkt Utrecht");
        entity["Consignee"]!["Address"]!["Street"]!.GetValue<string>().ShouldBe("Vredenburg");
        entity["TakingOverPlace"]!["City"]!.GetValue<string>().ShouldBe("Venlo");
        entity["EstablishedIn"]!["Name"]!.GetValue<string>().ShouldBe("Voorbeeld Transport", "box 21: the own company");
        entity["Goods"]!.AsArray().Single()["TransportUnit"]!.GetValue<string>().ShouldBe("Europallet");
        entity["Goods"]!.AsArray().Single()["Product"]!.GetValue<string>().ShouldBe("Zuivel");
        entity["GoodsDescription"]!.GetValue<string>().ShouldBe("Voorzichtig behandelen", "scenario 30");
        entity["Copies"]!.GetValue<int>().ShouldBe(4);
        payload["Me"]!["Name"]!.GetValue<string>().ShouldBe("Voorbeeld Transport");

        /* Stored once, downloadable, listed under the order. */
        var stored = await Notes.GetDocumentAsync(document.Id);
        stored.Number.ShouldBe(document.Number);
        var download = await Notes.GetDownloadAsync(document.Id);
        download.FileName.ShouldBe(document.FileName);
        using var reader = new StreamReader(download.GetStream());
        (await reader.ReadToEndAsync()).ShouldBe("%PDF-1.7 cmr");
        var list = await Notes.GetDocumentListAsync(new ConsignmentNoteDocumentListRequestDto { OrderId = order.Id });
        list.TotalCount.ShouldBe(1);

        /* A reprint is a new numbered document (scenario 34). */
        var reprint = (await Notes.PrintAsync(new PrintConsignmentNoteDto { OrderLineIds = [order.Lines[0].Id!.Value] })).Single();
        reprint.Number.ShouldBe($"CMR-{Today.Year}-000002");
        (await Notes.GetDocumentListAsync(new ConsignmentNoteDocumentListRequestDto { OrderId = order.Id, SortBy = "number", SortDir = "desc" })).Rows.Select(r => r.Number).ShouldBe([reprint.Number, document.Number]);
    }

    [Fact]
    public async Task Overrides_change_the_boxes_for_one_print_and_an_explicit_definition_wins_scenario_29()
    {
        using var _ = InTenantA();
        using var on = RenderingOn();
        var f = await FixtureAsync();
        await DefinitionAsync();
        var english = await DefinitionAsync(isDefault: false, culture: "en");
        await ProfileAsync();
        var order = await Orders.CreateAsync(CreateDto(f, confirm: true));

        var document = (await Notes.PrintAsync(new PrintConsignmentNoteDto
        {
            OrderLineIds = order.Lines.Select(l => l.Id!.Value).ToList(),
            ReportDefinitionId = english.Id,
            Overrides = new ConsignmentNoteOverridesDto { Copies = 2, Box1Sender = CmrPartySource.OwnCompany, Box2Consignee = CmrPartySource.Empty, Box18Items = [CmrRemarkItem.CustomerReference] }
        })).Single();

        document.ReportDefinitionId.ShouldBe(english.Id);
        var entity = JsonNode.Parse(((RenderRequest)Provider.Calls.Single(c => c.Operation == "render").Argument!).Templates.Single().Data.GetRawText())!["Entity"]!;
        entity["Copies"]!.GetValue<int>().ShouldBe(2);
        entity["Sender"]!["Name"]!.GetValue<string>().ShouldBe("Voorbeeld Transport");
        entity["Consignee"].ShouldBeNull();
        entity["CarrierRemarks"]!.GetValue<string>().ShouldBe("PO-1");
        entity["ProfileName"]!.GetValue<string>().ShouldBe("Standaard", "the profile stays as it is (the override is for this print)");

        await Should.ThrowAsync<Volo.Abp.Validation.AbpValidationException>(() => Notes.PrintAsync(new PrintConsignmentNoteDto { OrderLineIds = [order.Lines[0].Id!.Value], Overrides = new ConsignmentNoteOverridesDto { Copies = 9 } }));
    }

    [Fact]
    public async Task A_trip_print_takes_the_lines_of_the_trips_legs_and_makes_one_document_per_order_scenario_28()
    {
        using var _ = InTenantA();
        using var on = RenderingOn();
        var f = await FixtureAsync();
        await DefinitionAsync();
        await ProfileAsync();
        var first = await Orders.CreateAsync(CreateDto(f, confirm: true, lines: 2));
        var second = await Orders.CreateAsync(CreateDto(f, confirm: true));

        /* Planning's real trip: the carrier, a truck with its plate and a driver, the first line's leg of the first order and the second order's leg on it. */
        var truckType = await Run.VehicleTypeAsync("Truck", OpenTms.MasterData.Fleet.VehicleTypeKind.Rigid);
        var truck = await GetRequiredService<OpenTms.MasterData.Fleet.IVehicleAppService>().CreateAsync(new OpenTms.MasterData.Fleet.VehicleCreateDto
        {
            Name = "Truck 12", VehicleTypeId = truckType, OwnerRelationId = f.OwnCompanyId, LicencePlate = "12-BXL-3", RegistrationCountryId = f.CountryId, GrossVehicleWeightKg = 18_000
        });
        var driver = await Run.DriverAsync("JDV", "Jan", "de Vries");
        var trips = GetRequiredService<ITransportTripAppService>();
        var trip = await trips.CreateAsync(new TransportTripCreateDto
        {
            TransportTripTypeId = f.TripTypeId, PlannedDeparture = TransportRunFixture.At(Today, 6), PlannedArrival = TransportRunFixture.At(Today, 18),
            CarrierRelationId = f.CarrierId, Execution = TripExecution.Subcontracted, VehicleIds = [truck.Id], DriverIds = [driver]
        });
        var firstLegId = (await Run.LegsOfAsync(first.Id)).Single(l => l.OrderLineId == first.Lines[0].Id!.Value).Id;
        trip = await trips.AssignLegsAsync(trip.Id, new AssignLegsDto { LegIds = [firstLegId, (await Run.LegIdsOfAsync(second.Id)).Single()], AllowOutsideWindow = true });

        var documents = await Notes.PrintAsync(new PrintConsignmentNoteDto { TripId = trip.Id });

        documents.Count.ShouldBe(2);
        documents.Select(d => d.Number).ShouldBe([$"CMR-{Today.Year}-000001", $"CMR-{Today.Year}-000002"]);
        documents.ShouldAllBe(d => d.TripId == trip.Id);
        documents.Single(d => d.OrderId == first.Id).LineIds.ShouldBe([first.Lines[0].Id!.Value], "only the line on the trip, not the second line of the order");
        var entity = JsonNode.Parse(((RenderRequest)Provider.Calls.First(c => c.Operation == "render").Argument!).Templates.Single().Data.GetRawText())!["Entity"]!;
        entity["Trip"]!["Number"]!.GetValue<string>().ShouldBe(trip.Number);
        entity["Carrier"]!["Name"]!.GetValue<string>().ShouldBe("Charter Transport", "box 16 from the trip's carrier");
        entity["CarrierRemarks"]!.GetValue<string>().ShouldContain($"Truck 12 ({truck.LicencePlate})");
        (await Notes.GetDocumentListAsync(new ConsignmentNoteDocumentListRequestDto { TripId = trip.Id })).TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task Without_a_definition_of_purpose_ConsignmentNote_or_without_lines_the_print_is_refused()
    {
        using var _ = InTenantA();
        using var on = RenderingOn();
        var f = await FixtureAsync();
        await ProfileAsync();
        var order = await Orders.CreateAsync(CreateDto(f, confirm: true));

        (await Should.ThrowAsync<BusinessException>(() => Notes.PrintAsync(new PrintConsignmentNoteDto { OrderLineIds = [order.Lines[0].Id!.Value] })))
            .Code.ShouldBe(OrdersErrorCodes.ConsignmentNoteTemplateMissing);
        Provider.Calls.ShouldNotContain(c => c.Operation == "render");

        var invoice = await GetRequiredService<IReportDefinitionAppService>().CreateAsync(new ReportDefinitionCreateDto
        {
            Name = "Factuur", Purpose = ReportPurpose.Invoice, Culture = "nl", IsDefault = true, Provider = ReportRenderingProvider.PdfGeneratorApi, ProviderTemplateId = "tpl-1", FileNamePattern = "f-{{ number }}.pdf"
        });
        (await Should.ThrowAsync<BusinessException>(() => Notes.PrintAsync(new PrintConsignmentNoteDto { OrderLineIds = [order.Lines[0].Id!.Value], ReportDefinitionId = invoice.Id })))
            .Code.ShouldBe(OrdersErrorCodes.ConsignmentNoteTemplateMissing, "a definition of another purpose does not count");

        await DefinitionAsync();
        (await Should.ThrowAsync<BusinessException>(() => Notes.PrintAsync(new PrintConsignmentNoteDto()))).Code.ShouldBe(OrdersErrorCodes.NothingToPrint);
        (await Should.ThrowAsync<BusinessException>(() => Notes.PrintAsync(new PrintConsignmentNoteDto { OrderLineIds = [Guid.NewGuid()] }))).Code.ShouldBe(OrdersErrorCodes.NothingToPrint);
        (await Should.ThrowAsync<BusinessException>(() => Notes.PrintAsync(new PrintConsignmentNoteDto { TripId = Guid.NewGuid() }))).Code.ShouldBe(OrdersErrorCodes.NothingToPrint);
    }

    [Fact]
    public async Task Profiles_have_one_default_a_unique_name_and_checked_references_and_fixed_descriptions_in_use_stay()
    {
        using var _ = InTenantA();
        var f = await FixtureAsync();
        var text = await FixedDescriptions.CreateAsync(new ConsignmentNoteFixedDescriptionCreateDto { Name = "Breekbaar", Text = "Voorzichtig" });

        var first = await ProfileAsync("Standaard", fixedDescriptionIds: [text.Id]);
        first.IsDefault.ShouldBeTrue("the first profile of a tenant is the default");
        first.FixedDescriptionNames.ShouldBe(["Breekbaar"]);
        first.Box6Columns.ShouldBe(ConsignmentNoteProfile.OpenTmsDefaultColumns);
        var second = await ProfileAsync("Export");
        second.IsDefault.ShouldBeFalse();
        await Profiles.SetDefaultAsync(second.Id);
        (await Profiles.GetAsync(first.Id)).IsDefault.ShouldBeFalse();
        (await Profiles.GetListAsync(new ArchivableListRequestDto())).Rows.Single(r => r.IsDefault).Name.ShouldBe("Export");
        (await Should.ThrowAsync<BusinessException>(() => Profiles.DeleteAsync(second.Id))).Code.ShouldBe(ReferenceDataErrorCodes.DefaultRequired);

        (await Should.ThrowAsync<BusinessException>(() => ProfileAsync("standaard"))).Code.ShouldBe(ReferenceDataErrorCodes.DuplicateKey);
        (await Should.ThrowAsync<BusinessException>(() => ProfileAsync("Broken", fixedDescriptionIds: [Guid.NewGuid()]))).Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
        (await Should.ThrowAsync<BusinessException>(() => ProfileAsync("Broken", reportDefinitionId: Guid.NewGuid()))).Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);

        var blocked = await Should.ThrowAsync<BusinessException>(() => FixedDescriptions.DeleteAsync(text.Id));
        blocked.Code.ShouldBe(ReferenceDataErrorCodes.InUse);
        blocked.Data["reason"]!.ToString()!.ShouldContain("Standaard");
        (await FixedDescriptions.GetLookupAsync("breek")).ShouldHaveSingleItem();
        var free = await FixedDescriptions.CreateAsync(new ConsignmentNoteFixedDescriptionCreateDto { Name = "Los", Text = "Niet stapelen" });
        await FixedDescriptions.DeleteAsync(free.Id);
        (await FixedDescriptions.GetListAsync(new ArchivableListRequestDto())).TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task Another_tenant_sees_no_profiles_descriptions_or_documents()
    {
        Guid documentId;
        using (InTenantA())
        {
            using var on = RenderingOn();
            var f = await FixtureAsync();
            await DefinitionAsync();
            await ProfileAsync();
            var order = await Orders.CreateAsync(CreateDto(f, confirm: true));
            documentId = (await Notes.PrintAsync(new PrintConsignmentNoteDto { OrderLineIds = [order.Lines[0].Id!.Value] })).Single().Id;
        }

        using (InTenantB())
        {
            (await Profiles.GetListAsync(new ArchivableListRequestDto())).TotalCount.ShouldBe(0);
            (await FixedDescriptions.GetLookupAsync()).ShouldBeEmpty();
            (await Notes.GetDocumentListAsync(new ConsignmentNoteDocumentListRequestDto())).TotalCount.ShouldBe(0);
            await Should.ThrowAsync<EntityNotFoundException>(() => Notes.GetDocumentAsync(documentId));
            await Should.ThrowAsync<EntityNotFoundException>(() => Notes.GetDownloadAsync(documentId));
        }
    }
}
