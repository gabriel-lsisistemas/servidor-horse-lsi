using Microsoft.AspNetCore.Mvc;
using ServidorHorseLsi.Api.Filters;
using ServidorHorseLsi.Api.Models;
using ServidorHorseLsi.Api.Services;

namespace ServidorHorseLsi.Api.Controllers;

[ApiController]
[Route("licenca")]
[Iso88591Json]
public sealed class LicencaController : ControllerBase
{
    private readonly ILicencaService _licenca;

    public LicencaController(ILicencaService licenca) => _licenca = licenca;

    [HttpPost("liberar_cnpj")]
    public async Task<IActionResult> LiberarCnpj(CancellationToken cancellationToken)
    {
        var cnpj = await JsonBodyHelper.GetStringAsync(Request, "cnpj") ?? "";
        var sistema = await JsonBodyHelper.GetStringAsync(Request, "sistema") ?? "";
        return Ok(await _licenca.LiberarCnpjAsync(cnpj, sistema, cancellationToken));
    }

    [HttpPost("liberar_cnpj_id")]
    public async Task<IActionResult> LiberarCnpjId(CancellationToken cancellationToken)
    {
        var idLicenca = await JsonBodyHelper.GetStringAsync(Request, "id_licenca") ?? "";
        var idRetorno = await JsonBodyHelper.GetStringAsync(Request, "id_retorno") ?? "";
        var sistema = await JsonBodyHelper.GetStringAsync(Request, "sistema") ?? "";
        var cnpj = await JsonBodyHelper.GetStringAsync(Request, "cnpj") ?? "";
        return Ok(await _licenca.LiberarCnpjIdAsync(idLicenca, idRetorno, sistema, cnpj, cancellationToken));
    }

    [HttpPost("atualiza_status_licenca_cnpj")]
    public async Task<IActionResult> AtualizaStatusLicenca(CancellationToken cancellationToken)
    {
        var idLicenca = await JsonBodyHelper.GetIntAsync(Request, "id_licenca");
        var idStatus = await JsonBodyHelper.GetIntAsync(Request, "id_status_licenca");
        return Ok(await _licenca.AtualizaStatusLicencaAsync(idLicenca, idStatus, cancellationToken));
    }

    [HttpPost("existe_licenca_cnpj")]
    public async Task<IActionResult> ExisteLicencaCnpj(CancellationToken cancellationToken)
    {
        var cnpj = await JsonBodyHelper.GetStringAsync(Request, "cnpj") ?? "";
        var idSistema = await JsonBodyHelper.GetStringAsync(Request, "id_sistema") ?? "";
        var local = await JsonBodyHelper.GetStringAsync(Request, "id_local_instalacao") ?? "";
        var tipo = await JsonBodyHelper.GetStringAsync(Request, "id_tipo_licenca") ?? "";
        return Ok(await _licenca.ExisteLicencaCnpjAsync(cnpj, idSistema, local, tipo, cancellationToken));
    }

    [HttpPost("ativa_licenca_cnpj")]
    public async Task<IActionResult> AtivaLicencaCnpj(CancellationToken cancellationToken)
    {
        var cnpj = await JsonBodyHelper.GetStringAsync(Request, "cnpj") ?? "";
        var idSistema = await JsonBodyHelper.GetStringAsync(Request, "id_sistema") ?? "";
        var local = await JsonBodyHelper.GetStringAsync(Request, "id_local_instalacao") ?? "";
        return Ok(await _licenca.AtivaLicencaCnpjAsync(cnpj, idSistema, local, cancellationToken));
    }

    [HttpPost("existe_licenca_cnpj_free")]
    public async Task<IActionResult> ExisteLicencaCnpjFree(CancellationToken cancellationToken)
    {
        var cnpj = await JsonBodyHelper.GetStringAsync(Request, "cnpj") ?? "";
        var idSistema = await JsonBodyHelper.GetStringAsync(Request, "id_sistema") ?? "";
        var local = await JsonBodyHelper.GetStringAsync(Request, "id_local_instalacao") ?? "";
        var tipo = await JsonBodyHelper.GetStringAsync(Request, "id_tipo_licenca") ?? "";
        return Ok(await _licenca.ExisteLicencaCnpjFreeAsync(cnpj, idSistema, local, tipo, cancellationToken));
    }
}
