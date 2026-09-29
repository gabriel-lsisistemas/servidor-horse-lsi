using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using ServidorHorseLsi.Api.Filters;
using ServidorHorseLsi.Api.Services;

namespace ServidorHorseLsi.Api.Controllers;

[ApiController]
[Route("contrato")]
[Iso88591Json]
public sealed class ContaClickController : ControllerBase
{
    private readonly IContaClickService _contas;

    public ContaClickController(IContaClickService contas) => _contas = contas;

    /// <summary>
    /// Recebe parcelas do Cobra: apaga as em aberto no Click e gera as novas (INTEGRADO = N).
    /// Nao apaga parcela paga nem parcela vigente Asaas.
    /// </summary>
    [HttpPost("parcelas")]
    public async Task<IActionResult> Parcelas(CancellationToken cancellationToken)
    {
        Request.EnableBuffering();
        Request.Body.Position = 0;
        using var reader = new StreamReader(Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var raw = await reader.ReadToEndAsync(cancellationToken);
        Request.Body.Position = 0;

        if (string.IsNullOrWhiteSpace(raw))
            return Ok(await _contas.AtualizaParcelasDoCobraAsync(null, cancellationToken));

        using var body = JsonDocument.Parse(raw);
        return Ok(await _contas.AtualizaParcelasDoCobraAsync(body, cancellationToken));
    }
}
