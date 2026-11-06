using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using OpenTms.Integrations.AiConnector;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Controllers;
using Volo.Abp.DependencyInjection;
using Volo.Abp.OpenIddict.Controllers;
using Volo.Abp.Uow;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace OpenTms.AiConnector;

/// <summary>What the connect screen shows.</summary>
public sealed class ConnectScreenModel
{
    public string ToolName { get; init; } = string.Empty;

    /// <summary>Null for a host user.</summary>
    public string? OrganizationName { get; init; }

    public string UserName { get; init; } = string.Empty;

    /// <summary>Set: the refusal replaces Allow and Deny, and no code is issued.</summary>
    public AiConnectorRefusal? Refusal { get; init; }

    /// <summary>The authorize request's parameters; the form posts them back.</summary>
    public IReadOnlyList<KeyValuePair<string, string?>> Parameters { get; init; } = [];
}

/// <summary>
/// <c>/connect/authorize</c> for the approved AI tools (design 74 "Connect flow"); every other client takes ABP's way. ABP's
/// controller reuses a valid permanent authorization and skips consent, which would connect a tool on a second visit with
/// nothing shown; here the connect screen always appears, in the tenant of the session, and checks 1 to 5 run again on Allow.
/// The token's scopes are always exactly <see cref="AiConnectorConsts.Scope"/> and <c>offline_access</c>, whatever was asked
/// for, so its audience is the connector alone and the AI tool always gets a refresh token. The browser session's
/// <c>sid</c> is not carried: signing out in the browser does not end the connection.
/// </summary>
[ReplaceControllers(typeof(AuthorizeController))]
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(AuthorizeController), IncludeSelf = true)]
public class ConnectorAuthorizeController : AuthorizeController
{
    public const string AllowField = "allow";
    public const string DenyField = "deny";
    public const string DifferentAccountField = "different-account";

    private const string ViewPath = "~/Views/AiConnector/Connect.cshtml";

    private static readonly string[] FormOnlyFields = ["__RequestVerificationToken", AllowField, DenyField, DifferentAccountField];

    private static readonly ImmutableArray<string> ConnectorScopes = [AiConnectorConsts.Scope, OpenIddictConstants.Scopes.OfflineAccess];

    public override async Task<IActionResult> HandleAsync()
    {
        var request = await GetOpenIddictServerRequestAsync(HttpContext);
        if (!AiConnectorConsts.IsConnectorClient(request.ClientId))
        {
            return await base.HandleAsync();
        }

        var application = await ApplicationManager.FindByClientIdAsync(request.ClientId!)
                          ?? throw new InvalidOperationException("The client of the request does not exist.");

        /* OpenIddict requires a challenge of a public client through the application's requirement; only S256 will do. */
        if (await ApplicationManager.HasClientTypeAsync(application, OpenIddictConstants.ClientTypes.Public)
            && request.CodeChallengeMethod != OpenIddictConstants.CodeChallengeMethods.Sha256)
        {
            return Refuse(OpenIddictConstants.Errors.InvalidRequest, "PKCE with S256 is required.");
        }

        var session = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var mustSignIn = !session.Succeeded
                         || ((request.HasPromptValue(OpenIddictConstants.PromptValues.Login) || request.MaxAge == 0
                              || (request.MaxAge != null && session.Properties?.IssuedUtc != null
                                                         && TimeProvider.System.GetUtcNow() - session.Properties.IssuedUtc > TimeSpan.FromSeconds(request.MaxAge.Value)))
                             && TempData["IgnoreAuthenticationChallenge"] is null or false);
        if (mustSignIn)
        {
            if (request.HasPromptValue(OpenIddictConstants.PromptValues.None))
            {
                return Refuse(OpenIddictConstants.Errors.LoginRequired, "The user is not logged in.");
            }

            TempData["IgnoreAuthenticationChallenge"] = true;
            return Challenge(new AuthenticationProperties { RedirectUri = AuthorizeUrl(includePrompt: true) });
        }

        /* The screen must appear, and prompt=none forbids showing one. */
        if (request.HasPromptValue(OpenIddictConstants.PromptValues.None))
        {
            return Refuse(OpenIddictConstants.Errors.ConsentRequired, "Interactive user consent is required.");
        }

        var user = await UserManager.GetUserAsync(session.Principal!);
        if (user == null)
        {
            return Challenge(new AuthenticationProperties { RedirectUri = AuthorizeUrl(includePrompt: true) });
        }

        var refusal = await LazyServiceProvider.LazyGetRequiredService<AiConnectorAccessChecker>().CheckAsync(session.Principal!);
        return await ConnectScreenAsync(application, user, refusal);
    }

