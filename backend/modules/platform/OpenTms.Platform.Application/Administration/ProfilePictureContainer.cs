using Volo.Abp.BlobStoring;

namespace OpenTms.Platform.Administration;

/* The blob container of the users' profile pictures. The blob name is the user id; the content
 * type travels as an extra property on the user. Tenant-scoped like every container. */
[BlobContainerName("profile-pictures")]
public class ProfilePictureContainer
{
}
