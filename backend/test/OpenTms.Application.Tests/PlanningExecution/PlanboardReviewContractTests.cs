using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using OpenTms.Orders;
using OpenTms.Orders.Published;
using OpenTms.Orders.TransportOrders;
using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.Permissions;
using OpenTms.PlanningExecution.Planboard;
using OpenTms.Platform.Features;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Features;
using Xunit;

namespace OpenTms.PlanningExecution;

/// <summary>
/// Design 74, the contract side: every new endpoint carries its permission and feature, the committed OpenAPI contract exposes it with
/// the DTOs the generated client uses, the conflicts travel as 409 and the input errors as 400, and the Orders splitter is in-process
/// only. Risk class: High (contract tests on the API); the authorization part is Critical.
/// </summary>
public class PlanboardReviewContractTests
{
    private static MethodInfo Method(Type type, string name)
        => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Single(m => m.Name == name);

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

    [Theory]
    [InlineData(nameof(LegAppService.GetLineAsync), PlanningExecutionPermissions.Legs.Default)]
    [InlineData(nameof(LegAppService.UpdateLineAsync), PlanningExecutionPermissions.Legs.Update)]
    [InlineData(nameof(LegAppService.SplitOrderLineAsync), PlanningExecutionPermissions.Legs.Update)]
    public void The_leg_endpoints_carry_their_permission_and_either_app_feature(string name, string permission)
    {
        var method = Method(typeof(LegAppService), name);
        method.GetCustomAttribute<AuthorizeAttribute>().ShouldNotBeNull(name).Policy.ShouldBe(permission);
        method.GetCustomAttribute<AllowAnonymousAttribute>().ShouldBeNull(name);
        method.IsVirtual.ShouldBeTrue("ABP's interceptors need a virtual method");
        method.GetCustomAttribute<RequiresFeatureAttribute>().ShouldNotBeNull(name).Features.ShouldBe([AppFeatures.TransportApp.Trips, AppFeatures.Planboard], ignoreOrder: true);
    }

    [Theory]
    [InlineData(nameof(PlanboardAppService.GetAssignOrderPreviewAsync))]
    [InlineData(nameof(PlanboardAppService.AssignOrderAsync))]
    public void The_order_drop_needs_AssignLegs_and_the_planboard(string name)
    {
        var method = Method(typeof(PlanboardAppService), name);
        method.GetCustomAttribute<AuthorizeAttribute>().ShouldNotBeNull(name).Policy.ShouldBe(PlanningExecutionPermissions.TransportTrips.AssignLegs);
        method.GetCustomAttribute<AllowAnonymousAttribute>().ShouldBeNull(name);
        method.IsVirtual.ShouldBeTrue("ABP's interceptors need a virtual method");
        typeof(PlanboardAppService).GetCustomAttribute<RequiresFeatureAttribute>().ShouldNotBeNull().Features.ShouldBe([AppFeatures.Planboard]);
    }

    [Fact]
    public void The_line_field_is_reached_only_through_its_controller_and_the_splitter_never_over_http()
    {
        typeof(PlanboardOrderLineFieldAppService).GetCustomAttribute<RemoteServiceAttribute>().ShouldNotBeNull().IsEnabled.ShouldBeFalse();
        typeof(OrderLineSplitter).GetCustomAttribute<RemoteServiceAttribute>().ShouldNotBeNull().IsEnabled.ShouldBeFalse();
        typeof(IApplicationService).IsAssignableFrom(typeof(IOrderLineSplitter)).ShouldBeFalse("a published in-process interface, not an application service");
    }