    public override async Task<IActionResult> HandleCallbackAsync()
    {
        var request = await GetOpenIddictServerRequestAsync(HttpContext);
        if (!AiConnectorConsts.IsConnectorClient(request.ClientId))
        {
            return await base.HandleCallbackAsync();
        }

        if (await HasFormValueAsync(DenyField))
        {
            return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        if (await HasFormValueAsync(DifferentAccountField))
        {
            var restart = AuthorizeUrl(includePrompt: false);
            await SignInManager.SignOutAsync();
            return Redirect(restart);
        }

        if (!await HasFormValueAsync(AllowField))
        {
            return BadRequest();
        }

        var application = await ApplicationManager.FindByClientIdAsync(request.ClientId!)
                          ?? throw new InvalidOperationException("The client of the request does not exist.");
        var user = await UserManager.GetUserAsync(User)
                   ?? throw new InvalidOperationException("The signed-in user does not exist.");

        var refusal = await LazyServiceProvider.LazyGetRequiredService<AiConnectorAccessChecker>().CheckAsync(User);
        if (refusal != null)
        {
            return await ConnectScreenAsync(application, user, refusal);
        }

        /* The row is committed before the code leaves for the AI tool: its first /mcp request looks for it. */
        var authorizationId = await ConnectAsync(application, user, request.ClientId!);

        var principal = await SignInManager.CreateUserPrincipalAsync(user);
        principal.SetScopes(ConnectorScopes);
        principal.SetResources(AiConnectorConsts.Resource);
        principal.SetAuthorizationId(authorizationId);
        await OpenIddictClaimsPrincipalManager.HandleAsync(request, principal);

        Logger.LogInformation(
            "AI connection allowed: authorization {AuthorizationId}, client {ClientId}, user {UserId} (tenant {TenantId})",
            authorizationId, request.ClientId, user.Id, CurrentTenant.Id);
        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// The authorization this connect runs on, its connection recorded. Only a connection that is on keeps its authorization. A
    /// switched-off connection's tokens never work again whatever the host's state, so a connect after that always gets a new
    /// authorization and a new row, and the old row stays off with the old id that <c>/mcp</c> check 6 refuses (design 74).
    /// </summary>
    private async Task<string> ConnectAsync(object application, IdentityUser user, string clientId)
    {
        var recorder = LazyServiceProvider.LazyGetRequiredService<IAiConnectionRecorder>();
        var reusable = await recorder.FindReusableAsync(user.Id, clientId, await FindValidAuthorizationsAsync(application, user));

        /* Refused only when someone switched it off a moment ago: it stays off, and this connect goes on with a new one. */
        if (reusable != null && await recorder.TryRecordAsync(user.Id, clientId, reusable))
        {
            return reusable;
        }

        var created = await CreateAuthorizationAsync(application, user);
        if (!await recorder.TryRecordAsync(user.Id, clientId, created))
        {
            throw new InvalidOperationException($"The new OpenIddict authorization {created} already has a switched-off connection.");
        }

        return created;
    }

    /// <summary>OpenIddict's rows live in the host database (design 74), so this reads in the host's context. This find reads the store, not the entity cache.</summary>
    private async Task<IReadOnlyCollection<string>> FindValidAuthorizationsAsync(object application, IdentityUser user)
    {
        var subject = await UserManager.GetUserIdAsync(user);
        using (CurrentTenant.Change(null))
        {
            var unitOfWorkManager = LazyServiceProvider.LazyGetRequiredService<IUnitOfWorkManager>();
            using var unitOfWork = unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: false), requiresNew: true);
            var applicationId = await ApplicationManager.GetIdAsync(application);
            var ids = new List<string>();
            await foreach (var existing in AuthorizationManager.FindAsync(subject, applicationId, OpenIddictConstants.Statuses.Valid, OpenIddictConstants.AuthorizationTypes.Permanent, ConnectorScopes, default))
            {
                ids.Add((await AuthorizationManager.GetIdAsync(existing))!);
            }

            await unitOfWork.CompleteAsync();
            return ids;
        }
    }

    /// <summary>In the host's context and committed by itself: the tenant's connection row names this authorization next.</summary>
    private async Task<string> CreateAuthorizationAsync(object application, IdentityUser user)
    {
        var subject = await UserManager.GetUserIdAsync(user);
        using (CurrentTenant.Change(null))
        {
            var unitOfWorkManager = LazyServiceProvider.LazyGetRequiredService<IUnitOfWorkManager>();
            using var unitOfWork = unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: true), requiresNew: true);
            var applicationId = await ApplicationManager.GetIdAsync(application);
            var principal = await SignInManager.CreateUserPrincipalAsync(user);
            var authorization = await AuthorizationManager.CreateAsync(principal, subject, applicationId!, OpenIddictConstants.AuthorizationTypes.Permanent, ConnectorScopes);
            var id = await AuthorizationManager.GetIdAsync(authorization);
            await unitOfWork.CompleteAsync();
            return id!;
        }
    }

    private async Task<IActionResult> ConnectScreenAsync(object application, IdentityUser user, AiConnectorRefusal? refusal)
    {
        /* A screen that gives access is no frame for another site. */
        Response.Headers.XFrameOptions = "DENY";
        Response.Headers.ContentSecurityPolicy = "frame-ancestors 'none'";

        return View(ViewPath, new ConnectScreenModel
        {
            ToolName = await ApplicationManager.GetDisplayNameAsync(application) ?? string.Empty,
            OrganizationName = CurrentTenant.Id == null ? null : CurrentTenant.Name,
            UserName = user.Email ?? user.UserName ?? string.Empty,
            Refusal = refusal,
            Parameters = RequestParameters()
                .SelectMany(parameter => parameter.Value.Select(value => new KeyValuePair<string, string?>(parameter.Key, value)))
                .ToList(),
        });
    }

    private IActionResult Refuse(string error, string description) => Forbid(
        new AuthenticationProperties(new Dictionary<string, string?>
        {
            [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
        }),
        OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

    private IEnumerable<KeyValuePair<string, StringValues>> RequestParameters()
    {
        IEnumerable<KeyValuePair<string, StringValues>> parameters = Request.HasFormContentType ? Request.Form : Request.Query;
        return parameters.Where(parameter => !FormOnlyFields.Contains(parameter.Key, StringComparer.Ordinal));
    }

    /// <summary>The authorize request again; without the prompt when "a different account" restarts it, which itself signs out.</summary>
    private string AuthorizeUrl(bool includePrompt)
    {
        var parameters = RequestParameters().Where(parameter => includePrompt || parameter.Key != OpenIddictConstants.Parameters.Prompt);
        return Request.PathBase + "/connect/authorize" + QueryString.Create(parameters);
    }
}
