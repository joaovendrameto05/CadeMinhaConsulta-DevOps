# Run interactively in PowerShell 7. The password is read locally and sent to dotnet via stdin.
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9.-]*$')]
    [string] $Server,
    [ValidateRange(1, 65535)]
    [int] $Port = 1521,
    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]*$')]
    [string] $Identifier,
    [ValidateSet('ServiceName', 'Sid')]
    [string] $Naming = 'ServiceName',
    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Za-z][A-Za-z0-9_]*$')]
    [string] $OracleUser,
    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Za-z][A-Za-z0-9_]*$')]
    [string] $Schema
)

$ErrorActionPreference = 'Stop'
$project = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\CadeMinhaConsulta.Api'
$nameField = if ($Naming -eq 'Sid') { 'SID' } else { 'SERVICE_NAME' }
$descriptor = "(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=$Server)(PORT=$Port))(CONNECT_DATA=($nameField=$Identifier)))"
$connection = [System.Data.Common.DbConnectionStringBuilder]::new()
$password = Read-Host 'Senha Oracle' -AsSecureString
try {
    if ($password.Length -eq 0) { throw 'A senha está vazia.' }

    $connection['Data Source'] = $descriptor
    $connection['User Id'] = $OracleUser
    $connection['Password'] = [Net.NetworkCredential]::new('', $password).Password

    @{
        'ConnectionStrings:Oracle' = $connection.get_ConnectionString()
        'Oracle:Schema' = $Schema.ToUpperInvariant()
    } | ConvertTo-Json -Compress | dotnet user-secrets set --project $project | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Não foi possível configurar o Oracle em User Secrets.' }
} finally {
    $connection.Clear()
    $password.Dispose()
}
