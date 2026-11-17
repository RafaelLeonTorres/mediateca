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

Los escaneos no corren dentro de la API: la API (o el escaneo automático) solo los **pone en la cola**, que es una tabla de la base de datos, y el `worker` los toma y los ejecuta. Los dos contenedores usan la misma carpeta de medios (`/media`, de solo lectura) y el mismo volumen de datos.

## Levantar todo por primera vez

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
3. construye las imágenes, levanta los contenedores y espera a que estén sanos. Si algo falla, muestra el estado de los contenedores y el log de la API.

La primera vez tarda varios minutos porque descarga las imágenes base, instala las dependencias y compila. Las siguientes veces Docker reutiliza lo que no cambió.

### macOS o Linux

```bash
cp .env.example .env
sed -i.bak "s/^POSTGRES_PASSWORD=.*/POSTGRES_PASSWORD=$(openssl rand -hex 16)/" .env && rm .env.bak
docker compose up -d --build --wait
```

Abre <http://localhost:8080> y crea la cuenta de administrador.

### Qué pasa en el primer arranque

1. La API crea la base de datos `mediateca` y sus tablas con las migraciones de EF Core. Si ya existe, solo aplica las migraciones pendientes.
2. Siembra los roles `Admin` y `Usuario`.
3. Mientras no haya usuarios, la app muestra el asistente que crea al administrador. Después, ese asistente ya no aparece y la app pide iniciar sesión.
4. El `worker` espera a que la API esté sana (es la API quien prepara la base de datos) y entonces arranca. Por eso `docker compose ps` lo muestra unos segundos después que a los demás.

### Si algo falla

- **Un puerto está ocupado** ("Ports are not available" o "access permissions"). En Windows, `setup.ps1` lo resuelve solo. En macOS o Linux, detén lo que lo usa (por ejemplo, un `dotnet run` que haya quedado abierto) o cambia `WEB_PORT`, `API_PORT` o `DB_PORT` en `.env`.
- **Un contenedor no arranca.** `docker compose ps -a` muestra el estado de cada uno y `docker compose logs api` (o `web`, o `db`) el error.
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

## Configuración

Todo se configura en `.env`; los valores por defecto están en `.env.example`.

| Variable | Para qué | Por defecto |
|---|---|---|
| `POSTGRES_PASSWORD` | Contraseña de PostgreSQL | la genera `setup.ps1` |
| `WEB_PORT`, `API_PORT`, `DB_PORT` | Puertos en tu equipo | 8080, 5080 y 5433 |
| `MEDIA_PATH` | Carpeta con tu música y tus videos; la API la ve como `/media`, de solo lectura | `./media` |
| `API_ENVIRONMENT` | Con `Development`, la API publica su documentación en <http://localhost:5080/scalar> | `Production` |
| `SCAN_CRON` | Horario **inicial** del escaneo automático, en formato cron de Quartz (con segundos). Después se cambia desde Administrar › Bibliotecas | `0 0 3 * * ?` (todos los días a las 3:00) |
| `TZ` | Zona horaria en la que se interpretan los horarios del escaneo automático | `America/Mexico_City` |
| `OMDB_API_KEY`, `ANIDB_CLIENT`, `ANIDB_CLIENT_VERSION` | Metadatos de IMDb (a través de OMDb) y de AniDB | vacíos |

`.env` no se sube a git.

El formato de `SCAN_CRON` es el de Quartz: seis campos, `segundo minuto hora día-del-mes mes día-de-la-semana`, y el día del mes o el de la semana tiene que ser `?`. Por ejemplo, `0 0 */6 * * ?` es cada 6 horas y `0 0 3 ? * SUN`, los domingos a las 3:00. El intervalo mínimo entre escaneos es de 5 minutos. `SCAN_CRON` solo se usa mientras no haya un horario guardado: una vez que lo cambias en la pantalla, manda el de la base de datos.

## Tus bibliotecas

