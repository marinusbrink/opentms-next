using System.Threading.Tasks;

namespace OpenTms.Integrations.Emailing;

/// <summary>What the webhook endpoint answers Postmark (design 64).</summary>
public enum PostmarkWebhookOutcome
{
    /// <summary>Applied, or acknowledged and ignored (another system's mail, a record type the send log does not keep).</summary>
    Accepted,

    /// <summary>Missing or wrong credentials, or no credentials configured.</summary>
    Unauthorized,

    /// <summary>Not a Postmark report.</summary>
    Malformed
}

/// <summary>The ingest side of Postmark's delivery webhooks, for the host's anonymous endpoint (design 64).</summary>
public interface IPostmarkWebhookProcessor
{
    /// <param name="authorizationHeader">The request's Authorization header: Postmark's Basic credentials.</param>
    /// <param name="body">The raw JSON body.</param>
    Task<PostmarkWebhookOutcome> ProcessAsync(string? authorizationHeader, string body);
}
