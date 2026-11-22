param([ValidateRange(1024, 65535)][int]$Port = 5101)
. "$PSScriptRoot/LocalDatabase.ps1"

$oldConnection = $env:ConnectionStrings__Academic
$oldEnvironment = $env:ASPNETCORE_ENVIRONMENT
$oldIdentityBaseUrl = $env:Identity__BaseUrl
try {
    $env:ConnectionStrings__Academic = Get-NomiConnection
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    if ([string]::IsNullOrWhiteSpace($env:Identity__BaseUrl)) { $env:Identity__BaseUrl = 'http://localhost:5104' }
    & dotnet run --project (Join-Path $script:NomiRoot 'src/Nomi.Academic.Api') --no-launch-profile --urls "http://127.0.0.1:$Port"
    if ($LASTEXITCODE -ne 0) { throw 'A execução do Academic falhou.' }
}
finally {
    $env:ConnectionStrings__Academic = $oldConnection
    $env:ASPNETCORE_ENVIRONMENT = $oldEnvironment
    $env:Identity__BaseUrl = $oldIdentityBaseUrl
}
