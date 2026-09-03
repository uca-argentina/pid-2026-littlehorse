# Hook PostToolUse de drink.it.
# Formatea automaticamente el archivo que Claude acaba de editar, para que el diff del PR
# no tenga ruido de formato y nadie discuta llaves en la revision.
#
# Recibe por stdin el JSON del hook de Claude Code. No falla nunca: si la herramienta no
# esta instalada todavia, sale en silencio con codigo 0.

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
        # --include acota el formateo a este archivo: correr dotnet format sobre toda la
        # solucion en cada edicion es inusablemente lento.
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
