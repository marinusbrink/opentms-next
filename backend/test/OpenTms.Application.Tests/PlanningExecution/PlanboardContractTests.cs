using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using OpenTms.MasterData.Published;
using OpenTms.PlanningExecution.Availability;
using OpenTms.PlanningExecution.Permissions;
using OpenTms.PlanningExecution.Planboard;
using OpenTms.Platform.Features;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Features;
using Xunit;

namespace OpenTms.PlanningExecution;

/// <summary>
/// Design 68 "API contract" and design 69, the contract side: every Planboard read and command carries its permission
/// and feature, the conflicts travel as 409 and the input errors as 400, the committed OpenAPI contract exposes every
/// endpoint the generated client uses (optimize-stops as 202 Accepted), and the Master Data resource query is in-process
/// only. Risk class: High (contract tests on the API).
/// </summary>
public class PlanboardContractTests
{
    private static MethodInfo Method(Type type, string name)
        => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Single(m => m.Name == name);

    [Theory]
    [InlineData(nameof(PlanboardAppService.GetSchedulerAsync), PlanningExecutionPermissions.TransportTrips.Default, null)]
    [InlineData(nameof(PlanboardAppService.GetSchedulerBarsAsync), PlanningExecutionPermissions.TransportTrips.Default, null)]
    [InlineData(nameof(PlanboardAppService.GetMapAsync), PlanningExecutionPermissions.TransportTrips.Default, AppFeatures.PlanboardApp.Map)]
    [InlineData(nameof(PlanboardAppService.GetMapTripsAsync), PlanningExecutionPermissions.TransportTrips.Default, AppFeatures.PlanboardApp.Map)]
    [InlineData(nameof(PlanboardAppService.GetUnplannedNearAsync), PlanningExecutionPermissions.Legs.Default, AppFeatures.PlanboardApp.Map)]
    [InlineData(nameof(PlanboardAppService.GetResourceDriversAsync), PlanningExecutionPermissions.TransportTrips.Default, AppFeatures.PlanboardApp.Resources)]
    [InlineData(nameof(PlanboardAppService.GetResourceVehiclesAsync), PlanningExecutionPermissions.TransportTrips.Default, AppFeatures.PlanboardApp.Resources)]
    [InlineData(nameof(PlanboardAppService.RescheduleAsync), PlanningExecutionPermissions.TransportTrips.Update, null)]
    [InlineData(nameof(PlanboardAppService.ChangeResourceAsync), PlanningExecutionPermissions.TransportTrips.Update, null)]
    [InlineData(nameof(PlanboardAppService.AddDriverAsync), PlanningExecutionPermissions.TransportTrips.Update, null)]
    [InlineData(nameof(PlanboardAppService.SetFieldAsync), PlanningExecutionPermissions.TransportTrips.Update, null)]
    [InlineData(nameof(PlanboardAppService.CreateTripAsync), PlanningExecutionPermissions.TransportTrips.Create, null)]
    [InlineData(nameof(PlanboardAppService.AssignLegsAsync), PlanningExecutionPermissions.TransportTrips.AssignLegs, null)]
    [InlineData(nameof(PlanboardAppService.UnassignLegsAsync), PlanningExecutionPermissions.TransportTrips.AssignLegs, null)]
    [InlineData(nameof(PlanboardAppService.CompleteAsync), PlanningExecutionPermissions.TransportTrips.Complete, null)]
    public void Every_planboard_method_carries_its_permission_and_the_planboard_feature(string name, string permission, string? paneFeature)
    {
        var method = Method(typeof(PlanboardAppService), name);
        method.GetCustomAttribute<AuthorizeAttribute>().ShouldNotBeNull(name).Policy.ShouldBe(permission);
        method.GetCustomAttribute<AllowAnonymousAttribute>().ShouldBeNull(name);
        method.IsVirtual.ShouldBeTrue("ABP's interceptors need a virtual method");
        typeof(PlanboardAppService).GetCustomAttribute<RequiresFeatureAttribute>().ShouldNotBeNull().Features.ShouldBe([AppFeatures.Planboard]);
        var pane = method.GetCustomAttribute<RequiresFeatureAttribute>();
        if (paneFeature == null)
        {
            pane.ShouldBeNull(name);
        }
        else
        {
            pane.ShouldNotBeNull(name).Features.ShouldBe([paneFeature]);
        }
    }

    [Fact]
    public void Availability_needs_the_planboard_and_its_resources_pane_and_the_availability_permissions()
    {
        var feature = typeof(ResourceAvailabilityAppService).GetCustomAttribute<RequiresFeatureAttribute>().ShouldNotBeNull();
        feature.Features.ShouldBe([AppFeatures.Planboard, AppFeatures.PlanboardApp.Resources]);
        feature.RequiresAll.ShouldBeTrue();
        foreach (var read in new[] { nameof(ResourceAvailabilityAppService.GetListAsync), nameof(ResourceAvailabilityAppService.GetAsync) })
        {
            Method(typeof(ResourceAvailabilityAppService), read).GetCustomAttribute<AuthorizeAttribute>().ShouldNotBeNull().Policy.ShouldBe(PlanningExecutionPermissions.ResourceAvailability.Default);
        }

        foreach (var write in new[] { nameof(ResourceAvailabilityAppService.CreateAsync), nameof(ResourceAvailabilityAppService.UpdateAsync), nameof(ResourceAvailabilityAppService.DeleteAsync), nameof(ResourceAvailabilityAppService.UpdatePatternsAsync) })
        {
            Method(typeof(ResourceAvailabilityAppService), write).GetCustomAttribute<AuthorizeAttribute>().ShouldNotBeNull().Policy.ShouldBe(PlanningExecutionPermissions.ResourceAvailability.Manage);
        }
    }

