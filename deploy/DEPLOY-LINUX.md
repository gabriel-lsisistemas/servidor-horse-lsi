{
    "sucesso": false,
    "erro": "Erro ACBr (DPS): Código: X999 - Erro de Conexão: Não foi possível carregar a biblioteca LibXml2."
}# Deploy Linux — API Horse + API NFSe

Dois serviços C# no mesmo servidor:

| Serviço | Pasta | Porta | systemd |
|---------|-------|-------|---------|
| **servidor-horse** (CNPJ/licença/SQL) | `/opt/servidorHorse/publish-linux` | 9001 | `servidor-horse` |
| **API NFS** (NFSe / ACBr) | `/opt/servidorHorse/publish-api-nfse` | 5000 | `api-nfse` |

Apache (opcional):

- `/api/horse/` → `127.0.0.1:9001`
- `/api/` → `127.0.0.1:5000` (inclui `/api/nfse/`)

---

## 0. Tailscale (acesso ao SQL Server)

O banco do servidor Horse fica em outra máquina/rede. O Linux da API precisa estar na **mesma rede Tailscale** para alcançar o SQL na porta **1433**.

### Instalar e conectar

No servidor Linux (como root):

```bash
cd /opt/servidorHorse   # ou onde clonou/copiou o repo
sudo TAILSCALE_AUTH_KEY='SUA_CHAVE' bash deploy/install-tailscale.sh
```

Ou em uma linha (equivalente):

```bash
curl -fsSL https://tailscale.com/install.sh | sh && sudo tailscale up --auth-key=SUA_CHAVE
```

Alternativa com arquivo local (não versionar):

```bash
cp deploy/tailscale.auth.example deploy/tailscale.auth
nano deploy/tailscale.auth
sudo bash deploy/install-tailscale.sh
```

### Verificar

```bash
tailscale status
tailscale ip -4
nc -zv IP_TAILSCALE_DO_SQL 1433
```

No `conexao.conf`, campo **`SERVIDOR=`** deve ser o **IP Tailscale** do servidor SQL (ex.: `100.103.152.60`), não o IP público.

---

## 1. Publicar servidor-horse no Windows

```powershell
dotnet publish ServidorHorseLsi.Api/ServidorHorseLsi.Api.csproj -c Release -r linux-x64 --self-contained false -o publish-linux
copy ServidorHorseLsi.Api\conexao.conf publish-linux\conexao.conf
```

Envie `publish-linux` para `/opt/servidorHorse/publish-linux`.

---

## 2. Publicar API NFS (NFSe) no Windows

```powershell
dotnet publish "ApiNFS - LINUX\ApiNFSe.csproj" -c Release -r linux-x64 --self-contained true -o publish-api-nfse-linux
```

Envie para `/opt/servidorHorse/publish-api-nfse`. Copie também `deploy/openssl-legacy.cnf` para a mesma pasta.

No Linux — a ACBr exige `libacbrnfse64.so` em `ACBrLib/x64` (ou na raiz do publish):

```bash
ls -la /opt/servidorHorse/publish-api-nfse/libs/libacbrnfse64.so

sudo mkdir -p /opt/servidorHorse/publish-api-nfse/ACBrLib/x64
sudo cp -a /opt/servidorHorse/publish-api-nfse/libs/libacbrnfse64.so \
  /opt/servidorHorse/publish-api-nfse/ACBrLib/x64/
sudo cp -a /opt/servidorHorse/publish-api-nfse/libs/libacbrnfse64.so \
  /opt/servidorHorse/publish-api-nfse/

chmod +x /opt/servidorHorse/publish-api-nfse/ApiNFSe
chmod +x /opt/servidorHorse/publish-api-nfse/libacbrnfse64.so
chmod +x /opt/servidorHorse/publish-api-nfse/ACBrLib/x64/libacbrnfse64.so

# Dependências: GTK (lib atual) + Xvfb + XML/SSL
# Atenção: libxml2.dll / libssl*.dll na pasta libs/ são de Windows e NÃO servem no Linux.
sudo apt install -y libxml2 libxml2-dev libxslt1.1 libssl3 libgtk2.0-0 xvfb

# ACBr procura libxml2.so (sem versão) — garanta o symlink
sudo ln -sf /usr/lib/x86_64-linux-gnu/libxml2.so.2 /usr/lib/x86_64-linux-gnu/libxml2.so
ln -sf /usr/lib/x86_64-linux-gnu/libxml2.so.2 /opt/servidorHorse/publish-api-nfse/libxml2.so
ln -sf /usr/lib/x86_64-linux-gnu/libxml2.so.2 /opt/servidorHorse/publish-api-nfse/ACBrLib/x64/libxml2.so
ln -sf /usr/lib/x86_64-linux-gnu/libxml2.so.2 /opt/servidorHorse/publish-api-nfse/libs/libxml2.so

# OpenSSL 3 — PFX antigos (erro 0308010C unsupported)
sudo cp deploy/openssl-legacy.cnf /opt/servidorHorse/publish-api-nfse/openssl-legacy.cnf
```

