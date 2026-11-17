<#
.SYNOPSIS
  Levanta Mediateca con Docker: la primera vez y cada vez que quieras recompilar.

.DESCRIPTION
  1. Revisa que Docker Desktop esté instalado y abierto.
  2. Si no existe .env, lo crea a partir de .env.example con una contraseña aleatoria para PostgreSQL.
  3. Revisa los puertos de .env (web, api y db). Si alguno lo usa otro programa o Windows lo
     reservó (pasa con Hyper-V), usa el siguiente libre y lo guarda en .env.
  4. Construye las imágenes, levanta los cuatro contenedores y espera a que estén sanos.
  5. Muestra la dirección de la app.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\setup.ps1
#>

$raiz = $PSScriptRoot
$utf8 = New-Object System.Text.UTF8Encoding $false
$rangosReservados = @()

function Paso([string] $texto) {
    Write-Host "`n==> $texto" -ForegroundColor Cyan
}

function Detener([string] $texto) {
    Write-Host "`n$texto" -ForegroundColor Red
    exit 1
}

# Valor de una variable en las líneas de .env, o $null si no está.
function Leer-Variable($lineas, [string] $nombre) {
    foreach ($linea in $lineas) {
        if ($linea -match ('^\s*' + $nombre + '\s*=\s*(.*?)\s*$')) { return $Matches[1] }
    }
    return $null
}

# Pone NOMBRE=valor en las líneas de .env: reemplaza la línea si ya existe o la agrega al final.
function Escribir-Variable($lineas, [string] $nombre, [string] $valor) {
    for ($i = 0; $i -lt $lineas.Count; $i++) {
        if ($lineas[$i] -match ('^\s*' + $nombre + '\s*=')) { $lineas[$i] = "$nombre=$valor"; return }
    }
    $lineas.Add("$nombre=$valor")
}

# Explica por qué Docker no podría publicar el puerto, o devuelve $null si está libre.
function Motivo-PuertoOcupado([int] $puerto) {
    $rango = $rangosReservados | Where-Object { $puerto -ge $_.Inicio -and $puerto -le $_.Fin } | Select-Object -First 1
    if ($rango) { return "Windows lo reservó: rango $($rango.Inicio)-$($rango.Fin)" }
    $escucha = Get-NetTCPConnection -State Listen -LocalPort $puerto -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $escucha) { return $null }
    $contenedor = & docker ps --filter "publish=$puerto" --format '{{.Names}}' 2> $null | Select-Object -First 1
    if ($contenedor -like 'mediateca-*') { return $null }   # ya es de este proyecto; Compose lo recrea
    if ($contenedor) { return "lo usa el contenedor $contenedor" }
    $proceso = (Get-Process -Id $escucha.OwningProcess -ErrorAction SilentlyContinue).ProcessName
    return "lo usa $proceso, PID $($escucha.OwningProcess)"
}

