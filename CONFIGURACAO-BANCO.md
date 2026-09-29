# Mapeamento de bancos (Horse → API .NET)

| Chave API (`ConnectionStrings`) | Seção `conexao.conf` | Banco correto |
|--------------------------------|----------------------|---------------|
| `Gestor` | `[GESTOR]` | **clicklite** |
| `GestorFree` | `[GESTOR_FREE]` | **clickfreelicenca** |
| `GestorLsiClick` | `[GESTOR_LSICLICK]` | **clicklicenca** |

`[GESTOR_NSGESTOR]` / banco **clicklicoem** existe no INI do Horse mas **não é usado** pela API .NET na versão atual.

## Prioridade de configuração

1. Se existir `conexao.conf` na pasta `ServidorHorseLsi.Api`, ele **substitui** o `appsettings`.
2. Caso contrário, usa `ConnectionStrings` do `appsettings.Development.json` (ou `appsettings.json`).

## Erros comuns (seu caso anterior)

| O que estava errado | Correto (INI Horse) |
|---------------------|---------------------|
| `Gestor` → `clicklicoem` | `Gestor` → **clicklite** |
| `GestorLsiClick` → `clicklite` | `GestorLsiClick` → **clicklicenca** |
| Senha `321+_)(s` | Senha **`321+_)(sa`** (faltava o `a`) |

## Como configurar

**Recomendado:** copie o `conexao.conf` do servidor Horse para:

`servidor-horse-lsi-api/ServidorHorseLsi.Api/conexao.conf`

**Alternativa:** use exatamente os valores do INI em `appsettings.Development.json` (já corrigidos no repositório).

Ao subir a API, o log deve mostrar:

```
SQL [Gestor] ... Database=clicklite, User=sa
SQL [GestorFree] ... Database=clickfreelicenca, User=sa
SQL [GestorLsiClick] ... Database=clicklicenca, User=sa
```

E, com `ValidateSqlOnStartup: true`, as três linhas `conexao testada com sucesso`.
