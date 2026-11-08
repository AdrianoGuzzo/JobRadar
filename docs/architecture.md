# Arquitetura do JobRadar

## Visão geral

Fluxo do JobRadar:

1. Coleta as vagas de fontes autorizadas. No MVP, a única fonte são os alertas do LinkedIn por e-mail.
2. Normaliza e deduplica as vagas.
3. Analisa a compatibilidade com o perfil do candidato pelo Claude Code CLI.
4. Notifica no Discord as vagas com score ≥ limite (padrão 75).

## Projetos e camadas

```
src/
  JobRadar.Domain          entidades, value objects, políticas (sem dependências)
  JobRadar.Application     casos de uso + portas (IJobSource, IJobAnalyzer,
                           IDiscordNotificationService, repositórios)
  JobRadar.Infrastructure  EF Core/Npgsql, ClaudeCodeJobAnalyzer, fonte LinkedIn (IMAP), Discord
  JobRadar.Worker          host, composição (DI + opções validadas), Serilog, Hangfire, startup
tests/
  JobRadar.UnitTests         domínio, casos de uso com fakes, parser do e-mail, formatação; arquitetura
  JobRadar.IntegrationTests  Postgres e GreenMail (Testcontainers), FakeClaude, Discord falso, ponta a ponta
  JobRadar.FakeClaude        executável dublê do claude
```

**Regra de dependência:**
- Domain ← Application ← Infrastructure ← Worker.
- `tests/JobRadar.UnitTests/Architecture/LayerDependencyTests.cs` falha se Domain ou Application usarem tecnologia de infraestrutura: EF Core, Npgsql, Discord, MailKit, AngleSharp, Hangfire, Serilog ou Hosting.

O projeto `JobRadar.DiscordBot` fica para a fase dos comandos interativos. No MVP, a notificação usa só a API REST do Discord (`Discord.Net.Rest`) a partir do Infrastructure, sem gateway.

## Fluxo de processamento

```
[Hangfire: recorrente]
CollectJobs ──► IJobSource.FetchAsync ──► normaliza ──► upsert Job (dedup) ──► PendingAnalysis
AnalyzePendingJobs ──► reivindica lote (FOR UPDATE SKIP LOCKED) ──► IJobAnalyzer (semáforo N) ──► JobMatch
     falha transitória → Attempts++, NextAttemptAt (backoff) | permanente/esgotou → AnalysisFailed
NotifyMatches ──► score ≥ limite e sem Notification ──► INSERT Notification (unique) ──► Discord
     falha → Notification.Failed + retry; o JobMatch nunca é tocado
```

Cada etapa persiste o seu estado antes de seguir para a próxima. Isso garante três coisas:

- Uma falha do Claude não perde a vaga coletada.
- Uma falha do Discord não apaga a análise.
- Executar de novo não repete trabalho já feito.

Detalhes nos ADRs:

| ADR | Assunto |
|---|---|
| [001](decisions/001-deduplicacao-de-vagas.md) | Deduplicação de vagas |
| [002](decisions/002-idempotencia-e-notificacao-unica.md) | Idempotência, retry e notificação única |
| [003](decisions/003-claude-code-cli-isolado.md) | Execução isolada do Claude Code CLI |
| [004](decisions/004-hangfire-como-gatilho.md) | Hangfire só como gatilho recorrente |
| [005](decisions/005-discord-somente-rest.md) | Discord só pela API REST no MVP |

## Modelo de dados (PostgreSQL, EF Core, só migrations)

| Entidade | Conteúdo principal |
|---|---|
| `CandidateProfile` | Cargos, senioridade, tecnologias obrigatórias e desejáveis, anos mínimos, modalidade, localização, pretensão, idiomas e preferências. Listas em `jsonb`. `Version` é o hash do conteúdo. Importado de `config/profile.json`. |
| `JobSource` | Origem da vaga (semeada com `LinkedIn`). |
| `Job` | Título, empresa, localização, modalidade, URL normalizada, descrição, `ExternalId`, `Fingerprint`, `FoundAt`. Estado da análise: status, tentativas, último erro, próxima tentativa. |
| `JobMatch` | Histórico das análises, único por `(JobId, ProfileVersion)`. Guarda score (0 a 100), recommendation, strengths, gaps, technologyMatches, missingInformation e summary. |
| `Notification` | Única por `(JobId, CandidateProfileId)`. Guarda status, tentativas, lease, último erro, `SentAt` e o id da mensagem no Discord. |
| `JobSearchConfiguration` | Pasta IMAP, `UIDVALIDITY`, último UID lido e `LastCheckedAt`. |

Os campos opcionais de `Job` nunca descartam a vaga. Informação ausente fica explícita em `missingInformation`.

## Configuração (variáveis de ambiente, padrão `Seção__Chave`)

No Docker, o `docker-compose.yml` traduz as variáveis do `.env` (ver `.env.example`) para as abaixo.

