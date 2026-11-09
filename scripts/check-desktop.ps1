$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
# 发布产物落点：CI 用 `-o artifacts/desktop`（.github/workflows/build.yml）；
# .tools/publish/desktop 是 PROJECT_STATUS.md 里声明的历史发布包。两者都接受，优先新落点。
$exe = $null
foreach ($candidate in @(
    (Join-Path $repo 'artifacts/desktop/AntMind.Desktop.exe'),
    (Join-Path $repo '.tools/publish/desktop/AntMind.Desktop.exe')
)) {
    if (Test-Path -LiteralPath $candidate) { $exe = $candidate; break }
}
if (-not $exe) {
    throw "AntMind.Desktop.exe not found. Publish it first: dotnet publish src/AntMind.Desktop -c Release -r win-x64 --self-contained true -o artifacts/desktop"
}
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
function Send-Observe([int] $version) {
    return Send-Frame ((@{ ProtocolVersion = $version; Method = 'observe' } | ConvertTo-Json -Depth 3 -Compress))
}

$desktopProcess = $null
try {
    $desktopProcess = Start-Process -FilePath $exe -WindowStyle Hidden -PassThru
    # 协议号不写死：先发一个不存在的版本，宿主在版本校验失败时仍会回它自己的 Protocol.Version
    # （见 src/AntMind.Transport/Pipes.cs）。桌面启动时管道监听器可能还没就绪，故重试。
    $protocolVersion = $null
    for ($attempt = 0; $attempt -lt 30 -and -not $protocolVersion; $attempt++) {
        $probe = $null
        try { $probe = Send-Frame '{"ProtocolVersion":0,"Method":"observe"}' } catch { Start-Sleep -Milliseconds 500; continue }
        $protocolVersion = $probe.ProtocolVersion
        if (-not $protocolVersion) { Start-Sleep -Milliseconds 500 }
    }
    if (-not $protocolVersion) { throw 'Desktop IPC probe returned no protocol version' }
    $first = Send-Observe $protocolVersion
    # Snapshot 为 null 说明请求没被 dispatch（协议/管道/存档问题），必须单独判：
    # 否则 $null -ne 0 会把协议错误伪装成 'Desktop must start paused at Tick 0'。
    if ($null -eq $first.Snapshot) { throw "Desktop observe failed: $($first.Error)" }
    if ($first.Snapshot.Tick -ne 0) { throw 'Desktop must start paused at Tick 0' }
    $command = @{ ProtocolVersion=$protocolVersion; Method='command'; Command=@{RequestId=[Guid]::NewGuid().ToString(); ExpectedRevision=$first.Snapshot.Revision; Kind=2; Value=0; Reason='desktop smoke check'} } | ConvertTo-Json -Depth 5 -Compress
    $result = Send-Frame $command
    if (!$result.Result.Accepted) { throw 'Desktop step rejected' }
    $second = Send-Observe $protocolVersion
    if ($null -eq $second.Snapshot) { throw "Desktop observe after step failed: $($second.Error)" }
    if ($second.Snapshot.Tick -ne 1) { throw 'Desktop host did not step' }
    $desktopProcess.Refresh()
    "PASS published desktop process + IPC step Tick 0 -> 1; window handle=$($desktopProcess.MainWindowHandle); title=$($desktopProcess.MainWindowTitle)"
} finally {
    if ($desktopProcess -and !$desktopProcess.HasExited) { Stop-Process -Id $desktopProcess.Id -Force }
    $env:ANTMIND_PIPE = $priorPipe
}
