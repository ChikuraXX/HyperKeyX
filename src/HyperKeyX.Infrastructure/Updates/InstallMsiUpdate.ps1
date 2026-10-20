$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# 自行计算 SHA-256，避免安装辅助进程依赖 Windows PowerShell 的可选模块自动加载。
function Get-FileHash {
    param(
        [Parameter(Mandatory)] [string] $LiteralPath,
        [Parameter(Mandatory)] [ValidateSet('SHA256')] [string] $Algorithm
    )

    $stream = [System.IO.File]::Open(
        $LiteralPath,
        [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read,
        [System.IO.FileShare]::Read)
    $hasher = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = $hasher.ComputeHash($stream)
        return [pscustomobject]@{
            Hash = ([System.BitConverter]::ToString($bytes)).Replace('-', '')
        }
    }
    finally {
        $hasher.Dispose()
        $stream.Dispose()
    }
}

# 校验握手文件只能位于安装包目录，防止辅助进程被用于写入任意位置。
function Assert-HandoffPayload {
    param([Parameter(Mandatory)] $Payload)

    if (-not [System.IO.Path]::IsPathRooted([string] $Payload.InstallerPath) -or
        [System.IO.Path]::GetExtension([string] $Payload.InstallerPath) -ine '.msi' -or
        [string] $Payload.Sha256 -notmatch '^[0-9a-fA-F]{64}$' -or
        [long] $Payload.Bytes -le 0 -or
        [int] $Payload.ParentProcessId -le 0 -or
        [long] $Payload.ParentStartTimeUtcTicks -le 0 -or
        [int] $Payload.ParentExitTimeoutSeconds -lt 1 -or
        [int] $Payload.ParentExitTimeoutSeconds -gt 120 -or
        [string] $Payload.ReadyNonce -notmatch '^[0-9A-F]{64}$') {
        throw '安装交接参数无效。'
    }

    $installerDirectory = [System.IO.Path]::GetDirectoryName(
        [System.IO.Path]::GetFullPath([string] $Payload.InstallerPath))
    foreach ($handoffPath in @([string] $Payload.ReadyPath, [string] $Payload.ResultPath)) {
        if (-not [System.IO.Path]::IsPathRooted($handoffPath) -or
            [System.IO.Path]::GetDirectoryName([System.IO.Path]::GetFullPath($handoffPath)) -ine $installerDirectory) {
            throw '握手文件必须与安装包位于同一目录。'
        }
    }
}

