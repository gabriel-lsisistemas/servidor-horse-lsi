using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using ServidorHorseLsi.Api.Filters;
using ServidorHorseLsi.Api.Models;
using ServidorHorseLsi.Api.Services;

namespace ServidorHorseLsi.Api.Controllers;

[ApiController]
[Route("vendas")]
[Iso88591Json]
public sealed class VendasController : ControllerBase
{
    private readonly IFinanceiroService _financeiro;
    private readonly IVendaClickService _vendaClick;

    public VendasController(IFinanceiroService financeiro, IVendaClickService vendaClick)
    {
        _financeiro = financeiro;
        _vendaClick = vendaClick;
    }

    /// <summary>Legacy: same handler as parcelas_emaberto.</summary>
    [HttpPost("vendas_confirmadas")]
    public async Task<IActionResult> VendasConfirmadas(CancellationToken cancellationToken)
    {
        var cnpj = await JsonBodyHelper.GetStringAsync(Request, "cnpj") ?? "";
        return Ok(await _financeiro.ParcelasEmAbertoAsync(cnpj, cancellationToken));
    }

    /// <summary>
    /// Após pagamento Click Free: insert direto no SQL (sem API do gestor).
    /// </summary>
    [HttpPost("renovar_clickfree")]
    public async Task<IActionResult> RenovarClickfree(CancellationToken cancellationToken)
    {
        Request.EnableBuffering();
        Request.Body.Position = 0;
        using var reader = new StreamReader(Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var raw = await reader.ReadToEndAsync(cancellationToken);
        Request.Body.Position = 0;

        if (string.IsNullOrWhiteSpace(raw))
            return Ok(await _vendaClick.RenovarClickfreeAsync(null, cancellationToken));

        using var body = JsonDocument.Parse(raw);
        return Ok(await _vendaClick.RenovarClickfreeAsync(body, cancellationToken));
    }
}
