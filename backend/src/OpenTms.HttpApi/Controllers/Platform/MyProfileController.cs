using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OpenTms.Platform.Accounts;
using OpenTms.Platform.Administration;

namespace OpenTms.Controllers.Platform;

/// <summary>The signed-in user's own settings and profile picture (docs/designs/63-user-menu.md).</summary>
[ApiController]
[Route("api/platform/my-profile")]
[Authorize]
public class MyProfileController : OpenTmsController
{
    private readonly IMyProfileAppService _myProfileAppService;

    public MyProfileController(IMyProfileAppService myProfileAppService)
    {
        _myProfileAppService = myProfileAppService;
    }

    [HttpGet("")]
    [ProducesResponseType(typeof(MyProfileDto), 200)]
    public async Task<ActionResult<MyProfileDto>> GetAsync() => Ok(await _myProfileAppService.GetAsync());

    [HttpPut("")]
    [ProducesResponseType(typeof(MyProfileDto), 200)]
    [ProducesResponseType(400)]
    public async Task<ActionResult<MyProfileDto>> UpdateAsync([FromBody] UpdateMyProfileDto input) =>
        Ok(await _myProfileAppService.UpdateAsync(input));

    /// <summary>The user's own profile picture as an image; 404 when there is none.</summary>
    [HttpGet("picture")]
    [ProducesResponseType(typeof(FileContentResult), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetPictureAsync()
    {
        var picture = await _myProfileAppService.GetPictureAsync();
        if (picture == null)
            return NotFound();
        return File(picture.Content, picture.ContentType);
    }

    /// <summary>Multipart upload of one image file (png, jpeg or webp, at most 2 MB) as the user's own profile picture.</summary>
    [HttpPost("picture")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(ProfilePictureDto.MaxBytes + 64 * 1024)]
    [ProducesResponseType(204)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> SetPictureAsync(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest();

        using var stream = new MemoryStream();
        await file.CopyToAsync(stream);
        await _myProfileAppService.SetPictureAsync(new ProfilePictureDto { Content = stream.ToArray(), ContentType = file.ContentType });
        return NoContent();
    }

    [HttpDelete("picture")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> DeletePictureAsync()
    {
        await _myProfileAppService.DeletePictureAsync();
        return NoContent();
    }
}