    [Fact]
    public void The_errors_travel_with_the_documented_status()
    {
        foreach (var conflict in new[]
                 {
                     PlanningExecutionErrorCodes.StopHasLegs, PlanningExecutionErrorCodes.LegOnFinalTrip, PlanningExecutionErrorCodes.LegLineNotAChain, PlanningExecutionErrorCodes.LegChainReadOnlyLeg,
                     PlanningExecutionErrorCodes.OrderHasNoLegs, PlanningExecutionErrorCodes.OrderLegsStuck, PlanningExecutionErrorCodes.OrderAlreadyOnTrip
                 })
        {
            PlanningExecutionErrorCodes.ConflictCodes.ShouldContain(conflict, "409");
            PlanningExecutionErrorCodes.BadRequestCodes.ShouldNotContain(conflict);
        }

        foreach (var badRequest in new[]
                 {
                     PlanningExecutionErrorCodes.LegChainEmpty, PlanningExecutionErrorCodes.LegChainIncomplete, PlanningExecutionErrorCodes.LegChainNotContiguous, PlanningExecutionErrorCodes.LegChainDatesBackwards,
                     PlanningExecutionErrorCodes.LegChainEndsFixed, PlanningExecutionErrorCodes.LegChainDuplicateLeg, PlanningExecutionErrorCodes.CreateTripOrderOrLegs
                 })
        {
            PlanningExecutionErrorCodes.BadRequestCodes.ShouldContain(badRequest, "400");
            PlanningExecutionErrorCodes.ConflictCodes.ShouldNotContain(badRequest);
        }

        OrdersErrorCodes.ConflictCodes.ShouldContain(OrdersErrorCodes.LineOnFinalTrip);
        OrdersErrorCodes.BadRequestCodes.ShouldContain(OrdersErrorCodes.InvalidLineSplit);
    }

    [Theory]
    [InlineData("/api/planning/leg/line/{lineId}", "get")]
    [InlineData("/api/planning/leg/line/{lineId}", "put")]
    [InlineData("/api/planning/leg/{id}/split-order-line", "post")]
    [InlineData("/api/planning/planboard/{id}/assign-order-preview", "get")]
    [InlineData("/api/planning/planboard/{id}/assign-order", "post")]
    [InlineData("/api/orders/planboard-order/{id}/field", "put")]
    [InlineData("/api/orders/planboard-order/{id}/line/{lineId}/field", "put")]
    public void The_committed_contract_exposes_the_endpoint(string path, string verb)
    {
        Contract().GetProperty("paths").GetProperty(path).TryGetProperty(verb, out _).ShouldBeTrue($"{verb.ToUpperInvariant()} {path}");
    }

    [Theory]
    [InlineData("LegChainDto", "isChain", "rows", "orderLineId")]
    [InlineData("LegChainRowDto", "leg", "hasActuals", "isReadOnly")]
    [InlineData("LegChainUpdateDto", "rows", "stamps")]
    [InlineData("LegSplitOrderLineResultDto", "newLineId", "remainingQuantity", "newQuantity", "newLegs", "remainingLegs")]
    [InlineData("PlanboardAssignOrderPreviewDto", "legs", "pulledFrom", "stuck", "movableCount")]
    [InlineData("PlanboardAssignOrderResultDto", "moved", "stuck", "pulledFrom", "concurrencyStamp")]
    [InlineData("PlanboardCreateTripDto", "orderId", "legIds")]
    [InlineData("PlanboardOrderFieldResultDto", "customerReference", "internalRemarks", "concurrencyStamp")]
    [InlineData("PlanboardOrderLineFieldResultDto", "transportUnitName", "productName", "temperatureName", "concurrencyStamp")]
    [InlineData("UpdateStopDto", "relationId", "relationAddressId")]
    [InlineData("TripStopDto", "street", "houseNumber", "postalCode", "countryCode", "relationPhone", "relationMobile", "relationEmail")]
    [InlineData("LegDto", "orderConcurrencyStamp", "transportUnitName", "productName", "temperatureName")]
    public void The_committed_contract_has_the_dtos_fields(string dtoSuffix, params string[] properties)
    {
        var schemas = Contract().GetProperty("components").GetProperty("schemas");
        var schema = schemas.EnumerateObject().Single(s => s.Name == dtoSuffix || s.Name.EndsWith("." + dtoSuffix, StringComparison.Ordinal)).Value;
        var names = FieldsOf(schema, schemas);
        foreach (var property in properties)
        {
            names.ShouldContain(property, $"{dtoSuffix}.{property}");
        }
    }

    private static string[] FieldsOf(JsonElement schema, JsonElement schemas)
    {
        var own = schema.TryGetProperty("properties", out var properties) ? properties.EnumerateObject().Select(p => p.Name).ToArray() : [];
        if (!schema.TryGetProperty("allOf", out var parts))
        {
            return own;
        }

        return own.Concat(parts.EnumerateArray().SelectMany(p =>
                p.TryGetProperty("$ref", out var reference)
                    ? FieldsOf(schemas.GetProperty(reference.GetString()!.Split('/').Last()), schemas)
                    : FieldsOf(p, schemas)))
            .ToArray();
    }
}
