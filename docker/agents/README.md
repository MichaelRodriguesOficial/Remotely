# docker/agents

Pasta usada no build da imagem Docker para receber os agents e clientes de desktop
compilados (o mesmo conteúdo de `Server/wwwroot/Content`).

- No GitHub Actions, o job **agents** compila tudo no Windows e o job **docker** baixa o
  resultado aqui antes do `docker build`.
- Num build local, deixe esta pasta só com este README: o Dockerfile baixa o pacote
  `Remotely_Agents.zip` do último release.
- Para usar agents compilados por você: `pwsh Utilities/Build-Agents.ps1 -OutDir docker/agents`.
