param([switch]$Restart, [ValidateRange(1024, 65535)][int]$ApiPort = 15101)
. "$PSScriptRoot/LocalDatabase.ps1"

$settings = Read-NomiDatabaseSettings
$docker = Get-NomiDocker
$probe = 'probe_' + [Guid]::NewGuid().ToString('N')
$api = $null
$databaseStopped = $false

function Invoke-ProbeSql {
    param([string]$Sql, [switch]$Migrations, [switch]$ExpectDenied)
    $role = if ($Migrations) { 'nomi_academic_migrator' } else { 'nomi_academic_app' }
    $key = if ($Migrations) { 'NOMI_ACADEMIC_MIGRATOR_PASSWORD' } else { 'NOMI_ACADEMIC_APP_PASSWORD' }
    $result = & $docker compose --project-directory $script:NomiRoot --env-file (Join-Path $script:NomiRoot '.env') exec -T -e "PGPASSWORD=$($settings[$key])" postgres `
        psql -X --host 127.0.0.1 --username $role --dbname nomi_academic --set=ON_ERROR_STOP=1 --set=VERBOSITY=sqlstate --tuples-only --no-align --command $Sql 2>&1
    $code = $LASTEXITCODE
    if ($ExpectDenied) {
        if ($code -eq 0 -or ($result -join ' ') -notmatch '42501') {
            throw 'A operação de DDL deveria falhar especificamente por falta de permissão (42501).'
        }
        return
    }
    if ($code -ne 0) { throw "Falha na verificação SQL local: $($result -join ' ')" }
    return ($result -join "`n").Trim()
}

function Assert-ProbeValue {
    param([string]$Sql, [string]$Expected)
    if ((Invoke-ProbeSql -Sql $Sql) -ne $Expected) { throw 'Uma verificação de dados/permissões falhou.' }
}

function Assert-Health {
    param([string]$Endpoint, [int]$Expected)
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        if ($api.HasExited) { throw 'A API encerrou durante o teste de conexão.' }
        try {
            $response = Invoke-WebRequest "http://127.0.0.1:$ApiPort/health/$Endpoint" -SkipHttpErrorCheck -TimeoutSec 8
            if ([int]$response.StatusCode -eq $Expected) { return }
        }
        catch { }
        Start-Sleep -Milliseconds 500
    }
    throw "O endpoint $Endpoint não retornou HTTP $Expected."
}

try {
    Invoke-NomiCompose -ComposeArguments @('up', '--detach', '--wait', '--wait-timeout', '120', 'postgres')
    Assert-ProbeValue 'SELECT current_user' 'nomi_academic_app'
    Assert-ProbeValue "SELECT rolsuper OR rolcreatedb OR rolcreaterole FROM pg_roles WHERE rolname = current_user" 'f'
    Assert-ProbeValue "SELECT pg_has_role(current_user, 'nomi_academic_migrator', 'MEMBER')" 'f'
    Assert-ProbeValue "SELECT has_schema_privilege('academic', 'USAGE')" 't'
    Assert-ProbeValue "SELECT has_schema_privilege('academic', 'CREATE')" 'f'
    Assert-ProbeValue "SELECT has_schema_privilege('migrations', 'USAGE')" 'f'
    Assert-ProbeValue "SELECT has_database_privilege('postgres', 'CONNECT')" 'f'
    Invoke-ProbeSql "CREATE TABLE academic.$probe (id integer PRIMARY KEY, value text NOT NULL)" -Migrations | Out-Null
    Invoke-ProbeSql "CREATE TABLE academic.denied_$probe (id integer)" -ExpectDenied
    Invoke-ProbeSql "INSERT INTO academic.$probe VALUES (1, 'before_restart')" | Out-Null
    Invoke-ProbeSql "UPDATE academic.$probe SET value = 'persisted' WHERE id = 1" | Out-Null
    Assert-ProbeValue "SELECT value FROM academic.$probe WHERE id = 1" 'persisted'
    Write-Output 'Permissões: aplicação lê/grava dados, sem DDL, administração ou acesso ao schema de migrations.'

    $output = Join-Path $script:NomiRoot '.local/validation-api'
    & dotnet publish (Join-Path $script:NomiRoot 'src/Nomi.Academic.Api') -c Release -o $output --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar a API para validação.' }
    $start = [Diagnostics.ProcessStartInfo]::new((Get-Command dotnet).Source)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WorkingDirectory = $output
    $start.ArgumentList.Add((Join-Path $output 'Nomi.Academic.Api.dll'))
    $start.ArgumentList.Add('--urls')
    $start.ArgumentList.Add("http://127.0.0.1:$ApiPort")
    $start.Environment['ConnectionStrings__Academic'] = Get-NomiConnection
    $start.Environment['ASPNETCORE_ENVIRONMENT'] = 'Development'
    $start.Environment['Logging__LogLevel__Default'] = 'Critical'
    $api = [Diagnostics.Process]::new()
    $api.StartInfo = $start
    $null = $api.Start()
    Assert-Health 'live' 200
    Assert-Health 'ready' 200
    Write-Output 'API conectada pelo EF Core: liveness 200 e readiness 200.'

    if ($Restart) {
        $databaseStopped = $true
        Invoke-NomiCompose -ComposeArguments @('stop', 'postgres')
        Assert-Health 'live' 200
        Assert-Health 'ready' 503
        Write-Output 'Banco parado: API segue viva (200) e sinaliza indisponibilidade (503).'
        Invoke-NomiCompose -ComposeArguments @('up', '--detach', '--wait', '--wait-timeout', '120', 'postgres')
        $databaseStopped = $false
        Assert-Health 'ready' 200
        Assert-ProbeValue "SELECT value FROM academic.$probe WHERE id = 1" 'persisted'
        Write-Output 'Reconexão e persistência após reiniciar o contêiner: verificadas.'
    }

    Invoke-ProbeSql "DELETE FROM academic.$probe WHERE id = 1" | Out-Null
    Assert-ProbeValue "SELECT count(*) FROM academic.$probe" '0'
    Write-Output 'Validações do banco local concluídas; nenhuma tabela de negócio foi criada.'
}
finally {
    if ($api) {
        if (-not $api.HasExited) { $api.Kill($true); $api.WaitForExit() }
        $api.Dispose()
    }
    if ($databaseStopped) {
        Invoke-NomiCompose -ComposeArguments @('up', '--detach', '--wait', '--wait-timeout', '120', 'postgres')
    }
    Invoke-ProbeSql "DROP TABLE IF EXISTS academic.$probe" -Migrations | Out-Null
}
