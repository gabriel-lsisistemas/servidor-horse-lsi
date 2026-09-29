using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using ServidorHorseLsi.Api.Filters;
using ServidorHorseLsi.Api.Models;
using ServidorHorseLsi.Api.Services;

namespace ServidorHorseLsi.Api.Controllers;

[ApiController]
[Route("cliente")]
[Iso88591Json]
public sealed class ClienteController : ControllerBase
{
    private readonly IClienteConsultaService _consulta;
    private readonly IClienteService _cliente;

    public ClienteController(IClienteConsultaService consulta, IClienteService cliente)
    {
        _consulta = consulta;
        _cliente = cliente;
    }

    [HttpPost("consulta_cnpj")]
    public async Task<IActionResult> ConsultaCnpj(CancellationToken cancellationToken)
    {
        var cnpj = await JsonBodyHelper.GetStringAsync(Request, "cnpj") ?? "";
        var idLocal = await JsonBodyHelper.GetIntAsync(Request, "id_local_instalacao", 1);
        var result = await _consulta.ConsultaCnpjAsync(cnpj, idLocal, cancellationToken);
        return Ok(result);
    }

    [HttpPost("retorna_cnpj")]
    public async Task<IActionResult> RetornaCnpj(CancellationToken cancellationToken)
    {
        var cnpj = await JsonBodyHelper.GetStringAsync(Request, "cnpj") ?? "";
        return Ok(await _cliente.RetornaCnpjAsync(cnpj, cancellationToken));
    }

    [HttpPost("retorna_cnpj_free")]
    public async Task<IActionResult> RetornaCnpjFree(CancellationToken cancellationToken)
    {
        var cnpj = await JsonBodyHelper.GetStringAsync(Request, "cnpj") ?? "";
        return Ok(await _cliente.RetornaCnpjFreeAsync(cnpj, cancellationToken));
    }

    [HttpPost("cadastra_cnpj")]
    public async Task<IActionResult> CadastraCnpj(CancellationToken cancellationToken)
    {
        var doc = await JsonBodyHelper.ReadBodyAsync(Request);
        return Ok(await _cliente.CadastraCnpjAsync(doc, cancellationToken));
    }

    [HttpPost("cadastra_cnpj_free")]
    public async Task<IActionResult> CadastraCnpjFree(CancellationToken cancellationToken)
    {
        var doc = await JsonBodyHelper.ReadBodyAsync(Request);
        return Ok(await _cliente.CadastraCnpjFreeAsync(doc, cancellationToken));
    }
}