Se o log ACBr mostrar `digital envelope routines::unsupported`, o `OPENSSL_CONF` com provider legacy está faltando no systemd.

---

## 3. conexao.conf (servidor-horse)

Edite `/opt/servidorHorse/publish-linux/conexao.conf`:

```ini
[GESTOR]
SERVIDOR=100.103.152.60
BANCO=CLICK
USUARIO=sa
SENHA=sys
TIPO_BANCO=SQLSERVER
```

Use o IP **Tailscale** do SQL em `SERVIDOR`. Seções ausentes são ignoradas; incompletas impedem a subida da API.

---

## 4. Runtime .NET 8 no Linux

```bash
sudo apt update
sudo apt install -y aspnetcore-runtime-8.0
dotnet --list-runtimes
```

Não use `dotnet publish` no servidor se o SDK falhar — publique no Windows.

---

## 5. Serviços systemd

### servidor-horse (9001)

```bash
sudo cp deploy/servidor-horse.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable servidor-horse
sudo systemctl start servidor-horse
sudo systemctl status servidor-horse
```

### API NFS (5000)

A `libacbrnfse64.so` atual é build **GTK** e exige display (mesmo headless). Instale:

```bash
sudo apt install -y xvfb libgtk2.0-0
```

```bash
sudo cp deploy/api-nfse.service /etc/systemd/system/
# ou:
sudo tee /etc/systemd/system/api-nfse.service > /dev/null <<'EOF'
[Unit]
Description=API NFS NFSe (ACBr)
After=network.target

[Service]
WorkingDirectory=/opt/servidorHorse/publish-api-nfse
ExecStart=/usr/bin/xvfb-run -a /opt/servidorHorse/publish-api-nfse/ApiNFSe
Restart=always
RestartSec=10
User=root
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=LD_LIBRARY_PATH=/opt/servidorHorse/publish-api-nfse:/opt/servidorHorse/publish-api-nfse/ACBrLib/x64:/opt/servidorHorse/publish-api-nfse/libs
Environment=OPENSSL_CONF=/opt/servidorHorse/publish-api-nfse/openssl-legacy.cnf

[Install]
WantedBy=multi-user.target
EOF

sudo systemctl daemon-reload
sudo systemctl enable api-nfse
sudo systemctl restart api-nfse
sudo systemctl status api-nfse
```

Se o log mostrar `cannot open display:`, o `xvfb-run` não está no `ExecStart`.

Logs:

```bash
sudo journalctl -u servidor-horse -f
sudo journalctl -u api-nfse -f
```

---

## 6. Testes

### Horse (9001)

```bash
curl -s -u "admin.lsisistemas:SUA_SENHA" \
  -X POST http://127.0.0.1:9001/ping \
  -H "Content-Type: application/json" \
  -d '{"teste":"ok"}'
```

### NFSe (5000)

```bash
curl -s http://127.0.0.1:5000/swagger/index.html | head
```

### Portas ativas

```bash
sudo ss -tlnp | grep -E '5000|9001'
```

---

## 7. Apache (proxy externo)

Copie `deploy/apache-proxy.conf` para os VirtualHosts. **`/api/horse/` antes de `/api/`**.

```bash
sudo apache2ctl configtest
sudo systemctl reload apache2
```

---

## 8. PHP no mesmo servidor

```php
// Horse — consulta CNPJ / licença
const HORSE_LSI_BASE_URL = 'http://127.0.0.1:9001/';

// NFSe — via Apache ou direto:
// http://127.0.0.1:5000/api/nfse/...
```

---

## 9. Atualizar deploy

```bash
sudo systemctl stop servidor-horse api-nfse
# substituir arquivos (manter conexao.conf e pasta clientes/ da NFSe)
sudo systemctl start api-nfse servidor-horse
```

---

## Ordem recomendada na primeira instalação

1. Tailscale (`install-tailscale.sh`)
2. Testar `nc -zv IP_SQL_TAILSCALE 1433`
3. Publicar e subir **servidor-horse**
4. Publicar e subir **api-nfse**
5. Configurar Apache
6. Testar Emissor/Cobra
