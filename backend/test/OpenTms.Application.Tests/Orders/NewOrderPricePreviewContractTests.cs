using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using OpenTms.Financial.Tariffs.Published;
using OpenTms.Orders.Permissions;
using OpenTms.Orders.TransportOrders;
using OpenTms.Platform.Features;
using Shouldly;
using Volo.Abp.Features;
using Xunit;

namespace OpenTms.Orders;

/// <summary>
/// The price preview of a new, never-saved order (Marinus 2026-10-30, "review prices before an order is saved"): the
/// attributes of <c>PreviewNewOrderPricesAsync</c> (read by reflection: the SQLite tests grant every policy to a
/// signed-in user, so a wrong or missing permission is caught here), the request DTO's required customer, and the
/// committed API contract (the POST route, the request schema and <c>linePosition</c> on the preview line).
/// Risk class: Critical (authorization), High (API contract).
/// </summary>
public class NewOrderPricePreviewContractTests
{
    private static MethodInfo Method(Type type, string name)
        => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Single(m => m.Name == name);

    [Fact]
    public void The_new_order_preview_needs_TransportOrders_Create_and_the_Orders_node_like_Create()
    {
        var method = Method(typeof(TransportOrderAppService), nameof(TransportOrderAppService.PreviewNewOrderPricesAsync));

        method.GetCustomAttribute<AuthorizeAttribute>().ShouldNotBeNull().Policy.ShouldBe(OrdersPermissions.TransportOrders.Create);
        method.GetCustomAttribute<AllowAnonymousAttribute>().ShouldBeNull();
        method.GetCustomAttribute<RequiresFeatureAttribute>().ShouldNotBeNull().Features.ShouldBe([AppFeatures.TransportApp.Orders]);
        method.IsVirtual.ShouldBeTrue("ABP's interceptors need a virtual method");

        var create = Method(typeof(TransportOrderAppService), nameof(TransportOrderAppService.CreateAsync));
        method.GetCustomAttribute<AuthorizeAttribute>()!.Policy.ShouldBe(create.GetCustomAttribute<AuthorizeAttribute>()!.Policy, "whoever may create an order may preview one");
        typeof(TransportOrderAppService).GetCustomAttribute<RequiresFeatureAttribute>().ShouldNotBeNull().Features.ShouldContain(AppFeatures.Transport);
        typeof(ITransportOrderAppService).GetMethod(nameof(ITransportOrderAppService.PreviewNewOrderPricesAsync)).ShouldNotBeNull("exposed through the conventional controller");
    }

    [Fact]
    public void The_request_requires_the_customer_and_starts_with_no_lines()
    {
        typeof(NewOrderPricePreviewRequestDto).GetProperty(nameof(NewOrderPricePreviewRequestDto.CustomerRelationId))!
            .GetCustomAttribute<RequiredAttribute>().ShouldNotBeNull();
        var request = new NewOrderPricePreviewRequestDto();
        request.Lines.ShouldBeEmpty();
        request.OrderDate.ShouldBeNull("missing means today");
        request.FinancialTerms.ShouldBeNull("missing means the customer's defaults");
    }

    [Fact]
    public void The_committed_contract_exposes_the_new_order_preview_as_POST_with_the_line_position()
    {
        var contract = Contract();
        var path = contract.GetProperty("paths").GetProperty("/api/orders/transport-order/preview-new-order-prices");
        path.EnumerateObject().Select(p => p.Name).ShouldBe(["post"]);
        var post = path.GetProperty("post");
        post.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString()
            .ShouldBe("#/components/schemas/OpenTms.Orders.TransportOrders.NewOrderPricePreviewRequestDto");
        post.GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString()
            .ShouldBe("#/components/schemas/OpenTms.Financial.Tariffs.Published.PricePreviewDto");

        var schemas = contract.GetProperty("components").GetProperty("schemas");
        var request = schemas.GetProperty("OpenTms.Orders.TransportOrders.NewOrderPricePreviewRequestDto");
        request.GetProperty("properties").EnumerateObject().Select(p => p.Name).ShouldBe(["customerRelationId", "financialTerms", "lines", "orderDate"], ignoreOrder: true);
        request.GetProperty("required").EnumerateArray().Select(r => r.GetString()).ShouldBe(["customerRelationId"]);

        var position = schemas.GetProperty("OpenTms.Financial.Tariffs.Published.PricePreviewLineDto").GetProperty("properties").GetProperty("linePosition");
        position.GetProperty("type").GetString().ShouldBe("integer");
        position.GetProperty("nullable").GetBoolean().ShouldBeTrue("null for an order-level line");
        typeof(PricePreviewLineDto).GetProperty(nameof(PricePreviewLineDto.LinePosition))!.PropertyType.ShouldBe(typeof(int?));
    }

    private static JsonElement Contract()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "backend", "OpenTms.slnx")))
        {
            directory = directory.Parent;
        }

        directory.ShouldNotBeNull("the test must run inside the repository");
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory.FullName, "openapi", "opentms-next.json")));
        return document.RootElement.Clone();
    }
}
