$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repo '.tools/publish/desktop/AntMind.Desktop.exe'
$priorPipe = $env:ANTMIND_PIPE
$env:ANTMIND_PIPE = 'antmind-desktop-check-' + [Guid]::NewGuid().ToString('N')
function Send-Frame([string] $json) {
    $connection = [System.IO.Pipes.NamedPipeClientStream]::new('.', $env:ANTMIND_PIPE, [System.IO.Pipes.PipeDirection]::InOut)
    try {
        $connection.Connect(15000)
        $connection.ReadMode = [System.IO.Pipes.PipeTransmissionMode]::Byte
        $bytes = [Text.Encoding]::UTF8.GetBytes($json)
        $writer = [System.IO.BinaryWriter]::new($connection, [Text.Encoding]::UTF8, $true)
        $writer.Write([int]$bytes.Length)
        $writer.Write($bytes)
        $writer.Flush()
        $reader = [System.IO.BinaryReader]::new($connection, [Text.Encoding]::UTF8, $true)
        $length = $reader.ReadInt32()
        if ($length -le 0 -or $length -gt 1048576) { throw 'Invalid response length' }
        return ([Text.Encoding]::UTF8.GetString($reader.ReadBytes($length)) | ConvertFrom-Json)
    } finally { $connection.Dispose() }
}
$desktopProcess = $null
try {
    $desktopProcess = Start-Process -FilePath $exe -WindowStyle Hidden -PassThru
    $first = Send-Frame '{"ProtocolVersion":1,"Method":"observe"}'
    if ($first.Snapshot.Tick -ne 0) { throw 'Desktop must start paused at Tick 0' }
    $command = @{ ProtocolVersion=1; Method='command'; Command=@{RequestId=[Guid]::NewGuid().ToString(); ExpectedRevision=$first.Snapshot.Revision; Kind=2; Value=0; Reason='desktop smoke check'} } | ConvertTo-Json -Depth 5 -Compress
    $result = Send-Frame $command
    if (!$result.Result.Accepted) { throw 'Desktop step rejected' }
    $second = Send-Frame '{"ProtocolVersion":1,"Method":"observe"}'
    if ($second.Snapshot.Tick -ne 1) { throw 'Desktop host did not step' }
    $desktopProcess.Refresh()
    "PASS published desktop process + IPC step Tick 0 -> 1; window handle=$($desktopProcess.MainWindowHandle); title=$($desktopProcess.MainWindowTitle)"
} finally {
    if ($desktopProcess -and !$desktopProcess.HasExited) { Stop-Process -Id $desktopProcess.Id -Force }
    $env:ANTMIND_PIPE = $priorPipe
}
