using System.ComponentModel.DataAnnotations;

namespace OpenTms.Platform.Administration;

/// <summary>A profile picture as bytes: at most <see cref="MaxBytes"/>, one of <see cref="AllowedContentTypes"/>.</summary>
public record ProfilePictureDto
{
    public const int MaxBytes = 2 * 1024 * 1024;
    public static readonly string[] AllowedContentTypes = { "image/png", "image/jpeg", "image/webp" };

    [Required]
    public byte[] Content { get; init; } = default!;

    [Required]
    public string ContentType { get; init; } = default!;
}
