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
  JobRadar.Worker          host, DI, Serilog, Hangfire            (Fase 4)
tests/
  JobRadar.UnitTests         domínio e casos de uso com fakes em memória; teste de arquitetura
  JobRadar.IntegrationTests  Postgres (Testcontainers), FakeClaude, .eml, HTTP do Discord (Fase 3)
  JobRadar.FakeClaude        executável dublê do claude (Fase 4)
```

Cada projeto nasce na fase em que o primeiro teste precisa dele.

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

| Variável | Uso | Fase |
|---|---|---|
| `POSTGRES_*` | Docker Compose do banco | 1 |
| `ConnectionStrings__JobRadar` | Conexão do app | 3 |
| `Claude__ExecutablePath`, `Claude__Model`, `Claude__TimeoutSeconds`, `Claude__MaxConcurrency`, `CLAUDE_CODE_OAUTH_TOKEN` | Analisador | 4 |
| `Discord__BotToken`, `Discord__ChannelId` | Notificação | 5 |
| `Imap__Host`, `Imap__User`, `Imap__Password`, `Imap__Folder` | Fonte LinkedIn | 6 |
| `Monitoring__Interval`, `Notification__MinimumScore` (padrão 75) | Agendamento e filtro | 6 |

Os segredos ficam só no `.env`, que está no `.gitignore`. Os logs nunca registram token, senha nem corpo de e-mail.

## Bibliotecas

Cada pacote entra só na fase que precisa dele:

| Pacote | Fase | Por quê |
|---|---|---|
| xunit.v3, AwesomeAssertions, Microsoft.Testing.Extensions.CodeCoverage | 1 | Testes, iguais aos outros projetos do autor |
| Microsoft.Extensions.TimeProvider.Testing | 2 | Backoff e lease sem relógio real |
| Npgsql.EntityFrameworkCore.PostgreSQL, EF Core Design | 3 | Persistência |
| Testcontainers.PostgreSql | 3 | Postgres real nos testes de integração |
| Microsoft.Extensions.Hosting, Serilog (+Hosting, Console, Settings.Configuration) | 4 | Host do Worker e logging |
| Discord.Net.Rest | 5 | Só a parte REST, porque não precisa de gateway |
| MailKit, AngleSharp | 6 | IMAP e parser de HTML do e-mail |
| Hangfire.Core, Hangfire.PostgreSql | 6 | Gatilho recorrente ([ADR-004](decisions/004-hangfire-como-gatilho.md)) |

Os dublês de teste são fakes escritos à mão, sem biblioteca de mock.

## Estratégia de testes

| Suíte | Conteúdo | Requisitos |
|---|---|---|
| `JobRadar.UnitTests` | Domínio, políticas, casos de uso com fakes, formatação, validação do JSON da IA, arquitetura | Nenhum |
| `JobRadar.IntegrationTests` | Postgres (unicidade, concorrência), Claude contra o FakeClaude, parser `.eml`, HTTP do Discord simulado | Docker |
| `Category=External` | Claude, Discord e IMAP reais | Variáveis `JOBRADAR_EXTERNAL_*`. Sem elas, os testes se pulam. |

## Riscos conhecidos

1. **Flags do CLI e formato da saída mudam entre versões.** A imagem fixa a versão do Claude Code, há checagem de versão mínima e testes de contrato contra o FakeClaude.
2. **O token OAuth do Claude expira.** O erro é classificado como permanente, gera alerta no log e as vagas continuam pendentes, sem se perder.
3. **Limite de uso do plano Claude.** A concorrência é baixa, cada vaga é analisada uma vez por versão do perfil e as tentativas são limitadas.
4. **O layout do e-mail do LinkedIn muda.** O parser é tolerante e testado com fixtures reais. Um e-mail que não dá para ler gera aviso, não falha.
5. **Prompt injection pela descrição da vaga.** Mitigações no [ADR-003](decisions/003-claude-code-cli-isolado.md).
6. **Duplicação rara no Discord** se o processo morrer entre o envio e a gravação de `Sent` ([ADR-002](decisions/002-idempotencia-e-notificacao-unica.md)).
