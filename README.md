# Mediateca

Gestor de contenido multimedia: organiza y reproduce tu colección de música, películas, series y anime desde el navegador. Proyecto de portafolio con .NET 11, EF Core 11 (Code First), PostgreSQL 18 y Angular 22, que corre en Docker.

> En construcción: fase 1 (primer arranque e inicio de sesión). El plan completo, con las fases y los criterios de aceptación, está en [PLAN.md](PLAN.md).

## Cómo está armado

Cuatro contenedores con Docker Compose:

| Contenedor | Qué corre | Puerto en tu equipo (por defecto) |
|---|---|---|
| `web` | nginx con el build de Angular; manda `/api` a la API | 8080 |
| `api` | API de ASP.NET Core 11; pone los escaneos en la cola | 5080, solo desde tu equipo |
| `worker` | Corre los escaneos de la cola y el escaneo automático (Quartz) | ninguno |
| `db` | PostgreSQL 18 | 5433, solo desde tu equipo |

El navegador solo habla con `web`, así que la app y la API comparten origen: la cookie de sesión y el streaming de audio y video funcionan sin CORS ni tokens en las URL.

Los escaneos no corren dentro de la API: la API (o el escaneo automático) solo los **pone en la cola**, que es una tabla de la base de datos, y el `worker` los toma y los ejecuta. Los dos contenedores ven los mismos discos (en `/discos`, de solo lectura) y el mismo volumen de datos.

## Levantar todo por primera vez

**El primer arranque no necesita ninguna configuración de rutas.** `setup.ps1` monta tus discos, de solo lectura, dentro de los contenedores, y las carpetas de cada biblioteca se eligen después desde la app, en **Administrar › Bibliotecas**. La app arranca igual aunque todavía no haya bibliotecas.

