# syntax=docker/dockerfile:1

# ---- build ----
# O global.json não entra: ele fixa o SDK da máquina de desenvolvimento, e a imagem traz o SDK 10 dela.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/ src/
RUN dotnet publish src/JobRadar.Worker/JobRadar.Worker.csproj -c Release -o /app/publish

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime

# Versão do Claude Code em que os flags de isolamento foram verificados (ADR-003).
ARG CLAUDE_CODE_VERSION=2.1.319

RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl \
    && rm -rf /var/lib/apt/lists/*

# Usuário sem privilégios que já vem na imagem do .NET.
USER app
ENV HOME=/home/app \
    PATH=/home/app/.local/bin:$PATH \
    DISABLE_AUTOUPDATER=1

RUN curl -fsSL https://claude.ai/install.sh | bash -s ${CLAUDE_CODE_VERSION} \
    && claude --version

WORKDIR /app
COPY --from=build --chown=app:app /app/publish .

ENV DOTNET_ENVIRONMENT=Production \
    Claude__ExecutablePath=/home/app/.local/bin/claude \
    Claude__RequireOAuthToken=true \
    Profile__Path=/app/config/profile.json

ENTRYPOINT ["dotnet", "JobRadar.Worker.dll"]
