<#
.SYNOPSIS
  Instala y abre el ícono de Mediateca en la bandeja de Windows.

.DESCRIPTION
  El ícono es una app pequeña (backend\src\Mediateca.Tray) que controla los contenedores con
  Docker Compose: iniciar, detener, abrir la web y ver los registros.
  1. Revisa que el SDK de .NET 11 esté instalado.
  2. Cierra el ícono si ya estaba abierto, para poder reemplazar sus archivos.
  3. Lo publica en %LOCALAPPDATA%\Mediateca\Bandeja.
  4. Lo abre apuntando a esta carpeta.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\bandeja.ps1
#>

$ErrorActionPreference = 'Stop'

$proyecto = Join-Path $PSScriptRoot 'backend\src\Mediateca.Tray\Mediateca.Tray.csproj'
$destino = Join-Path $env:LOCALAPPDATA 'Mediateca\Bandeja'

function Paso([string] $texto) {
    Write-Host "`n==> $texto" -ForegroundColor Cyan
}

function Detener([string] $texto) {
    Write-Host "`n$texto" -ForegroundColor Red
    exit 1
}

# 1. SDK de .NET 11 -------------------------------------------------------------------------
Paso 'Revisando el SDK de .NET 11'
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    Detener 'No encontré dotnet. El ícono de la bandeja se compila con el SDK de .NET 11: instálalo desde https://dotnet.microsoft.com/download y vuelve a ejecutar bandeja.ps1.'
}

$hayNet11 = $false
foreach ($sdk in @(& dotnet --list-sdks)) {
    if ("$sdk" -match '^11\.') { $hayNet11 = $true }
}
if (-not $hayNet11) {
    Detener 'Tienes dotnet, pero no el SDK de .NET 11, que el ícono de la bandeja necesita para compilarse. Instálalo desde https://dotnet.microsoft.com/download y vuelve a ejecutar bandeja.ps1.'
}

# 2. Cerrar el ícono si ya estaba abierto ---------------------------------------------------
Paso 'Cerrando el ícono anterior (si estaba abierto)'
Get-Process -Name Mediateca.Tray -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

# 3. Publicar -------------------------------------------------------------------------------
Paso "Compilando e instalando en $destino"
& dotnet publish $proyecto -c Release -o $destino
if ($LASTEXITCODE -ne 0) {
    Detener 'No se pudo compilar el ícono de la bandeja. Arriba está el error de dotnet.'
}

# 4. Abrirlo, separado de esta ventana ------------------------------------------------------
Paso 'Abriendo el ícono'
Start-Process (Join-Path $destino 'Mediateca.Tray.exe') -ArgumentList '--carpeta', "`"$PSScriptRoot`""

# 5. Resumen --------------------------------------------------------------------------------
Write-Host "`nListo: el ícono de Mediateca ya está en la bandeja de Windows (junto al reloj; si no lo ves, está en la flecha ^)." -ForegroundColor Green
Write-Host "  Instalado en: $destino"
Write-Host '  Para que arranque con Windows, marca "Iniciar con Windows" en el menú del ícono (clic derecho).'
Write-Host '  Doble clic en el ícono abre Mediateca cuando está en ejecución.'
