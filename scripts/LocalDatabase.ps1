Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:NomiRoot = Split-Path $PSScriptRoot -Parent

function Get-NomiDocker {
    $command = Get-Command docker -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    if ($IsWindows -and $env:LOCALAPPDATA) {
        $fallback = Join-Path $env:LOCALAPPDATA 'Programs/DockerDesktop/resources/bin/docker.exe'
        if (Test-Path -LiteralPath $fallback) { return $fallback }
    }
    throw 'Docker não encontrado. Instale e inicie o Docker Desktop.'
}

function Read-NomiDatabaseSettings {
    $envPath = Join-Path $script:NomiRoot '.env'
    if (-not (Test-Path -LiteralPath $envPath)) {
        throw 'Execute scripts/Start-LocalDatabase.ps1 primeiro.'
    }
    $settings = @{}
    foreach ($line in [IO.File]::ReadAllLines($envPath)) {
        if ($line -match '^([A-Z_]+)=(.*)$') { $settings[$Matches[1]] = $Matches[2] }
    }
    foreach ($key in @('NOMI_POSTGRES_ADMIN_PASSWORD', 'NOMI_ACADEMIC_MIGRATOR_PASSWORD', 'NOMI_ACADEMIC_APP_PASSWORD')) {
        if (-not $settings.ContainsKey($key) -or $settings[$key] -notmatch '^[a-f0-9]{64}$') {
            throw "A configuração $key precisa conter uma senha local de 64 caracteres hexadecimais. O exemplo CHANGE_ME não pode ser usado."
        }
    }
    if (-not $settings.ContainsKey('NOMI_POSTGRES_PORT') -or
        $settings['NOMI_POSTGRES_PORT'] -notmatch '^\d{1,5}$' -or
        [int]$settings['NOMI_POSTGRES_PORT'] -lt 1024 -or
        [int]$settings['NOMI_POSTGRES_PORT'] -gt 65535) {
        throw 'NOMI_POSTGRES_PORT deve ser uma porta local entre 1024 e 65535.'
    }
    return $settings
}

function Invoke-NomiCompose {
    param([Parameter(Mandatory)][string[]]$ComposeArguments)
    $settings = Read-NomiDatabaseSettings
    $previous = @{}
    try {
        foreach ($key in $settings.Keys) {
            $previous[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
            [Environment]::SetEnvironmentVariable($key, $settings[$key], 'Process')
        }
        & (Get-NomiDocker) compose --project-directory $script:NomiRoot --env-file (Join-Path $script:NomiRoot '.env') @ComposeArguments
        if ($LASTEXITCODE -ne 0) { throw 'A operação do Docker Compose falhou. Confira o Docker e a porta configurada.' }
    }
    finally {
        foreach ($key in $previous.Keys) {
            if ($null -eq $previous[$key]) {
                Remove-Item -LiteralPath "Env:$key" -ErrorAction SilentlyContinue
            }
            else { [Environment]::SetEnvironmentVariable($key, $previous[$key], 'Process') }
        }
    }
}

function Get-NomiConnection {
    param([switch]$Migrations)
    $settings = Read-NomiDatabaseSettings
    $role = if ($Migrations) { 'nomi_academic_migrator' } else { 'nomi_academic_app' }
    $passwordKey = if ($Migrations) { 'NOMI_ACADEMIC_MIGRATOR_PASSWORD' } else { 'NOMI_ACADEMIC_APP_PASSWORD' }
    return "Host=127.0.0.1;Port=$($settings['NOMI_POSTGRES_PORT']);Database=nomi_academic;Username=$role;Password=$($settings[$passwordKey]);Timeout=3;Command Timeout=5"
}