# 首次校验包含大小、SHA-256、可读性和 MSI 数据库格式，完成后才允许通知父应用退出。
function Assert-InitialPackage {
    param([Parameter(Mandatory)] $Payload)

    $file = Get-Item -LiteralPath ([string] $Payload.InstallerPath) -ErrorAction Stop
    if (-not $file.PSIsContainer -and $file.Length -ne [long] $Payload.Bytes) {
        throw '安装包大小与发布元数据不一致。'
    }
    if ($file.PSIsContainer) {
        throw '安装包路径不是文件。'
    }

    $actualHash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    if ($actualHash -ine [string] $Payload.Sha256) {
        throw '安装包 SHA-256 校验失败。'
    }

    $stream = [System.IO.File]::Open(
        $file.FullName,
        [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read,
        [System.IO.FileShare]::Read)
    $stream.Dispose()

    $windowsInstaller = $null
    $database = $null
    try {
        $windowsInstaller = New-Object -ComObject WindowsInstaller.Installer
        $database = $windowsInstaller.GetType().InvokeMember(
            'OpenDatabase',
            'InvokeMethod',
            $null,
            $windowsInstaller,
            @($file.FullName, 0))
        if ($null -eq $database) {
            throw '无法读取 MSI 数据库。'
        }
    }
    finally {
        if ($null -ne $database -and [System.Runtime.InteropServices.Marshal]::IsComObject($database)) {
            [void] [System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($database)
        }
        if ($null -ne $windowsInstaller -and [System.Runtime.InteropServices.Marshal]::IsComObject($windowsInstaller)) {
            [void] [System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($windowsInstaller)
        }
    }
}

# 父应用退出后再次校验大小和哈希，覆盖握手后安装包被替换的时间窗口。
function Assert-FinalPackage {
    param([Parameter(Mandatory)] $Payload)

    $file = Get-Item -LiteralPath ([string] $Payload.InstallerPath) -ErrorAction Stop
    if ($file.PSIsContainer -or $file.Length -ne [long] $Payload.Bytes) {
        throw '父应用退出后安装包大小发生变化。'
    }
    $actualHash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    if ($actualHash -ine [string] $Payload.Sha256) {
        throw '父应用退出后安装包 SHA-256 校验失败。'
    }
}

# 使用 CreateNew 写入随机数；已存在的文件不会被覆盖或误判为本次握手。
function Write-ReadySignal {
    param([Parameter(Mandatory)] $Payload)

    $bytes = [System.Text.Encoding]::UTF8.GetBytes([string] $Payload.ReadyNonce)
    $stream = [System.IO.File]::Open(
        [string] $Payload.ReadyPath,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::Read)
    try {
        $stream.Write($bytes, 0, $bytes.Length)
        $stream.Flush($true)
    }
    finally {
        $stream.Dispose()
    }
}

# 同时核对 PID 与启动时间；PID 被复用时视为原父进程已经退出，绝不终止任何进程。
function Wait-ForParentExit {
    param([Parameter(Mandatory)] $Payload)

    $deadline = [System.Diagnostics.Stopwatch]::StartNew()
    $timeout = [TimeSpan]::FromSeconds([int] $Payload.ParentExitTimeoutSeconds)
    while ($deadline.Elapsed -lt $timeout) {
        try {
            $parent = [System.Diagnostics.Process]::GetProcessById([int] $Payload.ParentProcessId)
            try {
                if ($parent.StartTime.ToUniversalTime().Ticks -ne [long] $Payload.ParentStartTimeUtcTicks) {
                    return $true
                }
                if ($parent.HasExited) {
                    return $true
                }
            }
            finally {
                $parent.Dispose()
            }
        }
        catch [System.ArgumentException] {
            return $true
        }
        catch [System.InvalidOperationException] {
            return $true
        }

        Start-Sleep -Milliseconds 200
    }

    return $false
}

# Windows PowerShell 5.1 缺少 ArgumentList，回退分支仍通过 CreateProcess 直接传参而不经过 shell。
function Invoke-MsiInstall {
    param([Parameter(Mandatory)] $Payload)

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = Join-Path $env:SystemRoot 'System32\msiexec.exe'
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $false
    $argumentListProperty = $startInfo.PSObject.Properties['ArgumentList']
    if ($null -ne $argumentListProperty) {
        $startInfo.ArgumentList.Add('/i')
        $startInfo.ArgumentList.Add([string] $Payload.InstallerPath)
        $startInfo.ArgumentList.Add('/norestart')
    }
    else {
        $startInfo.Arguments = '/i "' + [string] $Payload.InstallerPath + '" /norestart'
    }

    $process = [System.Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) {
        throw 'msiexec 未启动。'
    }
    try {
        $process.WaitForExit()
        return $process.ExitCode
    }
    finally {
        $process.Dispose()
    }
}

# 只允许重新启动名称明确的 HyperKeyX.exe，不接受其他可执行文件名。
function Start-OriginalApplication {
    param([Parameter(Mandatory)] $Payload)

    $applicationPath = [string] $Payload.ApplicationPath
    if ([System.IO.Path]::IsPathRooted($applicationPath) -and
        [System.IO.Path]::GetFileName($applicationPath) -ieq 'HyperKeyX.exe' -and
        [System.IO.File]::Exists($applicationPath)) {
        Start-Process -FilePath $applicationPath
    }
}

# 结果文件只供诊断；写入失败不得改变安装或恢复流程。
function Write-InstallResult {
    param(
        [Parameter(Mandatory)] $Payload,
        [Parameter(Mandatory)] [string] $Status
    )

    try {
        [System.IO.File]::WriteAllText(
            [string] $Payload.ResultPath,
            $Status,
            [System.Text.UTF8Encoding]::new($false))
    }
    catch {
    }
}

$parentExited = $false
try {
    Assert-HandoffPayload -Payload $payload
    Assert-InitialPackage -Payload $payload
    Write-ReadySignal -Payload $payload

    $parentExited = Wait-ForParentExit -Payload $payload
    if (-not $parentExited) {
        Write-InstallResult -Payload $payload -Status 'ParentExitTimeout'
        return
    }

    Remove-Item -LiteralPath ([string] $payload.ReadyPath) -Force -ErrorAction SilentlyContinue
    Assert-FinalPackage -Payload $payload
    $exitCode = Invoke-MsiInstall -Payload $payload
    if ($exitCode -notin @(0, 3010)) {
        Write-InstallResult -Payload $payload -Status "MsiFailed:$exitCode"
        Start-OriginalApplication -Payload $payload
        return
    }

    # 成功后的启动完全交给 MSI 完成页选项，尊重用户取消勾选的选择。
    Write-InstallResult -Payload $payload -Status "Succeeded:$exitCode"
}
catch {
    Write-InstallResult -Payload $payload -Status ('Failed:' + $_.Exception.Message)
    if ($parentExited) {
        Start-OriginalApplication -Payload $payload
    }
}
finally {
    if ($parentExited) {
        Remove-Item -LiteralPath ([string] $payload.ReadyPath) -Force -ErrorAction SilentlyContinue
    }
}
