# drink.it PostToolUse hook.
# Formats whatever file Claude just edited, so PR diffs carry no formatting
# noise and nobody argues about braces during review.
#
# Reads the Claude Code hook payload as JSON on stdin. Never fails: if the
# toolchain is not installed yet, it exits quietly with code 0.

$ErrorActionPreference = 'SilentlyContinue'

try {
    $payload = [Console]::In.ReadToEnd() | ConvertFrom-Json
    $file = $payload.tool_input.file_path
} catch {
    exit 0
}

if ([string]::IsNullOrWhiteSpace($file) -or -not (Test-Path $file)) { exit 0 }

$root = $env:CLAUDE_PROJECT_DIR
if ([string]::IsNullOrWhiteSpace($root)) { $root = (Get-Location).Path }

$ext = [System.IO.Path]::GetExtension($file).ToLowerInvariant()

switch ($ext) {

    '.cs' {
        if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { exit 0 }
        $sln = Join-Path $root 'backend/DrinkIt.sln'
        if (-not (Test-Path $sln)) { exit 0 }
        # --include narrows formatting to this one file: running dotnet format
        # over the whole solution on every edit is unusably slow.
        dotnet format $sln --include $file --no-restore 2>&1 | Out-Null
    }

    { $_ -in '.ts', '.html', '.scss', '.css', '.json', '.md' } {
        $frontend = Join-Path $root 'frontend'
        if (-not (Test-Path (Join-Path $frontend 'node_modules'))) { exit 0 }
        Push-Location $frontend
        npx --no-install prettier --write --ignore-unknown $file 2>&1 | Out-Null
        if ($ext -eq '.ts') {
            npx --no-install eslint --fix $file 2>&1 | Out-Null
        }
        Pop-Location
    }
}

exit 0
