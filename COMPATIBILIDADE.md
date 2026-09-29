# Compatibilidade com a API Horse (Pascal)

Esta versão **replica intencionalmente** os seguintes comportamentos do servidor Lazarus para não quebrar consumidores existentes.

## HTTP

| Item | Comportamento |
|------|----------------|
| Porta | 9000 (configurável via `Urls`) |
| Auth | HTTP Basic em **todas** as rotas |
| JSON negócio | ISO-8859-1, chaves em minúsculas |
| `/ping` | UTF-8, eco do body JSON |

## Quirks legados (não corrigidos na v1)

1. **`POST /vendas/vendas_confirmadas`** — executa a mesma lógica que `POST /financeiro/parcelas_emaberto` (o Pascal chama `onParcelasemaberto`, não `onHistoricodeVendas`).

2. **`POST /financeiro/parcelas_pagas`** — consulta `vw_api_lst_contas_receber_aberto` (mesma view das parcelas em aberto).

3. **`POST /cliente/retorna_cnpj`** — quando o cliente **existe**, retorna `status: "1"` com `erro: "Não existe cliente com esse CNPJ"` (texto invertido no Pascal).

4. **`GET /util/data_servidor`** — o Pascal lê o body JSON mesmo em GET; a API .NET aceita body opcional.

5. **`POST /cliente/cadastra_cnpj`** e **`cadastra_cnpj_free`** — o Pascal não atribui `result` de forma confiável; a API .NET retorna `{}`.

6. **Renovação** — `liberar_cnpj` só renova quando `status` ∈ {3, 8, 14}; `liberar_cnpj_id` aceita {3, 8, 14, 2, 4, 5, 6, 7, 12, 13}.

## Códigos `status` (consulta CNPJ)

| status | Significado |
|--------|-------------|
| 1 | Sem licença ativa |
| 2 | ClickFree — pendência financeira |
| 3 | ClickFree — manutenção em dias |
| 4 | ClickFree — sem pedidos de garantia |
| 5 | Garantia/suporte avulso vencido |
| 6 | Ainda em garantia avulsa |
| 7 | LSIClick — pendência financeira |
| 8 | LSIClick — manutenção em dias |
| 9 | ClickFree — sem licença ativada |
| 10 | CNPJ não encontrado |
| 12 | Gestor em dias |
| 13 | LSIClick — sem pedidos de garantia |
| 14 | ClickFree sem contrato, manutenção em dias |

## Futura v2 (opcional)

- Corrigir mensagens de `retorna_cnpj`
- Ligar `vendas_confirmadas` à view de histórico real
- Implementar cadastro CNPJ com resposta explícita
- Migrar auth para secrets/variáveis sem valor padrão em Development
