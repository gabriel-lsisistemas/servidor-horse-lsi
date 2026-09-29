using Microsoft.AspNetCore.Mvc;
using ServidorHorseLsi.Api.Filters;
using ServidorHorseLsi.Api.Models;
using ServidorHorseLsi.Api.Services;

namespace ServidorHorseLsi.Api.Controllers;

[ApiController]
[Route("financeiro")]
[Iso88591Json]
public sealed class FinanceiroController : ControllerBase
{
    private readonly IFinanceiroService _financeiro;

    public FinanceiroController(IFinanceiroService financeiro) => _financeiro = financeiro;

    [HttpPost("parcelas_emaberto")]
    public async Task<IActionResult> ParcelasEmAberto(CancellationToken cancellationToken)
    {
        var cnpj = await JsonBodyHelper.GetStringAsync(Request, "cnpj") ?? "";
        return Ok(await _financeiro.ParcelasEmAbertoAsync(cnpj, cancellationToken));
    }

    [HttpPost("parcelas_pagas")]
    public async Task<IActionResult> ParcelasPagas(CancellationToken cancellationToken)
    {
        var cnpj = await JsonBodyHelper.GetStringAsync(Request, "cnpj") ?? "";
        return Ok(await _financeiro.ParcelasPagasAsync(cnpj, cancellationToken));
    }
}