    [Fact]
    public void Stop_optimisation_needs_ManageStops_and_is_reached_only_through_its_own_controller()
    {
        Method(typeof(PlanboardStopOptimizationAppService), nameof(PlanboardStopOptimizationAppService.OptimizeStopsAsync))
            .GetCustomAttribute<AuthorizeAttribute>().ShouldNotBeNull().Policy.ShouldBe(PlanningExecutionPermissions.TransportTrips.ManageStops);
        typeof(PlanboardStopOptimizationAppService).GetCustomAttribute<RemoteServiceAttribute>().ShouldNotBeNull().IsEnabled.ShouldBeFalse();
        typeof(PlanboardStopOptimizationAppService).GetCustomAttribute<RequiresFeatureAttribute>().ShouldNotBeNull().Features.ShouldBe([AppFeatures.Planboard]);
    }

    [Fact]
    public void The_master_data_resource_query_is_in_process_only_and_bounded()
    {
        typeof(MasterDataResourceQuery).GetCustomAttribute<RemoteServiceAttribute>().ShouldNotBeNull().IsEnabled.ShouldBeFalse();
        IMasterDataResourceQuery.MaxTake.ShouldBe(500);
        new MasterDataVehicleResourceQuery().Take.ShouldBe(500);
        new MasterDataDriverResourceQuery().Take.ShouldBe(500);
        typeof(MasterDataDriverResourceDto).GetProperties().Select(p => p.Name).ShouldBe(["Id", "Code", "Name", "EmployerRelationId", "IsArchived"], ignoreOrder: true,
            customMessage: "no phone, e-mail or qualifications leave Master Data (rule 5)");
    }

    [Fact]
    public void The_planboard_errors_travel_with_the_documented_status()
    {
        foreach (var conflict in new[] { PlanningExecutionErrorCodes.ResourceNotOnTrip, PlanningExecutionErrorCodes.NoTripType, PlanningExecutionErrorCodes.StopsNotOptimizable })
        {
            PlanningExecutionErrorCodes.ConflictCodes.ShouldContain(conflict, "409");
            PlanningExecutionErrorCodes.BadRequestCodes.ShouldNotContain(conflict);
        }

        foreach (var badRequest in new[]
                 {
                     PlanningExecutionErrorCodes.InvalidSchedulerWindow, PlanningExecutionErrorCodes.ResourceKindMismatch, PlanningExecutionErrorCodes.TooManyIds,
                     PlanningExecutionErrorCodes.UnknownGroupBy, PlanningExecutionErrorCodes.InvalidUnavailabilityPeriod, PlanningExecutionErrorCodes.InvalidRecurrence,
                     PlanningExecutionErrorCodes.InvalidWorkingPattern, PlanningExecutionErrorCodes.InvalidAvailabilityWindow, PlanningExecutionErrorCodes.InvalidNearbySearch
                 })
        {
            PlanningExecutionErrorCodes.BadRequestCodes.ShouldContain(badRequest, "400");
            PlanningExecutionErrorCodes.ConflictCodes.ShouldNotContain(badRequest);
        }
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

    [Theory]
    [InlineData("/api/planning/planboard/scheduler", "get")]
    [InlineData("/api/planning/planboard/scheduler-bars", "get")]
    [InlineData("/api/planning/planboard/{id}/map", "get")]
    [InlineData("/api/planning/planboard/map-trips", "get")]
    [InlineData("/api/planning/planboard/unplanned-near", "get")]
    [InlineData("/api/planning/planboard/resource-drivers", "get")]
    [InlineData("/api/planning/planboard/resource-vehicles", "get")]
    [InlineData("/api/planning/planboard/{id}/reschedule", "post")]
    [InlineData("/api/planning/planboard/{id}/change-resource", "post")]
    [InlineData("/api/planning/planboard/{id}/driver", "post")]
    [InlineData("/api/planning/planboard/{id}/set-field", "post")]
    [InlineData("/api/planning/planboard/trip", "post")]
    [InlineData("/api/planning/planboard/{id}/assign-legs", "post")]
    [InlineData("/api/planning/planboard/{id}/unassign-legs", "post")]
    [InlineData("/api/planning/planboard/{id}/complete", "post")]
    [InlineData("/api/planning/planboard/{id}/optimize-stops", "post")]
    [InlineData("/api/planning/resource-availability", "get")]
    [InlineData("/api/planning/resource-availability", "post")]
    [InlineData("/api/planning/resource-availability/{id}", "put")]
    [InlineData("/api/planning/resource-availability/{id}", "delete")]
    [InlineData("/api/planning/resource-availability/patterns", "put")]
    [InlineData("/api/planning/leg/groups", "get")]
    [InlineData("/api/planning/leg/{id}/split-line", "post")]
    [InlineData("/api/planning/leg/{id}/return-leg", "post")]
    public void The_committed_contract_exposes_the_planboard_endpoint(string path, string verb)
    {
        Contract().GetProperty("paths").GetProperty(path).TryGetProperty(verb, out _).ShouldBeTrue($"{verb.ToUpperInvariant()} {path}");
    }

    [Fact]
    public void Optimize_stops_answers_202_and_no_master_data_resource_endpoint_exists()
    {
        var paths = Contract().GetProperty("paths");
        paths.GetProperty("/api/planning/planboard/{id}/optimize-stops").GetProperty("post").GetProperty("responses").EnumerateObject().Select(r => r.Name).ShouldContain("202");
        paths.EnumerateObject().ShouldNotContain(p => p.Name.StartsWith("/api/master-data", StringComparison.Ordinal) && p.Name.Contains("resource-query", StringComparison.OrdinalIgnoreCase));
    }
}
