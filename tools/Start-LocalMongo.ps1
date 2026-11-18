param(
    [string] $MongoExecutable,
    [ValidateRange(1, 65535)]
    [int] $Port = 27017,
    [switch] $Foreground
)

$ErrorActionPreference = 'Stop'
$localRoot = Join-Path (Split-Path -Parent $PSScriptRoot) '.local\mongodb'
if (-not $MongoExecutable) {
    $MongoExecutable = Join-Path $localRoot 'mongodb-win32-x86_64-windows-8.0.38\bin\mongod.exe'
}
$MongoExecutable = (Resolve-Path -LiteralPath $MongoExecutable).Path
$data = Join-Path $localRoot 'data'
$log = Join-Path $localRoot 'mongod.log'

function Test-LoopbackListener {
    $socket = [Net.Sockets.TcpClient]::new()
    try {
        $socket.Connect('127.0.0.1', $Port)
        return $true
    } catch [Net.Sockets.SocketException] {
        return $false
    } finally {
        $socket.Dispose()
    }
}

if (Test-LoopbackListener) {
    throw "A porta $Port já está em uso no loopback. Confira a instância em execução."
}

New-Item -ItemType Directory -Path $data -Force | Out-Null

if ($Foreground) {
    & $MongoExecutable --bind_ip 127.0.0.1 --port $Port --dbpath $data --logpath $log --logappend
    exit $LASTEXITCODE
}

$mongo = Start-Process -FilePath $MongoExecutable -ArgumentList @(
    '--bind_ip', '127.0.0.1', '--port', $Port, '--dbpath', ('"{0}"' -f $data),
    '--logpath', ('"{0}"' -f $log), '--logappend'
) -WindowStyle Hidden -PassThru

$timer = [Diagnostics.Stopwatch]::StartNew()
while ($timer.Elapsed.TotalSeconds -lt 15) {
    if ($mongo.HasExited) { throw "MongoDB encerrou ao iniciar. Consulte $log." }
    if (Test-LoopbackListener) { return $mongo }
    Start-Sleep -Milliseconds 200
}

throw "MongoDB ainda não abriu a porta $Port. Consulte $log antes de tentar novamente."
