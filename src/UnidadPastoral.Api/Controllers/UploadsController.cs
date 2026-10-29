using Microsoft.AspNetCore.Mvc;
using UnidadPastoral.Api.Services;

namespace UnidadPastoral.Api.Controllers;

[ApiController]
[Route("uploads")]
public sealed class UploadsController(UploadService uploads) : ControllerBase
{
    [HttpGet("{**key}")]
    public async Task<IActionResult> Get(string? key, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            return NotFound();
        }

        var file = await uploads.ReadAsync(key, cancellationToken);
        if (file is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "public, max-age=3600";
        return File(file.Value.Content, file.Value.ContentType);
    }
}