Push-Location $raiz
try {
    # 1. Docker ---------------------------------------------------------------------------------
    Paso 'Revisando Docker'
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        Detener 'No encontré Docker. Instala Docker Desktop (https://www.docker.com/products/docker-desktop/) o ejecuta: winget install Docker.DockerDesktop'
    }
    & docker info *> $null
    if ($LASTEXITCODE -ne 0) {
        Detener 'Docker Desktop no está abierto. Ábrelo, espera a que el motor esté corriendo y vuelve a ejecutar el script.'
    }
    $versionCompose = & docker compose version 2> $null
    if ($LASTEXITCODE -ne 0) {
        Detener 'Falta Docker Compose v2 ("docker compose"). Actualiza Docker Desktop.'
    }
    Write-Host "    $versionCompose"

    # 2. .env -----------------------------------------------------------------------------------
    $archivoEnv = Join-Path $raiz '.env'
    if (-not (Test-Path $archivoEnv)) {
        Paso 'Creando .env con una contraseña aleatoria para PostgreSQL'
        $caracteres = [char[]]'abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789'
        $bytes = New-Object byte[] 24
        [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
        $clave = -join ($bytes | ForEach-Object { $caracteres[$_ % $caracteres.Length] })
        $lineasEjemplo = [IO.File]::ReadAllLines((Join-Path $raiz '.env.example'), $utf8) | ForEach-Object {
            if ($_ -match '^POSTGRES_PASSWORD=') { "POSTGRES_PASSWORD=$clave" } else { $_ }
        }
        [IO.File]::WriteAllLines($archivoEnv, [string[]] $lineasEjemplo, $utf8)
        Write-Host '    Listo. Ahí también puedes cambiar los puertos y la carpeta de medios.'
    }

    # 3. Puertos --------------------------------------------------------------------------------
    Paso 'Revisando los puertos'
    $rangosReservados = @(& netsh interface ipv4 show excludedportrange protocol=tcp 2> $null | ForEach-Object {
        if ($_ -match '^\s*(\d+)\s+(\d+)') { [pscustomobject]@{ Inicio = [int] $Matches[1]; Fin = [int] $Matches[2] } }
    })
    $lineas = New-Object 'System.Collections.Generic.List[string]'
    $lineas.AddRange([string[]] [IO.File]::ReadAllLines($archivoEnv, $utf8))
    $puertos = [ordered]@{ WEB_PORT = 8080; API_PORT = 5080; DB_PORT = 5433 }
    $asignados = @()
    $cambios = $false
    foreach ($nombre in @($puertos.Keys)) {
        $valor = Leer-Variable $lineas $nombre
        $puerto = if ($valor -match '^\d+$') { [int] $valor } else { $puertos[$nombre] }
        $motivo = Motivo-PuertoOcupado $puerto
        if ($motivo -or ($asignados -contains $puerto)) {
            if (-not $motivo) { $motivo = 'ya lo tiene otro de los tres' }
            $libre = $puerto
            do {
                $libre++
                if ($libre -gt $puerto + 200) { Detener "No encontré un puerto libre para $nombre cerca del $puerto. Elige uno en .env." }
            } while ((Motivo-PuertoOcupado $libre) -or ($asignados -contains $libre))
            Write-Host "    ${nombre}: el $puerto no está disponible ($motivo). Uso el $libre." -ForegroundColor Yellow
            $puerto = $libre
        }
        if ($valor -ne "$puerto") {
            Escribir-Variable $lineas $nombre "$puerto"
            $cambios = $true
        }
        $puertos[$nombre] = $puerto
        $asignados += $puerto
    }
    if ($cambios) { [IO.File]::WriteAllLines($archivoEnv, $lineas.ToArray(), $utf8) }
    Write-Host ('    web {0}, api {1} y db {2} (guardados en .env)' -f $puertos['WEB_PORT'], $puertos['API_PORT'], $puertos['DB_PORT'])
    if ($puertos['API_PORT'] -ne 5080) {
        Write-Host '    Ojo: npm start manda /api al puerto 5080; cámbialo en frontend/proxy.conf.json si lo usas.' -ForegroundColor Yellow
    }

    # 4. Contenedores ---------------------------------------------------------------------------
    Paso 'Construyendo las imágenes y levantando web, api y db (la primera vez tarda varios minutos)'
    & docker compose up -d --build --wait
    if ($LASTEXITCODE -ne 0) {
        Write-Host ''
        & docker compose ps -a
        Write-Host ''
        & docker compose logs --tail 40 api
        Detener 'Algún contenedor no arrancó. Arriba están su estado y las últimas líneas del log de la API.'
    }

    # 5. Dirección de la app --------------------------------------------------------------------
    Write-Host "`nListo: abre http://localhost:$($puertos['WEB_PORT'])" -ForegroundColor Green
    Write-Host "  PostgreSQL: localhost:$($puertos['DB_PORT']), usuario mediateca y la contraseña de .env"
    Write-Host '  Estado:     docker compose ps'
    Write-Host '  Logs:       docker compose logs -f api'
    Write-Host '  Detener:    docker compose down'
}
finally {
    Pop-Location
}
