using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenTms.Controllers.Platform;
using OpenTms.Platform.Administration.Logbook;
using OpenTms.Platform.Grid;
using OpenTms.Platform.Permissions;
using Shouldly;
using Xunit;

namespace OpenTms.Platform.Logbook;

/// <summary>
/// Controller-layer unit tests for LogbookAdministrationController (no ABP DI, no HTTP pipeline).
/// Risk class: High — the policy on every action and the query-string → filter mapping decide
/// who sees what. The full HTTP pipeline is not covered (no WebApplicationFactory project yet).
/// </summary>
public class LogbookAdministrationControllerTests
{
    private readonly FakeLogbookAppService _service = new();
    private readonly LogbookAdministrationController _controller;

    public LogbookAdministrationControllerTests()
    {
        _controller = new LogbookAdministrationController(_service);
    }

    [Theory]
    [InlineData(nameof(LogbookAdministrationController.GetTechnicalListAsync), PlatformPermissions.Administration.Logbook.Technical)]
    [InlineData(nameof(LogbookAdministrationController.GetTechnicalAsync), PlatformPermissions.Administration.Logbook.Technical)]
    [InlineData(nameof(LogbookAdministrationController.GetFunctionalListAsync), PlatformPermissions.Administration.Logbook.Functional)]
    [InlineData(nameof(LogbookAdministrationController.GetFunctionalAsync), PlatformPermissions.Administration.Logbook.Functional)]
    public void Every_action_requires_the_logbook_permission(string action, string expectedPolicy)
    {
        var authorize = typeof(LogbookAdministrationController)
            .GetMethod(action, BindingFlags.Public | BindingFlags.Instance)!
            .GetCustomAttributes<AuthorizeAttribute>()
            .ToList();

        authorize.ShouldHaveSingleItem().Policy.ShouldBe(expectedPolicy);
    }

    [Fact]
    public async Task Technical_list_maps_the_query_string_to_the_grid_request_and_the_filter()
    {
        var from = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc);

        var result = await _controller.GetTechnicalListAsync(
            startRow: 10, endRow: 60, wildcardSearch: "orders", sortBy: "executionDuration", sortDir: "desc",
            from: from, to: to, userName: "alice", httpMethod: "POST", url: "/api", httpStatusCode: 500,
            hasException: true, correlationId: "corr-1");

        result.Result.ShouldBeOfType<OkObjectResult>();
        var request = _service.LastTechnicalRequest!;
        request.StartRow.ShouldBe(10);
        request.EndRow.ShouldBe(60);
        request.WildcardSearch.ShouldBe("orders");
        request.SortModels.ShouldHaveSingleItem().ShouldBe(new SortModel("executionDuration", "desc"));

        var filter = _service.LastTechnicalFilter!;
        filter.From.ShouldBe(from);
        filter.To.ShouldBe(to);
        filter.UserName.ShouldBe("alice");
        filter.HttpMethod.ShouldBe("POST");
        filter.Url.ShouldBe("/api");
        filter.HttpStatusCode.ShouldBe(500);
        filter.HasException.ShouldBe(true);
        filter.CorrelationId.ShouldBe("corr-1");
    }

    [Fact]
    public async Task Functional_list_maps_the_query_string_to_the_grid_request_and_the_filter()
    {
        var result = await _controller.GetFunctionalListAsync(
            startRow: 0, endRow: 50, wildcardSearch: null, sortBy: null, sortDir: null,
            from: null, to: null, changeType: "Created", entityTypeFullName: "Order", entityId: "o-1", userName: "bob");

        result.Result.ShouldBeOfType<OkObjectResult>();
        var request = _service.LastFunctionalRequest!;
        request.SortModels.ShouldBeEmpty();
        request.WildcardSearch.ShouldBeNull();

        var filter = _service.LastFunctionalFilter!;
        filter.ChangeType.ShouldBe("Created");
        filter.EntityTypeFullName.ShouldBe("Order");
        filter.EntityId.ShouldBe("o-1");
        filter.UserName.ShouldBe("bob");
    }

    [Fact]
    public async Task Detail_actions_return_the_service_result()
    {
        var id = Guid.NewGuid();

        var technical = await _controller.GetTechnicalAsync(id);
        technical.Result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<TechnicalLogbookDetailDto>().Id.ShouldBe(id);

        var functional = await _controller.GetFunctionalAsync(id);
        functional.Result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<FunctionalLogbookDetailDto>().Id.ShouldBe(id);
    }

    private sealed class FakeLogbookAppService : ILogbookAppService
    {
        public GridRequest? LastTechnicalRequest { get; private set; }
        public TechnicalLogbookFilterDto? LastTechnicalFilter { get; private set; }
        public GridRequest? LastFunctionalRequest { get; private set; }
        public FunctionalLogbookFilterDto? LastFunctionalFilter { get; private set; }

        public Task<GridResponse<TechnicalLogbookRowDto>> GetTechnicalListAsync(GridRequest request, TechnicalLogbookFilterDto filter)
        {
            LastTechnicalRequest = request;
            LastTechnicalFilter = filter;
            return Task.FromResult(new GridResponse<TechnicalLogbookRowDto>());
        }

        public Task<TechnicalLogbookDetailDto> GetTechnicalAsync(Guid id) =>
            Task.FromResult(new TechnicalLogbookDetailDto { Id = id });

        public Task<GridResponse<FunctionalLogbookRowDto>> GetFunctionalListAsync(GridRequest request, FunctionalLogbookFilterDto filter)
        {
            LastFunctionalRequest = request;
            LastFunctionalFilter = filter;
            return Task.FromResult(new GridResponse<FunctionalLogbookRowDto>());
        }

        public Task<FunctionalLogbookDetailDto> GetFunctionalAsync(Guid id) =>
            Task.FromResult(new FunctionalLogbookDetailDto { Id = id, ChangeType = LogbookChangeTypes.Updated, EntityTypeFullName = "X", EntityTypeName = "X" });
    }
}
