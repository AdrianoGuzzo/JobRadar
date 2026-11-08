# ADR-002: Idempotência, retry e notificação única

- **Status:** aceito (2026-11-08). Implementação nas Fases 2 e 3.

## Contexto

As regras de negócio exigem:
- nunca notificar a mesma vaga duas vezes ao mesmo usuário, nem em execuções concorrentes;
- não perder dados quando o Claude ou o Discord falham;
- tentar de novo, de forma controlada, quando a falha é temporária.

## Decisão

**O estado vive nas nossas tabelas.** Cada etapa (coleta, análise, notificação) grava o seu resultado antes de seguir para a próxima.

**Análise:**
- `JobMatch` é único por `(JobId, ProfileVersion)`. Uma vaga só é analisada se não há match para a versão atual do perfil.
- O lote é reivindicado com `FOR UPDATE SKIP LOCKED`, então dois workers não pegam a mesma vaga.
- Falha transitória (timeout, rate limit): `Attempts++` e `NextAttemptAt` com backoff exponencial.
- Falha permanente, ou tentativas esgotadas: status `AnalysisFailed`. A vaga continua no banco.

**Notificação:**
- Só para matches com score ≥ limite (padrão 75).
- Primeiro insere a `Notification`, que é única por `(JobId, CandidateProfileId)`, e depois envia.
- O envio é reivindicado com lease (`Status = Sending`, `LeasedUntil`).
- Uma falha no Discord marca `Failed` e agenda nova tentativa. O `JobMatch` nunca é tocado.

**Mudança no perfil:** gera uma nova `ProfileVersion`. As vagas antigas não são reanalisadas automaticamente (fora do MVP).

## Consequências

- A entrega no Discord é **pelo menos uma vez**. Se o processo morrer entre a resposta do Discord e a gravação de `Sent`, o retry depois do lease pode repetir aquela mensagem.
- Preferimos esse risco raro a perder uma notificação.
