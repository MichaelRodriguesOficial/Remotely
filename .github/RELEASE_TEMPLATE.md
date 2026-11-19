## Instalação

**Docker**
```bash
docker pull michechel10/remotely:{{VERSION}}
# ou: ghcr.io/michaelrodriguesoficial/remotely:{{VERSION}}
```
Veja `docker-compose/docker-compose.yml` e `docker-compose/.env.example`.

**Ubuntu (sem Docker)**
```bash
curl -fsSLO https://raw.githubusercontent.com/MichaelRodriguesOficial/Remotely/master/remotely_install.sh
sudo bash remotely_install.sh
```

**Atualizar uma instalação existente:** `sudo ./remotely_upgrade.sh`. O banco é preservado e movido para `AppData`, se preciso.

O pacote `Remotely_Server_Linux-x64.zip` e o SHA-256 são anexados a este release pelo workflow **Release**.

---