1. **Apunta `MEDIA_PATH` a tu carpeta de medios** en `.env` (por ejemplo, `MEDIA_PATH=D:/Media`) y recrea los contenedores con `docker compose up -d`. La API la ve en `/media`, de solo lectura.
2. **Ordena los archivos con estos nombres** (el detalle completo está en la sección 7 de [PLAN.md](PLAN.md)):

   | Tipo | Estructura | Ejemplo |
   |---|---|---|
   | Películas | `Película (Año)/Película (Año).ext` | `Peliculas/Heat (1995)/Heat (1995).mkv` |
   | Series | `Serie (Año)/Temporada NN/… SxxEyy` | `Series/Mi Serie (2019)/Temporada 01/Mi Serie S01E02.mkv` |
   | Anime | `Serie/… - NN` o `SxxEyy` | `Anime/Mi Anime/[Grupo] Mi Anime - 05 [1080p].mkv` |
   | Música | `Artista/Álbum/NN - Título` (las etiquetas del archivo mandan) | `Musica/Miles Davis/Kind of Blue/01 - So What.flac` |

   En el nombre de la carpeta puedes agregar `[imdbid-tt0113277]` o `[anidbid-23]`. Se ignoran las carpetas y los archivos que empiezan con punto.
3. **Crea la biblioteca.** Entra como administrador, abre **Administrar** y elige **Nueva biblioteca**: nombre, tipo y una o más carpetas (con el selector, que solo muestra lo que hay dentro de `/media`).
4. **Escanéala.** Pulsa **Escanear** (o **Escanear todas**): el escaneo entra a la cola, el `worker` lo toma y el avance se actualiza cada 2 segundos. Los siguientes escaneos son incrementales: solo procesan lo nuevo, lo modificado y lo que desapareció.
5. **Deja que se escaneen solas.** Más abajo, en **Escaneo automático**, activa **Revisar cambios automáticamente** y elige la frecuencia (cada hora, cada 6 o 12 horas, diario a las 3:00, semanal o un cron propio). Pulsa **Guardar horario**: el worker lo aplica en menos de un minuto, sin reiniciar nada. La pantalla muestra la zona horaria y las próximas 3 ejecuciones.

Al terminar, el inicio muestra lo agregado recientemente y tus bibliotecas.

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

Con `dotnet watch` la API corre fuera de Docker y no ve `/media`: indica la carpeta de medios de tu equipo (la misma de `MEDIA_PATH`) con otro user-secret y usa esa ruta al crear las bibliotecas:

```powershell
dotnet user-secrets set "Storage:MediaRoots:0" "D:/Media" --project backend/src/Mediateca.Api
```

La API queda en <http://localhost:5080> y su documentación en <http://localhost:5080/scalar>. Para volver a la API en Docker: `docker compose start api`.

**Worker con recarga.** Los escaneos los corre el worker, así que con la API fuera de Docker también necesitas el worker fuera de Docker: el contenedor `worker` no ve las carpetas de tu equipo, solo `/media`. Detenlo y corre el proyecto en otra terminal:

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

## Pruebas

```powershell
dotnet test backend
cd frontend; npm ci; npx ng test --watch=false
```

Las pruebas de integración levantan su propio PostgreSQL con Testcontainers, así que necesitan Docker abierto. Las pruebas de integración registran el procesador de escaneos dentro de la API de prueba, así que no necesitan un segundo proceso. GitHub Actions corre estas pruebas en cada pull request y, además, levanta los contenedores y consulta `/api/health` a través de nginx.

## Estructura

```text
├── docker-compose.yml          los cuatro contenedores
├── setup.ps1                   primer arranque en Windows
├── .env.example                configuración (se copia a .env)
├── PLAN.md                     plan del proyecto
├── backend/
│   ├── Dockerfile              imágenes de la API y del worker
│   ├── Mediateca.slnx
│   ├── src/
│   │   ├── Mediateca.Api/             endpoints y configuración
│   │   ├── Mediateca.Core/            entidades, reglas e interfaces
│   │   ├── Mediateca.Infrastructure/  EF Core, escáner, cola de escaneos y proveedores de metadatos
│   │   └── Mediateca.Worker/          escaneos de la cola y escaneo automático (Quartz)
│   └── tests/
│       ├── Mediateca.UnitTests/
│       └── Mediateca.IntegrationTests/
└── frontend/                   Angular 22
    ├── Dockerfile              compila Angular y lo sirve con nginx
    └── nginx.conf              archivos de Angular y proxy de /api
```

## Licencia

[MIT](LICENSE)
