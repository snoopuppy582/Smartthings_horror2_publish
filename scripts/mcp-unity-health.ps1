param(
    [int]$Port = 8090
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$settingsPath = Join-Path $projectRoot "ProjectSettings\McpUnitySettings.json"
$packageIndex = Join-Path $projectRoot "Library\PackageCache\com.gamelovers.mcp-unity@aade29c7dd84\Server~\build\index.js"
$projectCodexConfig = Join-Path $projectRoot ".codex\config.toml"
$projectMcpJson = Join-Path $projectRoot ".mcp.json"
$workspaceRoot = Split-Path -Parent (Split-Path -Parent $projectRoot)
$workspaceMcpJson = Join-Path $workspaceRoot ".mcp.json"
$globalCodexConfig = Join-Path $env:USERPROFILE ".codex\config.toml"

$failures = New-Object System.Collections.Generic.List[string]

function Write-Check {
    param(
        [string]$Name,
        [bool]$Ok,
        [string]$Detail
    )

    $status = if ($Ok) { "PASS" } else { "FAIL" }
    Write-Host ("[{0}] {1}: {2}" -f $status, $Name, $Detail)

    if (-not $Ok) {
        $script:failures.Add($Name)
    }
}

Write-Host "MCP Unity health check"
Write-Host ("Project root: {0}" -f $projectRoot)

$nodeVersion = (& node --version) 2>$null
Write-Check "Node.js" ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($nodeVersion)) $nodeVersion

$npmVersion = (& npm --version) 2>$null
Write-Check "npm" ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($npmVersion)) $npmVersion

Write-Check "Unity MCP settings" (Test-Path $settingsPath) $settingsPath
if (Test-Path $settingsPath) {
    $settings = Get-Content -Raw $settingsPath | ConvertFrom-Json
    Write-Host ("  Port={0}, Timeout={1}s, AutoStart={2}, Remote={3}" -f $settings.Port, $settings.RequestTimeoutSeconds, $settings.AutoStartServer, $settings.AllowRemoteConnections)
}

Write-Check "MCP package index" (Test-Path $packageIndex) $packageIndex

foreach ($configPath in @($projectCodexConfig, $projectMcpJson, $workspaceMcpJson, $globalCodexConfig)) {
    $exists = Test-Path $configPath
    Write-Check "Config exists" $exists $configPath
    if ($exists) {
        $content = Get-Content -Raw $configPath
        $hasCwd = $content.Contains("cwd") -and $content.Contains("Smartthing_server/Smartthings_horror2")
        Write-Check "Config cwd/project path" $hasCwd $configPath
    }
}

$portOpen = $false
try {
    $connection = Test-NetConnection -ComputerName 127.0.0.1 -Port $Port -WarningAction SilentlyContinue
    $portOpen = [bool]$connection.TcpTestSucceeded
} catch {
    $portOpen = $false
}
Write-Check "Unity WebSocket port" $portOpen ("127.0.0.1:{0}" -f $Port)

if ($failures.Count -gt 0) {
    Write-Host ""
    Write-Host "Recovery:"
    Write-Host "1. Keep Unity out of Play Mode."
    Write-Host "2. In Unity, open Tools > MCP Unity > Server Window."
    Write-Host "3. Stop Server, then Start Server."
    Write-Host "4. Restart Codex so the updated cwd/timeout config is used."
    exit 1
}

Write-Host ""
Write-Host "MCP config and port checks passed. If Codex still reports Transport closed, restart Codex after Unity is back in Edit Mode."
