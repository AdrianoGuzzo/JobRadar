# ADR-003: Execução isolada do Claude Code CLI

- **Status:** aceito (2026-11-08). Os flags exatos serão confirmados no spike da tarefa 4.1, que pode revisar este ADR.

## Contexto

A análise semântica usa o Claude Code CLI em modo não interativo (`claude -p`), não a API da Anthropic diretamente.

A descrição da vaga é conteúdo externo e não confiável, e não pode virar instrução. Também não se pode lançar um processo por vaga sem limite.

## Decisão

`ClaudeCodeJobAnalyzer : IJobAnalyzer`, no Infrastructure:

**Processo:**
- `ProcessStartInfo` com `UseShellExecute = false`.
- Argumentos só por `ArgumentList`, nunca por string montada e nunca por shell.

**Prompt:**
- Vai pelo **stdin**.
- As instruções fixas vão por `--append-system-prompt`.
- A descrição da vaga entra entre delimitadores, declarada como dado não confiável.

**Isolamento:**
- Nenhuma ferramenta (`--tools ""`) e nenhum MCP (`--strict-mcp-config`).
- `--max-turns 1`.
- Diretório de trabalho temporário e vazio.
- Ambiente limpo: só `PATH`, `HOME` e `CLAUDE_CODE_OAUTH_TOKEN`.
- Antes de executar, uma checagem recusa qualquer argumento que pule permissões.

**Controle do processo:**
- Timeout configurável, ligado ao `CancellationToken`.
- No cancelamento, `Kill(entireProcessTree: true)`.
- No container, `init: true` (tini) recolhe os órfãos.
- Concorrência limitada por um `SemaphoreSlim` singleton (`Claude__MaxConcurrency`, padrão 2).

**Saída:**
- `--output-format json`. stdout e stderr são lidos em paralelo, com limite de tamanho, e o código de saída é validado.
- A resposta é validada antes de persistir:
  - schema;
  - score de 0 a 100;
  - `recommendation` dentro do enum;
  - listas com tamanho limitado.
- Se a validação falhar, nada é gravado e a vaga continua pendente.

**Classificação de erros:**
- **Transitórios:** timeout, rate limit, "overloaded" e saída vazia.
- **Permanentes:** não autenticado e versão do CLI incompatível.

**Autenticação:** pelo `CLAUDE_CODE_OAUTH_TOKEN` (`claude setup-token`). O CLI roda dentro do container do Worker.

## Consequências

- Sem ferramentas, uma injeção de prompt bem-sucedida só consegue distorcer o score de uma vaga. Ela não lê arquivos nem executa comandos.
- A mitigação restante é a validação e o link original sempre presente na notificação.
- A dependência da versão do CLI é controlada: a imagem fixa a versão, há checagem no startup e testes de contrato contra o `JobRadar.FakeClaude`.
