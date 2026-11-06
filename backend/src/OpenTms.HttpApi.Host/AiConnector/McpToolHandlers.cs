using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OpenIddict.Abstractions;
using OpenTms.Integrations.AiConnector;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Users;
using Volo.Abp.Validation;

namespace OpenTms.AiConnector;

/// <summary>
/// <c>tools/list</c> and <c>tools/call</c> over the catalog of Integrations (design 74 "Tool contract"). The list shows only
/// what this user may call. A failure the model can act on (not allowed, not found, invalid input) is a tool result with
/// <c>isError</c>, stating no data. The log line carries ids, the tool name, the duration and the row count, never the
/// arguments or the result: a search text can hold a name (constitution rule 5).
/// </summary>
public static class McpToolHandlers
{
    private const string NotAllowed = "Not allowed: this user may not use this tool, or the organisation has switched it off.";
    private const string NotFound = "Not found.";
    private const string Failed = "The tool failed. Nothing was changed; try again later.";

    public static async ValueTask<ListToolsResult> ListAsync(RequestContext<ListToolsRequestParams> request, CancellationToken cancellationToken)
    {
        var access = request.Services!.GetRequiredService<IAiConnectorToolAccess>();
        var tools = new List<Tool>();
        foreach (var definition in AiConnectorToolCatalog.All)
        {
            if (await access.IsAllowedAsync(definition))
            {
                tools.Add(new Tool
                {
                    Name = definition.Name,
                    Title = definition.Title,
                    Description = definition.Description,
                    InputSchema = definition.InputSchema,
                    Annotations = new ToolAnnotations
                    {
                        Title = definition.Title,
                        ReadOnlyHint = true,
                        DestructiveHint = false,
                        IdempotentHint = true,
                        OpenWorldHint = false,
                    },
                });
            }
        }

        return new ListToolsResult { Tools = tools };
    }

    public static async ValueTask<CallToolResult> CallAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken)
    {
        var services = request.Services!;
        var name = request.Params!.Name;
        var definition = AiConnectorToolCatalog.Find(name);
        if (definition == null)
        {
            return Error($"Unknown tool {name}.");
        }

        var stopwatch = Stopwatch.StartNew();
        var outcome = "ok";
        var rows = 0;
        try
        {
            if (!await services.GetRequiredService<IAiConnectorToolAccess>().IsAllowedAsync(definition))
            {
                outcome = "not_allowed";
                return Error(NotAllowed);
            }

            object? input;
            try
            {
                input = JsonSerializer.SerializeToElement(request.Params.Arguments ?? new Dictionary<string, JsonElement>(), AiConnectorJson.Options)
                    .Deserialize(definition.InputType, AiConnectorJson.Options);
            }
            catch (JsonException exception)
            {
                outcome = "invalid_input";
                /* The serializer names the JSON path and the expected type, never the value. */
                return Error("Invalid arguments: " + exception.Path);
            }

            if (input == null)
            {
                outcome = "invalid_input";
                return Error("Invalid arguments.");
            }

            var result = await definition.Invoke(services.GetRequiredService<IAiConnectorToolService>(), input);
            rows = result is IAiToolRowCount list ? list.RowCount : 1;
            var element = JsonSerializer.SerializeToElement(result, result.GetType(), AiConnectorJson.Options);
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = element.GetRawText() }],
                StructuredContent = element,
                IsError = false,
            };
        }
        catch (AbpAuthorizationException)
        {
            outcome = "not_allowed";
            return Error(NotAllowed);
        }
        catch (EntityNotFoundException)
        {
            outcome = "not_found";
            return Error(NotFound);
        }
        catch (AbpValidationException exception)
        {
            outcome = "invalid_input";
            /* These messages name the field and the rule, never the value. */
            return Error("Invalid arguments: " + string.Join(" ", exception.ValidationErrors.Select(e => e.ErrorMessage)));
        }
        catch (BusinessException exception)
        {
            outcome = "business_rule";
            return Error("Refused: " + exception.Code);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            outcome = "failed";
            services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(McpToolHandlers)).LogError(
                exception, "AI connector tool {ToolName} failed (tenant {TenantId}, user {UserId})",
                definition.Name, services.GetRequiredService<ICurrentTenant>().Id, services.GetRequiredService<ICurrentUser>().Id);
            return Error(Failed);
        }
        finally
        {
            var caller = request.User?.FindFirst(OpenIddictConstants.Claims.ClientId)?.Value;
            services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(McpToolHandlers)).LogInformation(
                "AI connector tool {ToolName} by user {UserId} (tenant {TenantId}, client {ClientId}): {Outcome}, {RowCount} rows in {DurationMs} ms",
                definition.Name, services.GetRequiredService<ICurrentUser>().Id, services.GetRequiredService<ICurrentTenant>().Id, caller, outcome, rows, stopwatch.ElapsedMilliseconds);
        }
    }

    private static CallToolResult Error(string message) => new()
    {
        Content = [new TextContentBlock { Text = message }],
        IsError = true,
    };
}