| Variável | Uso |
|---|---|
| `ConnectionStrings__JobRadar` | Conexão com o PostgreSQL (obrigatória) |
| `CLAUDE_CODE_OAUTH_TOKEN` | Login do Claude Code (`claude setup-token`). É exigido no container (`Claude__RequireOAuthToken`) |
| `Claude__ExecutablePath`, `Claude__Model`, `Claude__TimeoutSeconds` (180), `Claude__MaxConcurrency` (2), `Claude__MaxTurns` (3), `Claude__UseJsonSchema` (true) | Analisador |
| `Discord__BotToken`, `Discord__ChannelId` | Notificação (obrigatórias) |
| `Imap__Host`, `Imap__Port` (993), `Imap__Security` (SslOnConnect), `Imap__User`, `Imap__Password`, `Imap__Folder` (INBOX), `Imap__LookbackDays` (7), `Imap__MaxMessagesPerRun` (50) | Fonte LinkedIn |
| `Monitoring__CollectCron`, `Monitoring__AnalyzeCron`, `Monitoring__NotifyCron` | Agenda (cron em UTC) |
| `Notification__MinimumScore` (75, de 0 a 100) | Limite de notificação |
| `Analysis__BatchSize`, `Analysis__MaxConcurrency` | Lote de análise |
| `Profile__Path` (`config/profile.json`) | Perfil do candidato |

Toda opção é validada no startup: faltando um valor obrigatório ou com valor fora da faixa, o Worker para com uma mensagem clara.

Os segredos ficam só no `.env`, que está no `.gitignore`. Os logs nunca registram token, senha nem corpo de e-mail. O stderr do Claude e os erros do Discord passam pelo `SecretRedactor` antes de ir para o log ou para o banco.

## Bibliotecas

| Pacote | Por quê |
|---|---|
| xunit.v3, AwesomeAssertions, Microsoft.Testing.Extensions.CodeCoverage | Testes |
| Microsoft.Extensions.TimeProvider.Testing | Backoff e lease sem relógio real |
| Npgsql.EntityFrameworkCore.PostgreSQL, EF Core Design | Persistência |
| Testcontainers.PostgreSql | Postgres e GreenMail reais nos testes de integração |
| Microsoft.Extensions.Hosting, Options.DataAnnotations, Serilog (Hosting, Console, Settings.Configuration) | Host do Worker, opções validadas e logging |
| Discord.Net.Rest | Só a parte REST ([ADR-005](decisions/005-discord-somente-rest.md)) |
| MailKit, AngleSharp | IMAP e parser de HTML do e-mail |
| Hangfire.Core, Hangfire.NetCore, Hangfire.PostgreSql | Gatilho recorrente ([ADR-004](decisions/004-hangfire-como-gatilho.md)) |

Os dublês de teste são fakes escritos à mão, sem biblioteca de mock.

## Estratégia de testes

| Suíte | Conteúdo | Requisitos |
|---|---|---|
| `JobRadar.UnitTests` | Domínio, políticas, casos de uso com fakes, formatação, validação do JSON da IA, arquitetura | Nenhum |
| `JobRadar.IntegrationTests` | Postgres (unicidade, concorrência), IMAP contra o GreenMail, Claude contra o FakeClaude, Discord contra um servidor falso, composição do Worker e ponta a ponta | Docker |
| `Category=External` | Claude, Discord e IMAP reais | Variáveis `JOBRADAR_EXTERNAL_*`. Sem elas, os testes se pulam. |

## Riscos conhecidos

1. **Flags do CLI e formato da saída mudam entre versões.** A imagem fixa a versão do Claude Code, há checagem de versão mínima e testes de contrato contra o FakeClaude.
2. **O token OAuth do Claude expira ou a conta fica sem crédito.** O erro é classificado como permanente: a vaga fica `Failed`, guardada, e pode ser reprocessada ([consultas](consultas.md#reprocessar-depois-de-corrigir-a-causa)). No container, o startup exige o token.
3. **Limite de uso do plano Claude.** A concorrência é baixa, cada vaga é analisada uma vez por versão do perfil e as tentativas são limitadas.
4. **O layout do e-mail do LinkedIn muda.** O parser é tolerante e procura os links `/jobs/view/{id}`. Um alerta sem vagas reconhecíveis gera aviso, não falha. As fixtures atuais são **sintéticas**: acrescente alertas reais anonimizados em `tests/JobRadar.UnitTests/Fixtures/LinkedIn`.
7. **O alerta não traz a descrição da vaga.** A análise é parcial por natureza e marca a descrição como ausente. Buscá-la no site seria scraping.
5. **Prompt injection pela descrição da vaga.** Mitigações no [ADR-003](decisions/003-claude-code-cli-isolado.md).
6. **Duplicação rara no Discord** se o processo morrer entre o envio e a gravação de `Sent` ([ADR-002](decisions/002-idempotencia-e-notificacao-unica.md)).
