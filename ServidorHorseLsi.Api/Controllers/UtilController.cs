using Microsoft.AspNetCore.Mvc;
using ServidorHorseLsi.Api.Filters;
using ServidorHorseLsi.Api.Models;
using ServidorHorseLsi.Api.Services;

namespace ServidorHorseLsi.Api.Controllers;

[ApiController]
[Route("util")]
[Iso88591Json]
public sealed class UtilController : ControllerBase
{
    private readonly IUtilService _util;

    public UtilController(IUtilService util) => _util = util;

    [HttpGet("data_servidor")]
    public async Task<IActionResult> DataServidor(CancellationToken cancellationToken)
    {
        _ = await JsonBodyHelper.ReadBodyAsync(Request);
        var result = await _util.DataServidorAsync(cancellationToken);
        return Ok(result);
    }
}
