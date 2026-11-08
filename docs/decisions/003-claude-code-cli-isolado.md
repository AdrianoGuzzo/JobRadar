# ADR-003: Execução isolada do Claude Code CLI

- **Status:** aceito e implementado (2026-11-08). Spike da tarefa 4.1 registrado abaixo.

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

## Resultado do spike (tarefa 4.1, Claude Code 2.1.319)

**Flags finais:** `-p --output-format json --tools "" --strict-mcp-config --safe-mode --restricted --no-session-persistence --disable-slash-commands --max-turns N --system-prompt <instruções> [--json-schema <schema>] [--model <modelo>]`.

- **`--system-prompt`** substitui o prompt padrão de agente de código. As instruções do JobRadar são as únicas.
- **`--bare` não serve.** Ele só aceita `ANTHROPIC_API_KEY` e nunca lê OAuth, então o `CLAUDE_CODE_OAUTH_TOKEN` não funcionaria. O isolamento vem de `--safe-mode`, que desliga CLAUDE.md, hooks, plugins, skills e MCP, e de `--restricted`, que ignora os settings de usuário e projeto.
- **`--max-turns`** é aceito, mas não aparece no `--help`.
- **Em erro de API,** o CLI sai com código 1 e **o envelope JSON continua no stdout**: `is_error`, `result` e `api_error_status`. O analisador lê esse envelope para classificar o erro, em vez de olhar só o código de saída.
- **Na máquina de desenvolvimento,** o `claude -p` respondeu `Credit balance is too low` (HTTP 400), inclusive sem nenhum flag. A conta usada no modo headless não tinha crédito. Esse erro é classificado como **permanente**.
- **O formato de sucesso do `--json-schema`** (`structured_output`) não pôde ser observado. O parser aceita `structured_output` ou o JSON no texto de `result`, e `Claude__UseJsonSchema=false` desliga o flag se houver incompatibilidade.

## Consequências

- Sem ferramentas, uma injeção de prompt bem-sucedida só consegue distorcer o score de uma vaga. Ela não lê arquivos nem executa comandos.
- A mitigação restante é a validação e o link original sempre presente na notificação.
- A dependência da versão do CLI é controlada: a imagem fixa a versão, há checagem no startup e testes de contrato contra o `JobRadar.FakeClaude`.
