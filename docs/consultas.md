# Consultas de operação

O painel de rotinas está disponível em `/rotinas` (veja o [README](../README.md#painel-e-execução-manual)). As consultas abaixo complementam o painel com detalhes das vagas e notificações no PostgreSQL.

Para abrir uma sessão no banco do compose:

```powershell
docker compose exec db psql -U jobradar -d jobradar
```

Os nomes de tabela e coluna estão em PascalCase, por isso as aspas duplas. Os horários são gravados em UTC.

## Vagas encontradas

```sql
SELECT "FoundAt", "Title", "Company", "Location", "WorkMode", "AnalysisStatus", "Url"
FROM "Jobs"
ORDER BY "FoundAt" DESC
LIMIT 50;
```

## Vagas analisadas, com o score

```sql
SELECT m."AnalyzedAt", m."Score", m."Recommendation", j."Title", j."Company", m."Summary", m."MissingInformation"
FROM "JobMatches" m
JOIN "Jobs" j ON j."Id" = m."JobId"
ORDER BY m."AnalyzedAt" DESC
LIMIT 50;
```

## Vagas notificadas

```sql
SELECT n."SentAt", m."Score", j."Title", j."Company", j."Url"
FROM "Notifications" n
JOIN "JobMatches" m ON m."Id" = n."JobMatchId"
JOIN "Jobs" j ON j."Id" = n."JobId"
WHERE n."Status" = 'Sent'
ORDER BY n."SentAt" DESC;
```

## Histórico de análises de uma vaga

Há uma análise por versão do perfil.

```sql
SELECT m."ProfileVersion", m."AnalyzedAt", m."Score", m."Strengths", m."Gaps", m."TechnologyMatches"
FROM "JobMatches" m
WHERE m."JobId" = '<id da vaga>'
ORDER BY m."AnalyzedAt";
```

## Última verificação da fonte

O checkpoint IMAP tem o formato `UIDVALIDITY:último UID`.

```sql
SELECT s."Name", c."LastCheckedAt", c."Checkpoint", c."Enabled"
FROM "JobSearchConfigurations" c
JOIN "JobSources" s ON s."Id" = c."SourceId";
```

## Falhas

```sql
-- Análises em nova tentativa ou que desistiram (a vaga continua guardada)
SELECT "Title", "AnalysisStatus", "AnalysisAttempts", "NextAnalysisAttemptAt", "LastAnalysisError"
FROM "Jobs"
WHERE "AnalysisStatus" IN ('RetryScheduled', 'Failed')
ORDER BY "FoundAt" DESC;

-- Notificações que não chegaram ao Discord (a análise continua guardada)
SELECT n."Status", n."Attempts", n."NextAttemptAt", n."LastError", j."Title"
FROM "Notifications" n
JOIN "Jobs" j ON j."Id" = n."JobId"
WHERE n."Status" IN ('RetryScheduled', 'Failed');
```

## Reprocessar depois de corrigir a causa

Use depois de renovar o token do Claude, corrigir o canal do Discord etc.

```sql
-- Volta as análises que desistiram para a fila
UPDATE "Jobs"
SET "AnalysisStatus" = 'Pending', "AnalysisAttempts" = 0, "NextAnalysisAttemptAt" = NULL, "LastAnalysisError" = NULL
WHERE "AnalysisStatus" = 'Failed';

-- Volta as notificações que desistiram para a fila
UPDATE "Notifications"
SET "Status" = 'Pending', "Attempts" = 0, "NextAttemptAt" = NULL, "LastError" = NULL
WHERE "Status" = 'Failed';
```

Nenhuma das duas consultas cria duplicidade. Uma vaga que já tem análise para a versão atual do perfil não chama o Claude de novo. E uma notificação continua única por vaga e candidato.

## Agenda (Hangfire)

```sql
SELECT key, value FROM hangfire.hash WHERE key LIKE 'recurring-job:jobradar:%' AND field = 'LastExecution';
```
