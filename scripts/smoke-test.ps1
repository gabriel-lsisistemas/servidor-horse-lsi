param(
    [string]$BaseUrl = "http://localhost:9000",
    [string]$Username = "admin.lsisistemas",
    [string]$Password = "",
    [string]$Cnpj = "00000000000000"
)

$pair = "{0}:{1}" -f $Username, $Password
$bytes = [System.Text.Encoding]::UTF8.GetBytes($pair)
$basic = [Convert]::ToBase64String($bytes)
$headers = @{
    Authorization = "Basic $basic"
    "Content-Type" = "application/json"
}

function Invoke-Api {
    param([string]$Method, [string]$Path, [string]$Body = "{}")
    $uri = "$BaseUrl$Path"
    try {
        if ($Method -eq "GET") {
            return Invoke-WebRequest -Uri $uri -Method GET -Headers $headers -UseBasicParsing
        }
        return Invoke-WebRequest -Uri $uri -Method POST -Headers $headers -Body $Body -UseBasicParsing
    }
    catch {
        return $_.Exception.Response
    }
}

$endpoints = @(
    @{ Method = "POST"; Path = "/ping"; Body = '{"ping":true}' },
    @{ Method = "GET"; Path = "/util/data_servidor"; Body = $null },
    @{ Method = "POST"; Path = "/cliente/consulta_cnpj"; Body = "{`"cnpj`":`"$Cnpj`",`"id_local_instalacao`":1}" },
    @{ Method = "POST"; Path = "/cliente/retorna_cnpj"; Body = "{`"cnpj`":`"$Cnpj`"}" },
    @{ Method = "POST"; Path = "/cliente/retorna_cnpj_free"; Body = "{`"cnpj`":`"$Cnpj`"}" },
    @{ Method = "POST"; Path = "/cliente/cadastra_cnpj"; Body = "{`"CNPJ`":`"$Cnpj`",`"razao_social`":`"Teste`"}" },
    @{ Method = "POST"; Path = "/cliente/cadastra_cnpj_free"; Body = "{`"CNPJ`":`"$Cnpj`",`"razao_social`":`"Teste`"}" },
    @{ Method = "POST"; Path = "/financeiro/parcelas_emaberto"; Body = "{`"cnpj`":`"$Cnpj`"}" },
    @{ Method = "POST"; Path = "/financeiro/parcelas_pagas"; Body = "{`"cnpj`":`"$Cnpj`"}" },
    @{ Method = "POST"; Path = "/vendas/vendas_confirmadas"; Body = "{`"cnpj`":`"$Cnpj`"}" },
    @{ Method = "POST"; Path = "/licenca/liberar_cnpj"; Body = "{`"cnpj`":`"$Cnpj`",`"sistema`":`"LSICLICK`"}" },
    @{ Method = "POST"; Path = "/licenca/liberar_cnpj_id"; Body = "{`"id_licenca`":`"1`",`"id_retorno`":`"8`",`"sistema`":`"LSICLICK`",`"cnpj`":`"$Cnpj`"}" },
    @{ Method = "POST"; Path = "/licenca/atualiza_status_licenca_cnpj"; Body = "{`"id_licenca`":`"1`",`"id_status_licenca`":`"6`"}" },
    @{ Method = "POST"; Path = "/licenca/existe_licenca_cnpj"; Body = "{`"cnpj`":`"$Cnpj`",`"id_sistema`":`"1`",`"id_local_instalacao`":`"1`",`"id_tipo_licenca`":`"1`"}" },
    @{ Method = "POST"; Path = "/licenca/ativa_licenca_cnpj"; Body = "{`"cnpj`":`"$Cnpj`",`"id_sistema`":`"1`",`"id_local_instalacao`":`"1`"}" },
    @{ Method = "POST"; Path = "/licenca/existe_licenca_cnpj_free"; Body = "{`"cnpj`":`"$Cnpj`",`"id_sistema`":`"1`",`"id_local_instalacao`":`"1`",`"id_tipo_licenca`":`"1`"}" }
)

Write-Host "Smoke test: $BaseUrl" -ForegroundColor Cyan
$fail = 0

foreach ($ep in $endpoints) {
    $body = if ($null -eq $ep.Body) { "{}" } else { $ep.Body }
  if ($ep.Method -eq "GET") {
        $r = Invoke-Api -Method GET -Path $ep.Path
    }
    else {
        $r = Invoke-Api -Method POST -Path $ep.Path -Body $body
    }

    $code = if ($r -is [System.Net.HttpWebResponse]) { [int]$r.StatusCode } else { $r.StatusCode }
    $ok = ($code -ge 200 -and $code -lt 500)
    if (-not $ok) { $fail++ }
    $color = if ($ok) { "Green" } else { "Red" }
    Write-Host ("{0} {1} -> {2}" -f $ep.Method, $ep.Path, $code) -ForegroundColor $color
}

if ($fail -gt 0) {
    Write-Host "$fail endpoint(s) returned unexpected status." -ForegroundColor Red
    exit 1
}

Write-Host "All endpoints reachable (2xx/4xx with auth)." -ForegroundColor Green
