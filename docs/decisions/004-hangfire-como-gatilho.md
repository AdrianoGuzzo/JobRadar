# ADR-004: Hangfire só como gatilho recorrente

- **Status:** aceito (2026-11-08). Implementação na Fase 6.

## Contexto

O escopo pede Hangfire para o agendamento.

O Hangfire também oferece retry automático de jobs. Mas, se as regras de retry e idempotência dependessem dele, o domínio ficaria acoplado a uma biblioteca, e esse comportamento seria mais difícil de testar.

## Decisão

O Hangfire, armazenado no mesmo PostgreSQL, só dispara três tarefas recorrentes:
- `CollectJobs`;
- `AnalyzePendingJobs`;
- `NotifyMatches`.

O intervalo vem de `Monitoring__Interval`.

O retry automático do Hangfire fica desligado nessas tarefas (`AutomaticRetry(Attempts = 0)`). O estado de tentativas está nas nossas tabelas ([ADR-002](002-idempotencia-e-notificacao-unica.md)).

`DisableConcurrentExecution` é só uma defesa extra. A garantia real contra duplicidade é a do banco.

O dashboard do Hangfire não entra no MVP, porque exigiria ASP.NET.

## Consequências

- Trocar o Hangfire por um `PeriodicTimer` não muda nenhuma regra de negócio.
- As regras de retry são testadas sem o Hangfire.
