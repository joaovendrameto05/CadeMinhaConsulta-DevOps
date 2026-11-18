# Use a patient ID only with synthetic test data.
param([long] $TestPatientOracleId = 0, [switch] $SavePublicSettings, [long] $SecondTestPatientOracleId = 0)

if ($SecondTestPatientOracleId -gt 0 -and
    ($TestPatientOracleId -le 0 -or $SecondTestPatientOracleId -eq $TestPatientOracleId)) {
    throw 'Informe dois IDs fictícios diferentes para testar a titularidade.'
}

# Run from PowerShell 7. Only the public key is passed to the API process.
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:DOTNET_ENVIRONMENT = 'Development'
$env:Jwt__Issuer = 'urn:vagacerta:development'
$env:Jwt__Audience = 'vagacerta-dotnet-dev'
$env:Jwt__RoleClaim = 'role'

function ConvertTo-Base64Url([byte[]] $Bytes) {
    [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function New-RoleToken([string] $Role, [string] $Subject, [System.Security.Cryptography.RSA] $SigningKey, [long] $PatientId = 0) {
    $header = '{"alg":"RS256","typ":"JWT"}'
    $now = [DateTimeOffset]::UtcNow
    $claims = @{
        iss = $env:Jwt__Issuer
        aud = $env:Jwt__Audience
        sub = $Subject
        role = $Role
        iat = $now.ToUnixTimeSeconds()
        exp = $now.AddMinutes(30).ToUnixTimeSeconds()
    }
    if ($Role -eq 'CIDADAO' -and $PatientId -gt 0) {
        $claims['vagacerta_test_patient_id'] = $PatientId
    }
    $payload = $claims | ConvertTo-Json -Compress
    $encodedHeader = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes($header))
    $encodedPayload = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes($payload))
    $unsignedToken = "$encodedHeader.$encodedPayload"
    $signature = $SigningKey.SignData([Text.Encoding]::ASCII.GetBytes($unsignedToken),
        [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
    "$unsignedToken.$(ConvertTo-Base64Url $signature)"
}

$rsa = [System.Security.Cryptography.RSA]::Create(2048)
try {
    $env:Jwt__PublicKeyPem = $rsa.ExportSubjectPublicKeyInfoPem()
    if ($TestPatientOracleId -gt 0) {
        $env:Jwt__OracleUserIdClaim = 'vagacerta_test_patient_id'
    }
    if ($SavePublicSettings) {
        $apiProject = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\CadeMinhaConsulta.Api'
        $settings = @{
            'Jwt:Issuer' = $env:Jwt__Issuer
            'Jwt:Audience' = $env:Jwt__Audience
            'Jwt:RoleClaim' = $env:Jwt__RoleClaim
            'Jwt:PublicKeyPem' = $env:Jwt__PublicKeyPem
        }
        if ($TestPatientOracleId -gt 0) {
            $settings['Jwt:OracleUserIdClaim'] = $env:Jwt__OracleUserIdClaim
        }
        $settings | ConvertTo-Json -Compress | dotnet user-secrets set --project $apiProject | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Falha ao configurar a validação JWT de desenvolvimento em User Secrets.' }
    }
    $tokens = [ordered]@{
        Gestor = New-RoleToken 'GESTOR' 'demo-gestor' $rsa
        Cidadao = New-RoleToken 'CIDADAO' 'demo-cidadao' $rsa $TestPatientOracleId
    }
    if ($SecondTestPatientOracleId -gt 0) {
        $tokens['OutroCidadao'] = New-RoleToken 'CIDADAO' 'demo-cidadao-2' $rsa $SecondTestPatientOracleId
    }
    [pscustomobject]$tokens
} finally {
    $rsa.Dispose()
}
