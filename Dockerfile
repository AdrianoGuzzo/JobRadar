# syntax=docker/dockerfile:1

# ---- build ----
# O global.json não entra: ele fixa o SDK da máquina de desenvolvimento, e a imagem traz o SDK 10 dela.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/ src/
RUN dotnet publish src/JobRadar.Worker/JobRadar.Worker.csproj -c Release -o /app/publish

# Codex CLI fixado: inclui os binários nativos para a arquitetura da imagem.
FROM node:22-bookworm-slim AS codex
ARG CODEX_VERSION=0.169.0
RUN npm install -g @openai/codex@${CODEX_VERSION}

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# Versão do Claude Code em que os flags de isolamento foram verificados (ADR-003).
ARG CLAUDE_CODE_VERSION=2.1.319
COPY --from=codex /usr/local/bin/node /usr/local/bin/node
COPY --from=codex /usr/local/lib/node_modules/@openai/ /usr/local/lib/node_modules/@openai/
RUN ln -s /usr/local/lib/node_modules/@openai/codex/bin/codex.js /usr/local/bin/codex \
    && mkdir -p /home/app/.codex && chown app:app /home/app/.codex

RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl \
    && rm -rf /var/lib/apt/lists/*

# Usuário sem privilégios que já vem na imagem do .NET.
USER app
ENV HOME=/home/app \
    PATH=/home/app/.local/bin:$PATH \
    CODEX_HOME=/home/app/.codex \
    DISABLE_AUTOUPDATER=1

RUN curl -fsSL https://claude.ai/install.sh | bash -s ${CLAUDE_CODE_VERSION} \
    && claude --version

WORKDIR /app
EXPOSE 8080
COPY --from=build --chown=app:app /app/publish .

ENV DOTNET_ENVIRONMENT=Production \
    Claude__ExecutablePath=/home/app/.local/bin/claude \
    Claude__RequireOAuthToken=true \
    Profile__Path=/app/config/profile.json

CMD ["dotnet", "JobRadar.Worker.dll"]