Solo necesitas [Docker Desktop](https://www.docker.com/products/docker-desktop/) (en Linux, Docker Engine con el plugin de Compose). .NET y Node.js se usan dentro de las imágenes, así que no hace falta instalarlos para correr la app.

### Windows

1. Abre Docker Desktop y espera a que el motor esté corriendo.
2. En PowerShell, desde la carpeta del repositorio:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\setup.ps1
   ```

3. Abre <http://localhost:8080>. La primera vez, la app te pide crear la cuenta de administrador.

El script:

1. crea `.env` con una contraseña aleatoria para PostgreSQL, si todavía no existe;
2. revisa que los tres puertos estén libres. Si alguno lo usa otro programa o Windows lo reservó (pasa con Hyper-V), usa el siguiente libre y lo guarda en `.env`;
3. monta tus discos fijos (`C:\`, `D:\`, `F:\`...) en solo lectura dentro de `api` y `worker`, como `/discos/C`, `/discos/D`... Para eso genera `docker-compose.override.yml`, que Compose une automáticamente con `docker-compose.yml` (ver [Tus discos](#tus-discos));
4. si `MEDIA_PATH` apunta a una carpeta que no existe, lo regresa a `./media` para que Docker pueda arrancar;
5. construye las imágenes, levanta los contenedores y espera a que estén sanos. Si algo falla, muestra el estado de los contenedores y el log de la API.

La primera vez tarda varios minutos porque descarga las imágenes base, instala las dependencias y compila. Las siguientes veces Docker reutiliza lo que no cambió.

### macOS o Linux

```bash
cp .env.example .env
sed -i.bak "s/^POSTGRES_PASSWORD=.*/POSTGRES_PASSWORD=$(openssl rand -hex 16)/" .env && rm .env.bak
docker compose up -d --build --wait
```

Abre <http://localhost:8080> y crea la cuenta de administrador.

En macOS y Linux no hay script que monte los discos: hazlo con tu propio `docker-compose.override.yml` (ver [Tus discos](#tus-discos)). Mientras no lo hagas, la app funciona, pero el selector de carpetas estará vacío.

### Qué pasa en el primer arranque

1. La API crea la base de datos `mediateca` y sus tablas con las migraciones de EF Core. Si ya existe, solo aplica las migraciones pendientes.
2. Siembra los roles `Admin` y `Usuario`.
3. Mientras no haya usuarios, la app muestra el asistente que crea al administrador. Después, ese asistente ya no aparece y la app pide iniciar sesión.
4. El `worker` espera a que la API esté sana (es la API quien prepara la base de datos) y entonces arranca. Por eso `docker compose ps` lo muestra unos segundos después que a los demás.

### Si algo falla

- **Un puerto está ocupado** ("Ports are not available" o "access permissions"). En Windows, `setup.ps1` lo resuelve solo. En macOS o Linux, detén lo que lo usa (por ejemplo, un `dotnet run` que haya quedado abierto) o cambia `WEB_PORT`, `API_PORT` o `DB_PORT` en `.env`.
- **Un contenedor no arranca.** `docker compose ps -a` muestra el estado de cada uno y `docker compose logs api` (o `web`, o `db`) el error.
- **El selector de carpetas está vacío o no aparece un disco.** En Windows, corre `setup.ps1` otra vez (monta los discos que hay en ese momento y vuelve a levantar los contenedores). En macOS y Linux, revisa que tu `docker-compose.override.yml` monte las carpetas en `/discos/<nombre>` y vuelve a correr `docker compose up -d`.
- **Docker no inicia `api` o `worker` y dice que no existe la ruta de un montaje** (por ejemplo, `bind source path does not exist: F:\`). Desconectaste un disco que estaba montado. Conéctalo otra vez, o corre `setup.ps1`, que vuelve a generar el override solo con los discos que hay.
- **Un escaneo termina con un aviso de "No se pudo leer…".** La carpeta de la biblioteca no estaba disponible (un disco desconectado, por ejemplo). El escaneo no borra nada de lo que ya estaba en el catálogo: conecta el disco, o corre `setup.ps1` si es nuevo, y vuelve a escanear.
- **Un escaneo se queda en "En espera".** Los escaneos los corre el `worker`: si el contenedor no está corriendo, los escaneos se quedan en la cola y, pasados 30 segundos, la pantalla de bibliotecas lo avisa. Revisa `docker compose ps` y `docker compose logs worker`.
- **Cambiaste `POSTGRES_PASSWORD` después del primer arranque.** La contraseña se fija cuando se crea el volumen de la base. Ejecuta `docker compose down -v` para empezar de cero; se borran los datos.

## Uso diario

| Para | Comando |
|---|---|
| Levantar sin recompilar | `docker compose up -d` |
| Recompilar después de cambiar el código | `docker compose up -d --build --wait` (o `.\setup.ps1`) |
| Recompilar sola cada vez que guardas | `docker compose watch` |
| Ver el estado | `docker compose ps` |
| Ver el log de la API | `docker compose logs -f api` |
| Ver el log del worker (escaneos y escaneo automático) | `docker compose logs -f worker` |
| Detener (los datos se quedan en los volúmenes) | `docker compose down` |
| Empezar de cero (borra la base y los datos de la app) | `docker compose down -v` |

## Ícono en la bandeja (Windows)

Como Jellyfin, Mediateca puede tener un ícono junto al reloj para iniciarla, detenerla y abrirla sin usar la terminal. Un contenedor no puede dibujar ese ícono, así que es una app pequeña de Windows (`backend/src/Mediateca.Tray`) que controla los contenedores con Docker Compose.

```powershell
powershell -ExecutionPolicy Bypass -File .\bandeja.ps1
```

Necesita el SDK de .NET 11. El script compila la app, la instala en `%LOCALAPPDATA%\Mediateca\Bandeja` y la abre. El ícono es cian cuando Mediateca está en ejecución y gris si no; clic derecho abre el menú:

| Opción del menú | Qué hace |
|---|---|
| Iniciar con Windows | Abre el ícono al iniciar sesión; si Mediateca está detenida, la inicia (y abre Docker Desktop si hace falta) |
| Iniciar Mediateca | `docker compose up -d` |
| Detener Mediateca | `docker compose stop` |
| Abrir Mediateca | Abre `http://localhost:WEB_PORT` (doble clic en el ícono hace lo mismo) |
| Ver registros | Abre una consola con `docker compose logs -f` |
| Salir | Quita el ícono; los contenedores siguen corriendo |

Después de cambiar el código del ícono, vuelve a correr `bandeja.ps1`: cierra el que está abierto, lo reemplaza y lo abre otra vez.

## Configuración

Todo se configura en `.env`; los valores por defecto están en `.env.example`.

| Variable | Para qué | Por defecto |
|---|---|---|
| `POSTGRES_PASSWORD` | Contraseña de PostgreSQL | la genera `setup.ps1` |
| `WEB_PORT`, `API_PORT`, `DB_PORT` | Puertos en tu equipo | 8080, 5080 y 5433 |
| `MEDIA_PATH` | **Opcional.** Una carpeta extra (por ejemplo, un NAS); los contenedores la ven como `/media`, de solo lectura, y en el selector aparece como "Carpeta de medios". No hace falta para usar tus discos | `./media` |
| `API_ENVIRONMENT` | Con `Development`, la API publica su documentación en <http://localhost:5080/scalar> | `Production` |
| `SCAN_CRON` | Horario **inicial** del escaneo automático, en formato cron de Quartz (con segundos). Después se cambia desde Administrar › Bibliotecas | `0 0 3 * * ?` (todos los días a las 3:00) |
| `TZ` | Zona horaria en la que se interpretan los horarios del escaneo automático | `America/Mexico_City` |
| `OMDB_API_KEY` | Clave gratuita de [OMDb](https://www.omdbapi.com/apikey.aspx) para los metadatos de películas y series que no son anime (IMDb). El anime usa AniList y no necesita clave | vacía |

`.env` no se sube a git.

El formato de `SCAN_CRON` es el de Quartz: seis campos, `segundo minuto hora día-del-mes mes día-de-la-semana`, y el día del mes o el de la semana tiene que ser `?`. Por ejemplo, `0 0 */6 * * ?` es cada 6 horas y `0 0 3 ? * SUN`, los domingos a las 3:00. El intervalo mínimo entre escaneos es de 5 minutos. `SCAN_CRON` solo se usa mientras no haya un horario guardado: una vez que lo cambias en la pantalla, manda el de la base de datos.

## Tus bibliotecas

1. **Ten tus discos montados.** En Windows ya lo hizo `setup.ps1`; en macOS y Linux, tu override (ver [Tus discos](#tus-discos)).
2. **Ordena los archivos con estos nombres** (el detalle completo está en la sección 7 de [PLAN.md](PLAN.md)):

   | Tipo | Estructura | Ejemplo |
   |---|---|---|
   | Películas | `Película (Año)/Película (Año).ext` | `Peliculas/Heat (1995)/Heat (1995).mkv` |
   | Series | `Serie (Año)/Temporada NN/… SxxEyy` | `Series/Mi Serie (2019)/Temporada 01/Mi Serie S01E02.mkv` |
   | Anime | `Serie/… - NN` o `SxxEyy` | `Anime/Mi Anime/[Grupo] Mi Anime - 05 [1080p].mkv` |
   | Música | `Artista/Álbum/NN - Título` (las etiquetas del archivo mandan) | `Musica/Miles Davis/Kind of Blue/01 - So What.flac` |

   En el nombre de la carpeta puedes agregar `[imdbid-tt0113277]` o `[anilistid-21]` para fijar qué es (se usa en lugar de buscar por nombre). Las etiquetas `[anidbid-…]` se reconocen y se descartan: ya no se usa AniDB. Se ignoran las carpetas y los archivos que empiezan con punto.
3. **Crea la biblioteca.** Entra como administrador, abre **Administrar › Bibliotecas** y elige **Nueva biblioteca**: nombre, tipo (películas, series, anime o música) y una o más carpetas, que eliges con el selector en cualquier disco ("Disco C:", "Disco F:"...). Cada biblioteca tiene sus propias carpetas, y se ven con su ruta de Windows (`F:\Peliculas`).
4. **Escanéala.** Pulsa **Escanear** (o **Escanear todas**): el escaneo entra a la cola, el `worker` lo toma y el avance se actualiza cada 2 segundos. Los siguientes escaneos son incrementales: solo procesan lo nuevo, lo modificado y lo que desapareció.
5. **Deja que se escaneen solas.** Más abajo, en **Escaneo automático**, activa **Revisar cambios automáticamente** y elige la frecuencia (cada hora, cada 6 o 12 horas, diario a las 3:00, semanal o un cron propio). Pulsa **Guardar horario**: el worker lo aplica en menos de un minuto, sin reiniciar nada. La pantalla muestra la zona horaria y las próximas 3 ejecuciones.

6. **Busca los metadatos.** En la misma pantalla, cada biblioteca de anime, series o películas muestra cuántos elementos tienen metadatos, cuántos esperan, cuántos no se encontraron y cuántos fallaron. Pulsa **Buscar metadatos** y el `worker` los trae poco a poco, sin que tengas que esperar en la pantalla. Si falta la clave de OMDb, la pantalla lo avisa. Para las películas y series que no son anime, define `OMDB_API_KEY` en `.env` y reinicia (`docker compose up -d`).

Al terminar, el inicio muestra lo agregado recientemente y tus bibliotecas.

### Metadatos

- **De dónde salen.** El anime, de [AniList](https://anilist.co) (sin clave, con su aviso "Datos de AniList" en la ficha). Las películas y las series que no son anime, de IMDb a través de OMDb.
- **Cómo se ve la ficha.** Como en Jellyfin: imagen de fondo con un degradado oscuro, póster, título y título original, años ("2026 – Actualidad"), estado, calificación, clasificación por edad, duración, sinopsis, "Se emite los martes a las 23:00" (en tu zona horaria), géneros, estudios, etiquetas y enlaces a AniList, MyAnimeList e IMDb. Los episodios muestran su título cuando el proveedor lo conoce.
- **Tus imágenes mandan.** Si la carpeta tiene `folder.jpg` (póster), `backdrop.jpg` (fondo) o `NN-thumb.jpg` (miniatura de un episodio), se usan esas. Solo cuando no hay, se baja la del proveedor, una sola vez, y se guarda en el volumen de datos.
- **Si se equivoca.** Entra a la ficha como administrador y pulsa **Identificar**: busca por nombre y elige el correcto, o pega un id o una dirección de AniList o de IMDb. **Actualizar metadatos** vuelve a pedirlos con el mismo id. También puedes fijar el id en el nombre de la carpeta (`[anilistid-21]`).
- **Series en emisión.** Se actualizan solas cada 24 horas aproximadamente, para tener el siguiente episodio.
- **Límites.** AniList se consulta como máximo una vez cada 2 segundos, y OMDb permite 1000 consultas al día en la clave gratuita. Si el servicio pide esperar o se agota el cupo, la cola se pausa sola y sigue después: la administración lo indica.

### Tus discos

Los contenedores `api` y `worker` ven tus discos en `/discos/<letra>`, de solo lectura: la app nunca modifica tus archivos. El selector de carpetas solo navega dentro de `/discos` y de `/media`, y solo lo usan los administradores.

**Windows.** `setup.ps1` detecta tus discos fijos y genera `docker-compose.override.yml` (está en `.gitignore`) con un montaje por disco, en `api` y en `worker`:

```yaml
# Generado por setup.ps1: tus discos, de solo lectura, para elegir las carpetas de las bibliotecas
# desde la app. Se vuelve a generar cada vez que corres setup.ps1 (después de conectar o quitar un disco).
services:
  api:
    volumes:
      - type: bind
        source: 'C:\'
        target: /discos/C
        read_only: true
  worker:
    volumes:
      - type: bind
        source: 'C:\'
        target: /discos/C
        read_only: true
```

- **Disco nuevo:** después de conectarlo, corre `setup.ps1` otra vez para que aparezca en la app.
- **Disco desconectado:** si quitas un disco que estaba montado, Docker no puede iniciar `api` ni `worker` hasta que lo conectes de nuevo o corras `setup.ps1`, que vuelve a generar el archivo solo con los discos que hay. Mientras tanto, las bibliotecas de ese disco no se borran: sus escaneos terminan con un aviso y no quitan nada del catálogo.
- Si ya tienes un `docker-compose.override.yml` propio (sin la primera línea `# Generado por setup.ps1`), el script no lo toca y te explica cómo agregar los discos a mano.

**macOS y Linux.** Escribe tu propio `docker-compose.override.yml` en la raíz del repositorio, con una carpeta por montaje bajo `/discos/<nombre>` (en `api` y en `worker`). Por ejemplo, tu carpeta personal y un disco externo:

```yaml
services:
  api:
    volumes:
      - type: bind
        source: /home/usuario
        target: /discos/usuario
        read_only: true
      - type: bind
        source: /mnt/discos/externo
        target: /discos/externo
        read_only: true
  worker:
    volumes:
      - type: bind
        source: /home/usuario
        target: /discos/usuario
        read_only: true
      - type: bind
        source: /mnt/discos/externo
        target: /discos/externo
        read_only: true
```

En el selector aparecen con el nombre de la carpeta (`usuario`, `externo`). Después corre `docker compose up -d`. Solo las carpetas con una letra como nombre (`/discos/C`) se muestran como "Disco C:" y con rutas de Windows.

**`MEDIA_PATH` (opcional).** Si quieres una carpeta extra, como un NAS, ponla en `.env` (`MEDIA_PATH=//servidor/medios` o `MEDIA_PATH=D:/Media`): aparece en el selector como "Carpeta de medios". Si no la defines, Docker crea una carpeta `./media` vacía y no pasa nada.

**Seguridad.** Los discos se montan de solo lectura. Solo los administradores pueden explorar las carpetas del servidor. Las imágenes, los streams y los subtítulos solo salen de las carpetas de las bibliotecas o de las carpetas de medios, nunca de un archivo cualquiera.

## Reproductor, reescalado y subtítulos

El video se ve con un reproductor propio: controles que se ocultan solos a los 3 segundos, barra de avance que se arrastra, volumen, pantalla completa y atajos de teclado (espacio o K, flechas ±10 s, M, C y F).

**Reescalar.** Es un botón de la barra de controles que mejora la imagen con tu tarjeta gráfica, en el navegador (WebGPU): elige la vista (Original, Mejorado o Comparar con un divisor que se arrastra), la resolución de salida (Pantalla, 720p, 1080p, 1440p o 4K), el escalado (Bilineal, Bicúbico, Lanczos 2 o Lanczos 3) y la nitidez, la reducción de ruido, el anti-ringing y el dithering. Por omisión el video se ve en Original, sin usar la GPU, y el navegador recuerda lo último que elegiste. **Requiere Chrome o Edge actualizados con WebGPU**; sin él, el botón aparece deshabilitado y el video se ve normal. Un reescalado no agrega detalle que el archivo no tenga: mejora la nitidez y el escalado, y con una GPU integrada o por software puede ir lento.

**Subtítulos.** El botón CC (o la tecla C) está siempre en la barra. Los subtítulos de texto se leen de las pistas incrustadas (MKV, MP4...) y de los archivos sueltos junto al video con el mismo nombre y la extensión `.srt`, `.vtt`, `.ass` o `.ssa`; por ejemplo `Capitulo 01.mkv` y `Capitulo 01.es.srt` o `Capitulo 01.en.forced.vtt`. Si el video no trae, o quieres otros, elige **Cargar archivo…** en el menú CC: subes un archivo de tu equipo (hasta 8 MB), eliges el idioma y se activa al instante. Queda guardado en el servidor para todos los que pueden ver ese video, con "(subido)" en su nombre, y solo tú o un administrador lo pueden borrar desde el mismo menú. Por omisión están desactivados; el idioma que elijas se activa solo en el siguiente episodio. No se ofrecen los subtítulos de imagen (PGS, VobSub). La imagen de Docker de la API trae `ffmpeg` y `ffprobe` para leer las pistas incrustadas; si corres la API sin ellos solo se pierden esas pistas (los archivos sueltos siguen funcionando) y se puede indicar su ruta con `Media:Ffmpeg` y `Media:Ffprobe`.

## Desarrollo

Para correr las pruebas y trabajar con recarga instantánea necesitas el [SDK de .NET 11](https://dotnet.microsoft.com/download) y [Node.js 24](https://nodejs.org/). En Windows: `winget install Microsoft.DotNet.SDK.11` y `winget install OpenJS.NodeJS.LTS`.

**Frontend con recarga instantánea.** Con los contenedores arriba:

```powershell
cd frontend
npm ci
npm start
```

Abre <http://localhost:4200>. `ng serve` manda `/api` al contenedor de la API (puerto 5080) con `proxy.conf.json`.

**Backend con recarga.** Detén el contenedor de la API para liberar el puerto 5080, guarda una sola vez la cadena de conexión en los user-secrets y corre la API con `dotnet watch`:

```powershell
docker compose stop api
$clave = (Select-String -Path .env -Pattern '^POSTGRES_PASSWORD=(.*)').Matches[0].Groups[1].Value
$puerto = (Select-String -Path .env -Pattern '^DB_PORT=(\d+)').Matches[0].Groups[1].Value
dotnet user-secrets set ConnectionStrings:Default "Host=localhost;Port=$puerto;Database=mediateca;Username=mediateca;Password=$clave" --project backend/src/Mediateca.Api
dotnet watch --project backend/src/Mediateca.Api
```

Con `dotnet watch` la API corre fuera de Docker y no ve `/discos` ni `/media` (el selector queda vacío): indica la carpeta de medios de tu equipo con otro user-secret y usa esa ruta al crear las bibliotecas:

```powershell
dotnet user-secrets set "Storage:MediaRoots:0" "D:/Media" --project backend/src/Mediateca.Api
```

La API queda en <http://localhost:5080> y su documentación en <http://localhost:5080/scalar>. Para volver a la API en Docker: `docker compose start api`.

**Worker con recarga.** Los escaneos los corre el worker, así que con la API fuera de Docker también necesitas el worker fuera de Docker: el contenedor `worker` no ve las carpetas de tu equipo, solo `/discos` y `/media`. Detenlo y corre el proyecto en otra terminal:

```powershell
docker compose stop worker
dotnet run --project backend/src/Mediateca.Worker
```

El worker comparte los user-secrets con la API (tienen el mismo `UserSecretsId`), así que la cadena de conexión y `Storage:MediaRoots:0` de arriba ya le sirven. Define además una **ruta absoluta** para `Storage:DataPath` una sola vez; si fuera relativa, la API y el worker usarían cada uno su propia carpeta `data` y no compartirían pósters ni carátulas:

```powershell
dotnet user-secrets set "Storage:DataPath" "D:/Mediateca/data" --project backend/src/Mediateca.Api
```

En desarrollo, el horario inicial y la zona horaria salen de `appsettings.json` y de la zona de tu equipo. Para volver al worker en Docker: `docker compose start worker`.

**Cambios en la base de datos.** Las entidades están en `Mediateca.Core` y su configuración en `Mediateca.Infrastructure/Persistence`. Después de cambiarlas, crea una migración; la API la aplica sola al arrancar (el worker nunca migra):

```powershell
cd backend
dotnet tool restore
dotnet ef migrations add NombreDelCambio --project src/Mediateca.Infrastructure --startup-project src/Mediateca.Api --output-dir Persistence/Migrations
```

El cambio que movió los escaneos al worker agrega la tabla `ScanSchedules` y el índice único de escaneo activo por biblioteca, así que necesita su propia migración (nombre sugerido: `WorkerYHorarioDeEscaneo`). Mientras no la generes, la API no arranca y las pruebas de integración fallan, porque EF Core 11 no migra con cambios de modelo pendientes.

Los metadatos (fase 4) cambian `AniDbId` por `AniListId` y agregan columnas e índices a `MediaItems`, así que también necesitan su migración: `dotnet ef migrations add Metadatos --project src/Mediateca.Infrastructure --startup-project src/Mediateca.Api --output-dir Persistence/Migrations`. La migración borra la columna `AniDbId`: si ya guardaste ids de AniDB, no se conservan (AniDB ya no se usa).

## Pruebas

```powershell
dotnet test backend
cd frontend; npm ci; npx ng test --watch=false
```

Las pruebas de integración levantan su propio PostgreSQL con Testcontainers, así que necesitan Docker abierto. Las pruebas de integración registran el procesador de escaneos dentro de la API de prueba, así que no necesitan un segundo proceso. GitHub Actions corre estas pruebas en cada pull request y, además, levanta los contenedores y consulta `/api/health` a través de nginx.

## Estructura

```text
├── docker-compose.yml          los cuatro contenedores
├── setup.ps1                   primer arranque en Windows (también monta tus discos)
├── bandeja.ps1                 instala el ícono de la bandeja de Windows
├── docker-compose.override.yml generado por setup.ps1 o escrito a mano; no se sube a git
├── .env.example                configuración (se copia a .env)
├── PLAN.md                     plan del proyecto
├── backend/
│   ├── Dockerfile              imágenes de la API y del worker
│   ├── Mediateca.slnx
│   ├── src/
│   │   ├── Mediateca.Api/             endpoints y configuración
│   │   ├── Mediateca.Core/            entidades, reglas e interfaces
│   │   ├── Mediateca.Infrastructure/  EF Core, escáner, cola de escaneos y proveedores de metadatos
│   │   ├── Mediateca.Worker/          escaneos de la cola y escaneo automático (Quartz)
│   │   └── Mediateca.Tray/            ícono de la bandeja de Windows (fuera de Docker)
│   └── tests/
│       ├── Mediateca.UnitTests/
│       └── Mediateca.IntegrationTests/
└── frontend/                   Angular 22
    ├── Dockerfile              compila Angular y lo sirve con nginx
    └── nginx.conf              archivos de Angular y proxy de /api
```

## Licencia

[MIT](LICENSE)
