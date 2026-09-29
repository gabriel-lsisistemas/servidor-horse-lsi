using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using ServidorHorseLsi.Api.Filters;
using ServidorHorseLsi.Api.Services;

namespace ServidorHorseLsi.Api.Controllers;

[ApiController]
[Route("contrato")]
[Iso88591Json]
public sealed class ContratoController : ControllerBase
{
    private readonly IContratoService _contratos;

    public ContratoController(IContratoService contratos) => _contratos = contratos;

    /// <summary>
    /// Atualiza contrato no Click a partir do Cobra (id_click = CONTRATOS.ID).
    /// </summary>
    [HttpPost("atualiza")]
    public async Task<IActionResult> Atualiza(CancellationToken cancellationToken)
    {
        Request.EnableBuffering();
        Request.Body.Position = 0;
        using var reader = new StreamReader(Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var raw = await reader.ReadToEndAsync(cancellationToken);
        Request.Body.Position = 0;

        if (string.IsNullOrWhiteSpace(raw))
            return Ok(await _contratos.AtualizaDoCobraAsync(null, cancellationToken));

        using var body = JsonDocument.Parse(raw);
        return Ok(await _contratos.AtualizaDoCobraAsync(body, cancellationToken));
    }
}
