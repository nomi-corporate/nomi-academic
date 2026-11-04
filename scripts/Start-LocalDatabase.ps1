# Requer PowerShell 7 e Docker Desktop/Engine funcionando.
. "$PSScriptRoot/LocalDatabase.ps1"

$envPath = Join-Path $script:NomiRoot '.env'
if (-not (Test-Path -LiteralPath $envPath)) {
    $values = @('NOMI_POSTGRES_PORT=55432')
    foreach ($key in @('NOMI_POSTGRES_ADMIN_PASSWORD', 'NOMI_ACADEMIC_MIGRATOR_PASSWORD', 'NOMI_ACADEMIC_APP_PASSWORD')) {
        $bytes = [byte[]]::new(32)
        [Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
        $values += "$key=$([Convert]::ToHexString($bytes).ToLowerInvariant())"
    }
    [IO.File]::WriteAllLines($envPath, $values, [Text.UTF8Encoding]::new($false))
    if (-not $IsWindows) { & chmod 600 $envPath }
    Write-Output 'Criado .env local com senhas aleatórias; arquivo ignorado pelo Git.'
}

Invoke-NomiCompose -ComposeArguments @('up', '--detach', '--wait', '--wait-timeout', '120', 'postgres')
Write-Output 'PostgreSQL local pronto. Inicie a API com scripts/Run-LocalAcademic.ps1.'
