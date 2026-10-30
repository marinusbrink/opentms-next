using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using OpenTms.Financial;
using OpenTms.Financial.Permissions;
using OpenTms.Financial.Tariffs;
using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.Permissions;
using OpenTms.Platform.Features;
using Shouldly;
using Volo.Abp.Features;
using Xunit;

namespace OpenTms.PlanningExecution;

/// <summary>
/// Design 61 "Order page review" points 3–5, build notes 1, 3 and 4: the attributes of the new commands and reads (read by
/// reflection: the SQLite tests run with authorization always allowed, so a missing or wrong permission is caught here),
/// the HTTP mapping of the new error codes, and the committed API contract (<c>split-at-address</c> and
/// <c>remove-split</c> as POST, the order's cost read with <c>Side</c>, the lookup's <c>basis</c>, the new DTO fields).
/// Risk class: Critical (authorization), High (API contract).
/// </summary>
public class OrderPageReviewContractTests
{
    private static MethodInfo Method(Type type, string name)
        => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Single(m => m.Name == name);

    [Theory]
    [InlineData(nameof(LegAppService.SplitAtAddressAsync))]
    [InlineData(nameof(LegAppService.UndoSplitAtAddressAsync))]
    public void The_split_commands_need_Legs_Update_and_the_Trips_node_like_the_other_leg_endpoints(string name)
    {
        var method = Method(typeof(LegAppService), name);

        method.GetCustomAttribute<AuthorizeAttribute>().ShouldNotBeNull().Policy.ShouldBe(PlanningExecutionPermissions.Legs.Update);
        method.GetCustomAttribute<AllowAnonymousAttribute>().ShouldBeNull();
        var feature = method.GetCustomAttribute<RequiresFeatureAttribute>().ShouldNotBeNull();
        feature.Features.ShouldBe([AppFeatures.Transport, AppFeatures.TransportApp.Trips]);
        feature.RequiresAll.ShouldBeTrue();
        method.IsVirtual.ShouldBeTrue("ABP's interceptors need a virtual method");
        var update = Method(typeof(LegAppService), nameof(LegAppService.UpdateAsync)).GetCustomAttribute<RequiresFeatureAttribute>().ShouldNotBeNull();
        feature.Features.ShouldBe(update.Features, "feature as the other leg endpoints");
    }

    [Theory]
    [InlineData(nameof(RateLineAppService.GetAsync), FinancialPermissions.RateLines.Default)]
    [InlineData(nameof(RateLineAppService.CreateManualAsync), FinancialPermissions.RateLines.Update)]
    [InlineData(nameof(RateLineAppService.UpdateAsync), FinancialPermissions.RateLines.Update)]
    [InlineData(nameof(RateLineAppService.DeleteAsync), FinancialPermissions.RateLines.Update)]
    public void The_purchase_price_read_and_commands_carry_the_rate_line_permissions(string name, string permission)
    {
        Method(typeof(RateLineAppService), name).GetCustomAttribute<AuthorizeAttribute>().ShouldNotBeNull().Policy.ShouldBe(permission);
    }

    [Fact]
    public void The_new_error_codes_travel_with_the_documented_status()
    {
        FinancialErrorCodes.ConflictCodes.ShouldContain(FinancialErrorCodes.LegNotOnTrip);
        foreach (var conflict in new[] { PlanningExecutionErrorCodes.LegOnTrip, PlanningExecutionErrorCodes.OrderCancelled, PlanningExecutionErrorCodes.LegNotSplitAtAddress, PlanningExecutionErrorCodes.SplitAtAddressNested })
        {
            PlanningExecutionErrorCodes.ConflictCodes.ShouldContain(conflict, "409");
            PlanningExecutionErrorCodes.BadRequestCodes.ShouldNotContain(conflict);
        }

        foreach (var badRequest in new[] { PlanningExecutionErrorCodes.SplitAddressIsLegEnd, PlanningExecutionErrorCodes.SplitDateOutsideLeg })
        {
            PlanningExecutionErrorCodes.BadRequestCodes.ShouldContain(badRequest, "400");
            PlanningExecutionErrorCodes.ConflictCodes.ShouldNotContain(badRequest);
        }

        ((int)LegSource.SplitAtAddress).ShouldBe(6);
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

    private static string[] Properties(JsonElement contract, string schema)
        => contract.GetProperty("components").GetProperty("schemas").GetProperty(schema).GetProperty("properties").EnumerateObject().Select(p => p.Name).ToArray();

    [Fact]
    public void The_committed_contract_exposes_split_and_remove_split_as_POST_on_the_leg()
    {
        var paths = Contract().GetProperty("paths");

        var split = paths.GetProperty("/api/planning/leg/{id}/split-at-address");
        split.EnumerateObject().Select(p => p.Name).ShouldBe(["post"]);
        split.GetProperty("post").GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString()
            .ShouldBe("#/components/schemas/OpenTms.PlanningExecution.Legs.LegSplitAtAddressDto");
        var splitResult = split.GetProperty("post").GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema");
        splitResult.GetProperty("type").GetString().ShouldBe("array", "the two new legs");

        var remove = paths.GetProperty("/api/planning/leg/{id}/remove-split");
        remove.EnumerateObject().Select(p => p.Name).ShouldBe(["post"], "not a DELETE: the host maps UndoSplitAtAddressAsync to remove-split");
        remove.GetProperty("post").GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString()
            .ShouldBe("#/components/schemas/OpenTms.PlanningExecution.Legs.LegDto");
        paths.EnumerateObject().ShouldNotContain(p => p.Name.Contains("undo-split", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_committed_contract_carries_the_side_and_basis_filters_and_the_new_fields()
    {
        var contract = Contract();
        var paths = contract.GetProperty("paths");

        paths.GetProperty("/api/financial/rate-line").GetProperty("get").GetProperty("parameters").EnumerateArray().Select(p => p.GetProperty("name").GetString())
            .ShouldBe(["OrderId", "TripId", "Side"]);
        paths.GetProperty("/api/financial/price-list/lookup").GetProperty("get").GetProperty("parameters").EnumerateArray().Select(p => p.GetProperty("name").GetString())
            .ShouldContain("basis");

        Properties(contract, "OpenTms.Financial.Tariffs.RateLineDto").ShouldContain("anchorTripId");
        Properties(contract, "OpenTms.Financial.Tariffs.RateLineDto").ShouldContain("tripNumber");
        Properties(contract, "OpenTms.Financial.Tariffs.RateLineDto").ShouldContain("legOrderLineId");
        Properties(contract, "OpenTms.PlanningExecution.Legs.LegSplitAtAddressDto").ShouldBe(["relationId", "relationAddressId", "arrivalWaypointTypeId", "departureWaypointTypeId", "date"], ignoreOrder: true);
        Properties(contract, "OpenTms.Orders.TransportOrders.TransportOrderFinancialTermsInputDto").ShouldContain("chargedBases");
        Properties(contract, "OpenTms.Orders.TransportOrders.TransportOrderFinancialTermsDto").ShouldContain("chargedBases");
        Properties(contract, "OpenTms.Orders.TransportOrders.TransportOrderFinancialTermsDto").ShouldContain("priceListBases");
        Properties(contract, "OpenTms.Financial.Invoicing.RelationFinancialSettingsDto").ShouldContain("revenueChargedBases");
        Properties(contract, "OpenTms.Financial.Invoicing.RelationFinancialSettingsUpsertDto").ShouldContain("revenueChargedBases");
        Properties(contract, "OpenTms.Financial.Invoicing.RelationPriceListDto").ShouldContain("basis");
    }
}
