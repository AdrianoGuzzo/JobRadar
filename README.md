# JobRadar

O JobRadar monitora vagas de emprego para você:

- **Fonte:** lê os **alertas de vaga que o LinkedIn manda por e-mail** (IMAP, só leitura, sem scraping).
- **Análise:** compara cada vaga com o seu perfil profissional usando o **Claude Code CLI**.
- **Notificação:** publica no **Discord** as vagas com compatibilidade a partir de 75% (configurável).

Status: **MVP**. Documentação:

- [Arquitetura](docs/architecture.md)
- [Regras de negócio e testes](docs/regras-de-negocio.md)
- [Consultas de operação](docs/consultas.md)
- [Decisões (ADRs)](docs/decisions)

## Como funciona

```
LinkedIn ──e-mail──► sua caixa (pasta dos alertas) ──IMAP──► JobRadar ──► PostgreSQL
                                                              │
                              claude -p (sem ferramentas) ◄───┤
                                                              └──► Discord (score ≥ limite)
```

O Hangfire dispara três tarefas recorrentes:

1. **Coleta,** a cada 15 min: lê os alertas novos e grava as vagas sem duplicar.
2. **Análise,** a cada 2 min: analisa as vagas pendentes, no máximo 2 processos do Claude ao mesmo tempo.
3. **Notificação,** a cada 1 min: envia as vagas compatíveis e refaz as que falharam.

> **Limitação importante:** o e-mail de alerta do LinkedIn **não traz a descrição da vaga**, só título, empresa, local e modalidade. Buscar a descrição no site seria scraping, o que o projeto não faz. Por isso a análise trabalha com informação parcial, e toda notificação mostra "Informações não disponíveis: descrição". Leia o score como triagem e abra o link antes de se candidatar.

## Instalação (Docker)

Pré-requisitos: Docker com Compose, uma conta do Claude (Pro ou Max), um servidor do Discord seu e uma caixa de e-mail com IMAP (Gmail serve).

### 1. Token do Claude Code

Numa máquina com navegador e o Claude Code instalado, rode:

```bash
claude setup-token
```

O comando gera um token de longa duração. Ele vai em `CLAUDE_CODE_OAUTH_TOKEN`, e a análise consome a sua assinatura.

Para testar a conta antes, rode `claude -p "ok"`. Se responder `Credit balance is too low` ou erro de login, resolva isso primeiro: o JobRadar trata esse erro como permanente.

### 2. Bot do Discord

1. Em <https://discord.com/developers/applications>, crie uma aplicação e, em **Bot**, gere o token. Ele vai em `DISCORD_BOT_TOKEN`.
2. Em **OAuth2 → URL Generator**, marque o escopo `bot` e as permissões **View Channel** e **Send Messages**. Abra a URL e adicione o bot ao seu servidor.
3. No Discord, ative **Configurações → Avançado → Modo desenvolvedor**. Depois clique com o botão direito no canal e escolha **Copiar ID**. O id vai em `DISCORD_CHANNEL_ID`.

### 3. Alertas do LinkedIn por e-mail

1. No LinkedIn, crie alertas de vaga (busca → **Ativar alerta**) com entrega por e-mail.
2. Recomendado: no Gmail, crie um filtro para os e-mails de `jobalerts-noreply@linkedin.com` com o marcador `LinkedIn`. Depois defina `IMAP_FOLDER=LinkedIn`.
3. Ative a verificação em duas etapas e gere uma **senha de app** em <https://myaccount.google.com/apppasswords>. Ela vai em `IMAP_PASSWORD`.

O JobRadar abre a pasta só para leitura: não marca como lido, não move e não apaga nada.

### 4. Perfil

```powershell
Copy-Item config/profile.example.json config/profile.json
```

Edite `config/profile.json` com cargos, senioridade, tecnologias obrigatórias e desejáveis, modalidade, localização, idiomas e experiência. O arquivo fica fora do git.

Ao reiniciar o worker, o perfil é reimportado. Se o conteúdo mudou, ele ganha uma nova versão, e as próximas análises usam essa versão.

### 5. Configurar e subir

```powershell
Copy-Item .env.example .env   # preencha os valores
docker compose up -d --build
docker compose logs -f worker
```

No startup, o worker valida a configuração, aplica as migrations, importa o perfil e confere o Claude Code. Se faltar algo, ele para com uma mensagem dizendo o quê.

## Operação

| Tarefa | Como |
|---|---|
| Ver o que está acontecendo | `docker compose logs -f worker` |
| Mudar o limite de score | `NOTIFICATION_MINIMUM_SCORE` no `.env`, depois `docker compose up -d` |
| Mudar a frequência da coleta | `MONITORING_COLLECT_CRON` (cron em UTC) |
| Mudar o perfil | Editar `config/profile.json`, depois `docker compose restart worker` |
| Consultar vagas, análises e notificações | [docs/consultas.md](docs/consultas.md) |
| Reprocessar falhas, depois de corrigir a causa | [docs/consultas.md](docs/consultas.md#reprocessar-depois-de-corrigir-a-causa) |
| Atualizar o Claude Code | Mudar `CLAUDE_CODE_VERSION` no `Dockerfile` (e `ClaudeCliVersionCheck.MinimumVersion`, se preciso), depois `docker compose up -d --build` |

## Desenvolvimento

| Ferramenta | Versão |
|---|---|
| .NET SDK | 10.0.4xx (fixado no `global.json`) |
| Docker Desktop | 29+ (PostgreSQL, GreenMail e testes de integração) |
| PowerShell | 5.1+ (`scripts/test.ps1`) |

```powershell
./scripts/test.ps1            # build Release (avisos são erros) + todas as suítes
./scripts/test.ps1 -UnitOnly  # só os unitários, sem Docker
docker compose up -d db       # só o banco, em 127.0.0.1:5434
```

**Testes External.** Os testes contra serviços reais são marcados `Category=External` e se pulam sozinhos sem as variáveis abaixo:

- Claude: `JOBRADAR_EXTERNAL_CLAUDE=1` e, se o `claude` não estiver no PATH, `JOBRADAR_EXTERNAL_CLAUDE_PATH`.
- Discord: `JOBRADAR_EXTERNAL_DISCORD_TOKEN` e `JOBRADAR_EXTERNAL_DISCORD_CHANNEL`.

**Regras do projeto:**

- **TDD:** RED → GREEN → REFACTOR.
- **Idioma:** identificadores em inglês; documentação, comentários e commits em pt-BR (Conventional Commits).
- **Pacotes:** as versões ficam só no `Directory.Packages.props`.
- **Segredos:** nunca são versionados.
