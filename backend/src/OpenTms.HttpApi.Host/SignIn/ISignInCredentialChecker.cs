using System.Threading.Tasks;
using OpenTms.Platform.SignIn;

namespace OpenTms.SignIn;

public enum SignInCredentialOutcome
{
    /// <summary>The candidate's user is gone or no longer has this e-mail address (a stale lookup row).</summary>
    NotFound,
    Failed,
    LockedOut,
    /// <summary>The password is valid, but the user may not sign in (inactive, must change the password, unconfirmed).</summary>
    NotAllowed,
    Succeeded,
}

/// <summary>Checks a password for one sign-in candidate in the candidate's own tenant.</summary>
public interface ISignInCredentialChecker
{
    /// <summary>The check without counting a failure: the caller counts it only when the password was valid nowhere.</summary>
    Task<SignInCredentialOutcome> CheckAsync(SignInCandidate candidate, string normalizedEmail, string password);

    /// <summary>Counts a failed sign-in towards the candidate's lockout; true when the user is locked out now.</summary>
    Task<bool> RecordFailureAsync(SignInCandidate candidate);
}
