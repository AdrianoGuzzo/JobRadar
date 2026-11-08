# ADR-001: Deduplicação de vagas

- **Status:** aceito (2026-11-08). Implementação nas Fases 2 e 3.

## Contexto

A mesma vaga pode chegar várias vezes, de duas formas:
- em vários e-mails de alerta, às vezes com parâmetros de rastreamento diferentes na URL;
- por fontes diferentes, no futuro.

Nem toda fonte fornece um identificador estável.

## Decisão

Duas camadas, as duas garantidas por índice único no PostgreSQL:

1. **Identificador externo:** unique em `(SourceId, ExternalId)`. No LinkedIn, é o número em `/jobs/view/{id}`.
2. **Fingerprint:** hash de título, empresa e localização normalizados (minúsculas, sem acentos, espaços colapsados). Ele também tem unique e cobre vagas sem id e repetições entre fontes.

A URL é normalizada antes de ser guardada: os parâmetros de rastreamento (`utm_*`, `trk`, `refId`...) são removidos.

A inserção usa `ON CONFLICT DO NOTHING`. Uma vaga repetida é simplesmente ignorada, sem erro.

## Consequências

- Duas vagas diferentes com o mesmo título, empresa e localização colapsam em uma só. É aceitável para o uso pessoal.
- Uma mudança de título na fonte gera uma vaga nova.
