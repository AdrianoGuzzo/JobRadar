# JobRadar

Monitoramento de vagas de emprego. O JobRadar recebe alertas de vaga por e-mail (no MVP, o LinkedIn por IMAP, sem scraping), compara cada vaga com o seu perfil profissional usando o **Claude Code CLI** e envia as vagas compatíveis para um canal do **Discord**.

> Status: **Fase 1 (fundação)**. Ainda não há funcionalidade de produto. O plano completo e as decisões estão em [`docs/architecture.md`](docs/architecture.md).

## Pré-requisitos

| Ferramenta | Versão | Para quê |
|---|---|---|
| .NET SDK | 10.0.4xx (fixado no `global.json`) | Build e testes |
| Docker Desktop | 29+ | PostgreSQL local e testes de integração (Testcontainers) |
| PowerShell | 5.1+ | `scripts/test.ps1` |

## Como rodar os testes

```powershell
./scripts/test.ps1            # build Release (avisos são erros) + todas as suítes
./scripts/test.ps1 -UnitOnly  # só os unitários, sem Docker
```

Também dá para rodar direto: `dotnet test --project tests/JobRadar.UnitTests`.

Os testes contra serviços reais (Claude, Discord, IMAP) são marcados como `External` e se pulam sozinhos sem as variáveis `JOBRADAR_EXTERNAL_*`. Eles nunca são obrigatórios.

## Banco local

```powershell
Copy-Item .env.example .env   # e troque a senha
docker compose up -d db       # PostgreSQL 17 em 127.0.0.1:5434
```

## Como o projeto é desenvolvido

- **TDD obrigatório:** RED → GREEN → REFACTOR, um comportamento por vez.
- **Identificadores** em inglês. **Documentação, comentários e commits** em pt-BR (Conventional Commits).
- **Avisos são erros.** As versões de pacote ficam só no `Directory.Packages.props`.
- **Decisões de arquitetura** ficam em [`docs/decisions/`](docs/decisions).
- **Segredos:** nunca são versionados. Use o `.env`, que está no `.gitignore`, e veja o `.env.example`.
