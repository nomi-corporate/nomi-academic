param(
    [ValidateSet('Info', 'List', 'Add', 'Script', 'Update')][string]$Action = 'Info',
    [ValidatePattern('^[A-Za-z][A-Za-z0-9_]*$')][string]$Name
)
. "$PSScriptRoot/LocalDatabase.ps1"

$oldConnection = $env:NOMI_ACADEMIC_MIGRATION_CONNECTION
Push-Location $script:NomiRoot
try {
    $env:NOMI_ACADEMIC_MIGRATION_CONNECTION = Get-NomiConnection -Migrations
    & dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao restaurar a ferramenta EF Core.' }
    $options = @('--project', 'src/Nomi.Academic.Infrastructure', '--startup-project', 'src/Nomi.Academic.Api')
    switch ($Action) {
        'Info' { & dotnet ef dbcontext info @options }
        'List' {
            $directory = Join-Path $script:NomiRoot 'src/Nomi.Academic.Infrastructure/Persistence/Migrations'
            if (-not (Test-Path -LiteralPath $directory)) {
                Write-Output 'Ainda não há migrations de negócio. O bootstrap criou apenas o banco e os schemas locais.'
            }
            else { & dotnet ef migrations list @options }
        }
        'Add' {
            if (-not $Name) { throw 'Informe -Name para criar a migration.' }
            & dotnet ef migrations add $Name --output-dir Persistence/Migrations @options
        }
        { $_ -in 'Script', 'Update' } {
            $directory = Join-Path $script:NomiRoot 'src/Nomi.Academic.Infrastructure/Persistence/Migrations'
            if (-not (Test-Path -LiteralPath $directory) -or
                -not (Get-ChildItem -LiteralPath $directory -Filter '*.cs' -ErrorAction SilentlyContinue)) {
                throw 'Ainda não há migrations de negócio. Crie e revise o modelo da HU antes de gerar ou aplicar SQL.'
            }
            if ($Action -eq 'Script') {
                New-Item -ItemType Directory -Force -Path '.local' | Out-Null
                & dotnet ef migrations script --idempotent --output .local/migrations.sql @options
            }
            else { & dotnet ef database update @options }
        }
    }
    if ($LASTEXITCODE -ne 0) { throw 'A operação de migrations falhou.' }
}
finally {
    $env:NOMI_ACADEMIC_MIGRATION_CONNECTION = $oldConnection
    Pop-Location
}
