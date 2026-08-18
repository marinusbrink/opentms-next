using System.ComponentModel.DataAnnotations;

namespace OpenTms.Platform.Administration;

public record AdministrationResetPasswordDto
{
    [Required]
    public string NewPassword { get; init; } = default!;
}
