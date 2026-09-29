using Microsoft.AspNetCore.Mvc;
using ServidorHorseLsi.Api.Filters;
using ServidorHorseLsi.Api.Services;

namespace ServidorHorseLsi.Api.Controllers;

[ApiController]
[Route("replicador")]
[Iso88591Json]
public sealed class ReplicadorController : ControllerBase
{
    private readonly ICobraSaasReplicadorService _replicador;

    public ReplicadorController(ICobraSaasReplicadorService replicador) => _replicador = replicador;

    /// <summary>
    /// Replicacao incremental: so contratos ainda sem ID_ORIGINAL (nao enviados ao Cobra).
    /// Use inicial=true ou POST /replicador/cobra_saas/inicial para a carga completa.
    /// </summary>
    [HttpPost("cobra_saas")]
    public async Task<IActionResult> CobraSaas(
        [FromQuery] bool inicial = false,
        CancellationToken cancellationToken = default)
    {
        return Ok(await _replicador.ReplicarAsync(inicial, cancellationToken));
    }

    /// <summary>
    /// Replicacao inicial: reenvia todos os contratos elegiveis (com ou sem ID_ORIGINAL).
    /// </summary>
    [HttpPost("cobra_saas/inicial")]
    public async Task<IActionResult> CobraSaasInicial(CancellationToken cancellationToken)
    {
        return Ok(await _replicador.ReplicarAsync(inicial: true, cancellationToken));
    }
}
