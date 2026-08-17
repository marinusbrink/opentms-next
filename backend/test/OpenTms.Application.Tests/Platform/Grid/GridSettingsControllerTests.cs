using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenTms.Controllers.Platform;
using OpenTms.Platform.Grid;
using Shouldly;
using Xunit;

namespace OpenTms.Platform.Grid;

/// <summary>
/// Controller-layer unit tests for GridSettingsController.
/// Risk class: High (§Test risk analysis — GridSettingsController HTTP layer).
///
/// These tests instantiate the controller directly (without ABP DI) and verify:
///   1. The [Authorize] attribute is present (auth guard, design §Security quickscan).
///   2. Invalid gridId format → 400 BadRequest (controller-level guard independent
///      of the app-service validation).
///   3. Null service result → 404 NotFound (GET only).
///   4. Non-null service result → 200 OK with the DTO (GET).
///   5. Successful save → 204 NoContent (PUT).
///   6. Successful reset → 204 NoContent (DELETE).
///
/// NOTE: These are unit tests, not HTTP integration tests. The full HTTP pipeline
/// (routing, model binding, auth middleware, ABP exception filter) requires a
/// WebApplicationFactory-based test project that does not exist yet. Until that
/// project is added, HTTP-level integration and contract coverage is a gap.
/// </summary>
public class GridSettingsControllerTests
{
    private readonly FakeGridSettingsAppService _fakeService = new();
    private readonly GridSettingsController _controller;

    public GridSettingsControllerTests()
    {
        _controller = new GridSettingsController(_fakeService);
    }

    // ── [Authorize] attribute ─────────────────────────────────────────────────

    [Fact]
    public void Controller_class_has_Authorize_attribute()
    {
        typeof(GridSettingsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .ShouldNotBeEmpty(
                "GridSettingsController must carry [Authorize] — design §Security quickscan: " +
                "authenticated session required, no policy argument.");
    }

    // ── GET — gridId format validation ────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("has/slash")]
    [InlineData("has@special")]
    // 101 chars — one over the {1,100} limit ("ab"×50 + "c" = 101):
    [InlineData("ababababababababababababababababababababababababababababababababababababababababababababababababababababababc")]
    public async Task GetAsync_returns_BadRequest_for_invalid_gridId(string invalidId)
    {
        var result = await _controller.GetAsync(invalidId);
        result.Result.ShouldBeOfType<BadRequestObjectResult>(
            $"expected 400 BadRequest for invalid gridId '{invalidId}'");
    }

    [Fact]
    public async Task GetAsync_accepts_valid_gridId_format()
    {
        _fakeService.GetResult = null;
        var result = await _controller.GetAsync("valid-grid.id_123");
        result.Result.ShouldBeOfType<NotFoundResult>("valid gridId should not produce BadRequest");
    }

    // ── GET — response mapping ────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_returns_NotFound_when_service_returns_null()
    {
        _fakeService.GetResult = null;
        var result = await _controller.GetAsync("orders-grid");
        result.Result.ShouldBeOfType<NotFoundResult>(
            "404 NotFound expected when no settings are saved for the gridId (design §API contract)");
    }

    [Fact]
    public async Task GetAsync_returns_Ok_with_dto_when_settings_exist()
    {
        var dto = new GridSettingsDto
        {
            ColumnStates = new List<ColumnStateDto>
            {
                new() { ColId = "reference", Visible = true, Order = 0 },
            },
        };
        _fakeService.GetResult = dto;

        var result = await _controller.GetAsync("orders-grid");

        var ok = result.Result.ShouldBeOfType<OkObjectResult>();
        ok.Value.ShouldBe(dto);
    }

    // ── PUT — gridId format validation ────────────────────────────────────────

    [Theory]
    [InlineData("has space")]
    [InlineData("has/slash")]
    [InlineData("")]
    public async Task SetAsync_returns_BadRequest_for_invalid_gridId(string invalidId)
    {
        var result = await _controller.SetAsync(invalidId, new GridSettingsDto());
        result.ShouldBeOfType<BadRequestObjectResult>(
            $"expected 400 BadRequest for invalid gridId '{invalidId}'");
    }

    [Fact]
    public async Task SetAsync_returns_NoContent_on_success()
    {
        var result = await _controller.SetAsync("orders-grid", new GridSettingsDto());
        result.ShouldBeOfType<NoContentResult>("204 NoContent expected on successful PUT (design §API contract)");
    }

    // ── DELETE — gridId format validation ─────────────────────────────────────

    [Theory]
    [InlineData("has space")]
    [InlineData("has/slash")]
    [InlineData("")]
    public async Task ResetAsync_returns_BadRequest_for_invalid_gridId(string invalidId)
    {
        var result = await _controller.ResetAsync(invalidId);
        result.ShouldBeOfType<BadRequestObjectResult>(
            $"expected 400 BadRequest for invalid gridId '{invalidId}'");
    }

    [Fact]
    public async Task ResetAsync_returns_NoContent_on_success()
    {
        var result = await _controller.ResetAsync("orders-grid");
        result.ShouldBeOfType<NoContentResult>("204 NoContent expected on successful DELETE (design §API contract)");
    }

    // ── Fake service ──────────────────────────────────────────────────────────

    private sealed class FakeGridSettingsAppService : IGridSettingsAppService
    {
        public GridSettingsDto? GetResult { get; set; }

        public Task<GridSettingsDto?> GetAsync(string gridId) =>
            Task.FromResult(GetResult);

        public Task SetAsync(string gridId, GridSettingsDto input) =>
            Task.CompletedTask;

        public Task ResetAsync(string gridId) =>
            Task.CompletedTask;
    }
}
