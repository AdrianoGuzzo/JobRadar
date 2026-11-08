# ADR-005: Discord só pela API REST no MVP

- **Status:** aceito e implementado (2026-11-08).

## Contexto

O escopo pede o Discord.Net e um projeto `JobRadar.DiscordBot`.

No MVP só existem notificações automáticas, e publicar uma mensagem num canal não exige a conexão de gateway (websocket) de um bot.

## Decisão

**Pacote:** `Discord.Net.Rest` (3.20.2), sem o gateway. O `DiscordNotificationService` vive no Infrastructure.

**Login:** feito uma vez por processo. O canal fica em cache. Um erro permanente descarta o cliente, e o próximo envio refaz o login.

**Retry:** o retry interno do Discord.Net fica desligado (`RetryMode.AlwaysFail`). A classificação do erro alimenta a política de novas tentativas do JobRadar ([ADR-002](002-idempotencia-e-notificacao-unica.md)):

| Erro | Classificação |
|---|---|
| 400 / 401 / 403 / 404, token inválido | permanente |
| 429, 5xx, timeout, rede | transitório |

**Timeout:** um token próprio cancela a chamada HTTP. O `RequestOptions.Timeout` do Discord.Net não interrompe a requisição, conforme verificado em teste.

**Menções:** sempre `AllowedMentions.None`. O formatador também neutraliza `@` e markdown vindos da vaga.

**Testes:** apontam o `RestClientProvider` para um servidor HTTP local, o `FakeDiscordServer` (spike da tarefa 5.1).

## Consequências

- O projeto `JobRadar.DiscordBot`, com gateway e comandos, nasce na fase de comandos interativos.
- Os favoritos chegam junto com os comandos.
