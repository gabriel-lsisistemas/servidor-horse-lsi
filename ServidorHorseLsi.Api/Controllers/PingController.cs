using Microsoft.AspNetCore.Mvc;
using ServidorHorseLsi.Api.Models;

namespace ServidorHorseLsi.Api.Controllers;

[ApiController]
[Route("ping")]
public sealed class PingController : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Post()
    {
        var body = await JsonBodyHelper.ReadBodyAsObjectAsync(Request);
        return Ok(body ?? new { });
    }
}
