namespace OpenTms.Platform.Administration;

public record RoleDeleteCheckDto
{
    public string RoleName { get; init; } = default!;
    public long UserCount { get; init; }
}
