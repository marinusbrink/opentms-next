using System;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTms.Platform.Accounts;
using OpenTms.Platform.Emailing;
using OpenTms.Platform.Localization;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Emailing;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Platform.Administration;

/// <summary>
/// The mails that let a user of the current tenant choose a password through the reset-password page: the invitation
/// of a new user, the reset link an administrator sends and the one a user asks for under "Forgot password?". All
/// refuse up front when e-mail delivery is not on, because the mail is then dropped and nobody would know the user
/// cannot sign in. The mail is queued in the
/// caller's transaction (design 64), so it goes out only with the change that caused it.
/// </summary>
public class UserPasswordMailSender : ITransientDependency
{
    /// <summary>
    /// A fingerprint of the password hash the invitation was sent with: the invitation is pending while the hash is
    /// unchanged. Not the hash itself, and not reversible to it.
    /// </summary>
    public const string InvitationPasswordStampProperty = "InvitationPasswordStamp";

    public ILogger<UserPasswordMailSender> Logger { get; set; } = NullLogger<UserPasswordMailSender>.Instance;

    private readonly PasswordLinkBuilder _passwordLinks;
    private readonly IEmailSender _emailSender;
    private readonly EmailDeliveryAvailability _availability;
    private readonly ICurrentTenant _currentTenant;
    private readonly IStringLocalizer<PlatformResource> _localizer;

    public UserPasswordMailSender(
        PasswordLinkBuilder passwordLinks,
        IEmailSender emailSender,
        EmailDeliveryAvailability availability,
        ICurrentTenant currentTenant,
        IStringLocalizer<PlatformResource> localizer)
    {
        _passwordLinks = passwordLinks;
        _emailSender = emailSender;
        _availability = availability;
        _currentTenant = currentTenant;
        _localizer = localizer;
    }

    /// <summary>Throws a user-friendly error when the mail would not go out; call before changing anything.</summary>
    public async Task EnsureDeliveryAvailableAsync()
    {
        if (!await _availability.IsAvailableAsync())
            throw new UserFriendlyException(_localizer["Administration:EmailDeliveryNotAvailable"]);
    }

    /// <summary>
    /// Whether the user was invited and has not chosen a password since: any password change (the link, an
    /// administrator's reset, the user's own change) replaces the random password the invitation was sent with.
    /// </summary>
    public static bool IsInvitationPending(IdentityUser user)
    {
        var stamp = user.GetProperty<string?>(InvitationPasswordStampProperty);
        return stamp != null && stamp == PasswordStamp(user);
    }

    /// <summary>Sends the invitation and stamps it on the user; the caller saves the user.</summary>
    public async Task SendInvitationAsync(IdentityUser user)
    {
        await EnsureDeliveryAvailableAsync();
        var link = await _passwordLinks.BuildAsync(user, PasswordLinkKind.Invitation);
        var organisation = Organisation();

        await _emailSender.SendAsync(
            user.Email,
            _localizer["Administration:InvitationSubject", _currentTenant.Name ?? "OpenTMS"],
            _localizer["Administration:InvitationBody", GreetingName(user), organisation, WebUtility.HtmlEncode(user.Email), link],
            isBodyHtml: true);

        user.SetProperty(InvitationPasswordStampProperty, PasswordStamp(user));

        /* No address in the log (constitution rule 5). */
        Logger.LogInformation("Invitation sent to user {UserId}", user.Id);
    }

    public async Task SendPasswordResetLinkAsync(IdentityUser user)
    {
        await EnsureDeliveryAvailableAsync();
        var link = await _passwordLinks.BuildAsync(user, PasswordLinkKind.Reset);

        await _emailSender.SendAsync(
            user.Email,
            _localizer["Administration:PasswordResetLinkSubject"],
            _localizer["Administration:PasswordResetLinkBody", GreetingName(user), WebUtility.HtmlEncode(user.Email), link],
            isBodyHtml: true);

        Logger.LogInformation("Password reset link sent to user {UserId}", user.Id);
    }

    /// <summary>The link a user asked for on the sign-in page ("Forgot password?"), naming the organisation it is for.</summary>
    public async Task SendForgotPasswordLinkAsync(IdentityUser user)
    {
        await EnsureDeliveryAvailableAsync();
        var link = await _passwordLinks.BuildAsync(user, PasswordLinkKind.Reset);

        await _emailSender.SendAsync(
            user.Email,
            _localizer["Accounts:ForgotPasswordSubject"],
            _localizer["Accounts:ForgotPasswordBody", GreetingName(user), Organisation(), WebUtility.HtmlEncode(user.Email), link],
            isBodyHtml: true);

        Logger.LogInformation("Forgot-password link sent to user {UserId}", user.Id);
    }

    private string Organisation() => WebUtility.HtmlEncode(_currentTenant.Name ?? "OpenTMS");

    private static string GreetingName(IdentityUser user) =>
        WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(user.Name) ? user.UserName : user.Name.Trim());

    private static string PasswordStamp(IdentityUser user) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(user.PasswordHash ?? string.Empty)))[..16];
}
