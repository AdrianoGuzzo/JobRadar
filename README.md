# JobRadar

O JobRadar monitora vagas de emprego para você:

- **Fonte:** lê os **alertas de vaga que o LinkedIn manda por e-mail** (IMAP, só leitura, sem scraping).
- **Análise:** compara cada vaga com o seu perfil profissional usando o **Claude Code CLI ou Codex CLI**.
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

### Alternativa: Codex CLI

A análise pode usar **Claude Code CLI** ou **Codex CLI**, sem integração direta com APIs.
Escolha no `.env`:

```dotenv
ANALYSIS_PROVIDER=Codex
CODEX_MODEL=
CODEX_MAX_CONCURRENCY=2
CODEX_TIMEOUT_SECONDS=180
```

Com Codex selecionado, `CLAUDE_CODE_OAUTH_TOKEN` pode ficar vazio.
Faça o login da sua conta ChatGPT no container (uma vez):

```powershell
docker compose build worker
docker compose run --rm --no-deps worker codex login --device-auth
docker compose up -d
```

Siga a URL e o código mostrados pelo CLI. Se necessário, habilite o login por dispositivo
nas configurações de segurança da conta. A autenticação fica no volume `codex-auth`,
fora do repositório, e pode ser renovada repetindo o login. Não remova esse volume
se quiser preservar a sessão.

Para voltar ao Claude, defina `ANALYSIS_PROVIDER=Claude`, preencha o token dele e rode
`docker compose up -d`. A configuração só valida o CLI selecionado.

Fora do Docker, instale Codex CLI 0.169.0 ou superior, execute `codex login`, defina
`Analysis__Provider=Codex` e, se necessário, `Codex__ExecutablePath` apontando para o
binário nativo (`codex.exe` no Windows, sem wrapper `.cmd`).

O Codex roda com `exec`, schema JSON, diretório temporário, sandbox de leitura,
sem configuração pessoal, regras, shell ou busca web e sem persistência de sessões.
A verificação inicial de instalação e login não chama o modelo. As análises reais
consomem os limites da conta selecionada.

### 2. Bot do Discord

1. Em <https://discord.com/developers/applications>, crie uma aplicação e, em **Bot**, gere o token. Ele vai em `DISCORD_BOT_TOKEN`.
2. Em **OAuth2 → URL Generator**, marque o escopo `bot` e as permissões **View Channel** e **Send Messages**. Abra a URL e adicione o bot ao seu servidor.
3. No Discord, ative **Configurações → Avançado → Modo desenvolvedor**. Depois clique com o botão direito no canal e escolha **Copiar ID**. O id vai em `DISCORD_CHANNEL_ID`.

#### Avaliar vagas

1. Instale o bot no servidor com os escopos `bot` e `applications.commands`. Mantenha **Interactions Endpoint URL** vazio no portal do Discord para receber interações pelo Gateway.
2. No `.env`, defina `DISCORD_INTERACTIONS_ENABLED=true`, `DISCORD_GUILD_ID` com o ID do servidor e `DISCORD_ALLOWED_USER_IDS` com os IDs dos usuários autorizados, separados por vírgulas. Fora do Compose, use `Discord__InteractionsEnabled`, `Discord__GuildId` e `Discord__AllowedUserIds`.
3. Execute `docker compose up -d --build`. O startup aplica a migração aditiva `AddJobReviews` ao PostgreSQL e o bot registra `/vagas-aprovadas` no servidor.
4. Em um novo alerta, clique em **Gostei** e confira o estado **Aprovada** e a confirmação privada. Execute `/vagas-aprovadas`. Use a opção `pagina` para avançar, com cinco vagas por página.
5. Execute `docker compose restart worker` e confirme que a vaga continua na lista. Na mesma mensagem, clique em **Descartar** e confirme que a vaga saiu da lista. Repita o clique para verificar a confirmação idempotente. Com uma conta fora da lista autorizada, confira que tanto o botão quanto o comando recusam acesso.

Os botões aparecem nos alertas publicados a partir da ativação. A decisão é compartilhada por vaga. Se houver mais de uma mensagem para a mesma vaga, clicar em uma delas atualiza aquela mensagem. O comando consulta as decisões atuais.

Execute `./scripts/test.ps1` com o Docker ativo para os testes de PostgreSQL e IMAP. Use `./scripts/test.ps1 -UnitOnly` para compilar e executar apenas os testes unitários.

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

### Painel e execução manual

Defina `DASHBOARD_USERNAME` e uma senha em `DASHBOARD_PASSWORD` no `.env` do servidor e execute `docker compose up -d --build worker`.
Abra `http://192.168.100.8:8080/rotinas` (ou o endereço da sua instalação) e entre com essas credenciais.
A porta pode ser alterada por `DASHBOARD_HOST_PORT`. Sem senha configurada, o painel só permite requisições de loopback; o acesso pelo host ao container também exige senha.
Para acesso pela internet, use HTTPS no proxy reverso, pois a autenticação Basic envia as credenciais em cada requisição.

Em **Recurring Jobs / Tarefas recorrentes**, acompanhe as três rotinas, sua agenda e as últimas/próximas execuções.
Selecione a rotina e clique em **Trigger now / Executar agora** para colocá-la imediatamente na fila, sem alterar a agenda:

- `jobradar:collect`: coletar novos alertas de e-mail.
- `jobradar:analyze`: analisar as vagas pendentes com Claude.
- `jobradar:notify`: enviar as notificações elegíveis ao Discord.

Para testar o fluxo completo, dispare a coleta, espere sua conclusão em **Succeeded**, dispare a análise e depois a notificação.
As telas **Enqueued**, **Processing**, **Succeeded** e **Failed** mostram fila, processamento, conclusões e erros; abra uma execução para inspecionar seus detalhes.
Uma execução concluída pode não encontrar itens novos. O botão mantém os checkpoints, os critérios de elegibilidade e as proteções contra duplicidade; falhas permanentes de vagas continuam sendo reprocessadas conforme as [consultas de operação](docs/consultas.md).

O painel usa o [Dashboard oficial do Hangfire](https://docs.hangfire.io/en/latest/configuration/using-dashboard.html), no mesmo processo e banco das rotinas.

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
