<#
.SYNOPSIS
    Compila a solução e roda as suítes de teste do JobRadar.

.DESCRIPTION
    Sempre: build Release (avisos são erros) e testes unitários.
    Testes de integração: rodam quando o projeto existir e o Docker estiver
    disponível (Testcontainers). Com -UnitOnly, são pulados.
    Testes "External" (serviços reais) se pulam sozinhos sem as variáveis
    JOBRADAR_EXTERNAL_*; nunca são obrigatórios.

.EXAMPLE
    ./scripts/test.ps1
    ./scripts/test.ps1 -UnitOnly
#>
param(
    [switch]$UnitOnly
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

function Invoke-Step([string]$Title, [scriptblock]$Command) {
    Write-Host "==> $Title" -ForegroundColor Cyan
    & $Command
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FALHOU: $Title (código $LASTEXITCODE)" -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

Invoke-Step 'Build (Release)' { dotnet build "$root/JobRadar.slnx" -c Release }
Invoke-Step 'Testes unitários' { dotnet test --project "$root/tests/JobRadar.UnitTests" -c Release --no-build }

$integration = Join-Path $root 'tests/JobRadar.IntegrationTests'

if ($UnitOnly) {
    Write-Host 'Testes de integração pulados (-UnitOnly).' -ForegroundColor Yellow
}
elseif (-not (Test-Path $integration)) {
    Write-Host 'Ainda não há testes de integração.' -ForegroundColor Yellow
}
else {
    docker info *> $null
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'FALHOU: Docker indisponível para os testes de integração. Inicie o Docker Desktop ou use -UnitOnly.' -ForegroundColor Red
        exit 1
    }

    Invoke-Step 'Testes de integração' { dotnet test --project $integration -c Release --no-build }
}

Write-Host 'Tudo verde.' -ForegroundColor Green
