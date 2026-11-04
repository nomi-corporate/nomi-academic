param([ValidateRange(1024, 65535)][int]$Port = 5101)
. "$PSScriptRoot/LocalDatabase.ps1"

$oldConnection = $env:ConnectionStrings__Academic
$oldEnvironment = $env:ASPNETCORE_ENVIRONMENT
try {
    $env:ConnectionStrings__Academic = Get-NomiConnection
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    & dotnet run --project (Join-Path $script:NomiRoot 'src/Nomi.Academic.Api') --no-launch-profile --urls "http://127.0.0.1:$Port"
    if ($LASTEXITCODE -ne 0) { throw 'A execução do Academic falhou.' }
}
finally {
    $env:ConnectionStrings__Academic = $oldConnection
    $env:ASPNETCORE_ENVIRONMENT = $oldEnvironment
}
