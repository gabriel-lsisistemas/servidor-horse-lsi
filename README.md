# Servidor Horse LSI — ASP.NET Core

Reimplementação da API Horse (Lazarus/Pascal) em **ASP.NET Core 8**, mantendo o mesmo contrato HTTP para consumidores existentes (PHP, etc.).

## Requisitos

- .NET 8 SDK
- SQL Server (mesmas views/tabelas do servidor Pascal)
- Porta **9000** (padrão)

## Configuração

### Autenticação (HTTP Basic)

Configure em `appsettings.json` ou variáveis de ambiente:

```json
"BasicAuth": {
  "Username": "admin.lsisistemas",
  "Password": "<sua-senha>"
}
```

Em desenvolvimento, `appsettings.Development.json` pode conter a senha legada. **Não commite senhas de produção.**

### Banco de dados

**Opção A — `conexao.conf`** (compatível com o deploy Pascal): copie o arquivo para a pasta de publicação da API com as seções:

- `[GESTOR]`
- `[GESTOR_FREE]`
- `[GESTOR_LSICLICK]`

**Opção B — `appsettings.json`:**

Use os **mesmos** `SERVIDOR`, `BANCO`, `USUARIO` e `SENHA` do `conexao.conf` que funciona no servidor Horse (não use `ChangeMe` se a senha real for outra).

```json
"ConnectionStrings": {
  "Gestor": "Server=142.44.252.253;Database=NOME_REAL_GESTOR;User Id=321+_)(sa;Password=SENHA_REAL;TrustServerCertificate=True;Encrypt=False",
  "GestorFree": "Server=142.44.252.253;Database=NOME_REAL_FREE;User Id=321+_)(sa;Password=SENHA_REAL;TrustServerCertificate=True;Encrypt=False",
  "GestorLsiClick": "Server=142.44.252.253;Database=NOME_REAL_LSICLICK;User Id=321+_)(sa;Password=SENHA_REAL;TrustServerCertificate=True;Encrypt=False"
}
```

A API força **TCP na porta 1433** (`tcp:host,1433`), igual ao Pascal (`Port := 1433`). Isso evita o erro *Provedor de Pipes Nomeados* em servidor remoto.

**Importante:** se existir `conexao.conf` na pasta da API, ele tem **prioridade** sobre o `appsettings.json`. Para testar só o appsettings, renomeie ou remova `conexao.conf` temporariamente.

Ao subir, o log mostra qual origem foi usada e o `DataSource`/`Database` (sem senha).

Veja `ServidorHorseLsi.Api/conexao.conf.example`.

### Erro de conexão SQL

| Sintoma | Causa comum |
|---------|-------------|
| *Provedor de Pipes Nomeados* | Falta TCP/porta 1433 (corrigido na versão atual) |
| Login failed | Senha errada (`ChangeMe` ≠ senha do `conexao.conf`) |
| Cannot open database | Nome do banco diferente do SSMS / Horse |
| Config ignorada | `conexao.conf` na pasta da API sobrescreve appsettings |

## Executar

```bash
cd servidor-horse-lsi-api/ServidorHorseLsi.Api
dotnet run
```

Publicação:

```bash
dotnet publish -c Release -o ./publish
```

## Endpoints

| Método | Path |
|--------|------|
| POST | `/ping` |
| GET | `/util/data_servidor` |
| POST | `/cliente/consulta_cnpj` |
| POST | `/cliente/retorna_cnpj` |
| POST | `/cliente/retorna_cnpj_free` |
| POST | `/cliente/cadastra_cnpj` |
| POST | `/cliente/cadastra_cnpj_free` |
| POST | `/financeiro/parcelas_emaberto` |
| POST | `/financeiro/parcelas_pagas` |
| POST | `/vendas/vendas_confirmadas` |
| POST | `/licenca/liberar_cnpj` |
| POST | `/licenca/liberar_cnpj_id` |
| POST | `/licenca/atualiza_status_licenca_cnpj` |
| POST | `/licenca/existe_licenca_cnpj` |
| POST | `/licenca/ativa_licenca_cnpj` |
| POST | `/licenca/existe_licenca_cnpj_free` |

Todas as rotas exigem `Authorization: Basic ...`.

Rotas de negócio (`/cliente`, `/financeiro`, `/licenca`, `/util`) respondem JSON com **charset ISO-8859-1** e campos em **minúsculas**, como o Horse original.

### Exemplo (consulta CNPJ)

```http
POST http://localhost:9000/cliente/consulta_cnpj
Content-Type: application/json
Authorization: Basic <base64(user:password)>

{
  "cnpj": "11229442000120",
  "id_local_instalacao": 1
}
```

## Testes

```bash
cd servidor-horse-lsi-api
dotnet test
```

Smoke manual (PowerShell):

```powershell
.\scripts\smoke-test.ps1 -BaseUrl http://localhost:9000 -Username admin.lsisistemas -Password "<senha>"
```

## Cutover (substituir o Pascal)

1. Capturar respostas golden do servidor Pascal em homologação (`consulta_cnpj` por CNPJ de cada `status`).
2. Parar o executável Horse na porta 9000.
3. Publicar e iniciar `ServidorHorseLsi.Api` na mesma porta.
4. Executar `scripts/smoke-test.ps1` e o cliente PHP de produção apontando para o novo host.
5. Manter `conexao.conf` ou migrar connection strings para `appsettings`.

Detalhes de comportamentos legados: [COMPATIBILIDADE.md](COMPATIBILIDADE.md).
