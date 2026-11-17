# Mediateca

Gestor de contenido multimedia: organiza y reproduce tu colección de música, películas, series y anime desde el navegador. Proyecto de portafolio con .NET 11, EF Core 11 (Code First), PostgreSQL 18 y Angular 22, que corre en Docker.

> En construcción: fase 0 (estructura base). El plan completo, con las fases y los criterios de aceptación, está en [PLAN.md](PLAN.md).

## Cómo está armado

Tres contenedores con Docker Compose:

| Contenedor | Qué corre | Puerto en tu equipo (por defecto) |
|---|---|---|
| `web` | nginx con el build de Angular; manda `/api` a la API | 8080 |
| `api` | API de ASP.NET Core 11 | 5080, solo desde tu equipo |
| `db` | PostgreSQL 18 | 5433, solo desde tu equipo |

El navegador solo habla con `web`, así que la app y la API comparten origen: la cookie de sesión y el streaming de audio y video funcionan sin CORS ni tokens en las URL.

## Levantar todo por primera vez

Solo necesitas [Docker Desktop](https://www.docker.com/products/docker-desktop/) (en Linux, Docker Engine con el plugin de Compose). .NET y Node.js se usan dentro de las imágenes, así que no hace falta instalarlos para correr la app.

### Windows

1. Abre Docker Desktop y espera a que el motor esté corriendo.
2. En PowerShell, desde la carpeta del repositorio:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\setup.ps1
   ```

3. Abre <http://localhost:8080>. La página debe decir "La API responde correctamente".

El script:

1. crea `.env` con una contraseña aleatoria para PostgreSQL, si todavía no existe;
2. revisa que los tres puertos estén libres. Si alguno lo usa otro programa o Windows lo reservó (pasa con Hyper-V), usa el siguiente libre y lo guarda en `.env`;
3. construye las imágenes, levanta los tres contenedores y espera a que estén sanos. Si algo falla, muestra el estado de los contenedores y el log de la API.

La primera vez tarda varios minutos porque descarga las imágenes base, instala las dependencias y compila. Las siguientes veces Docker reutiliza lo que no cambió.

### macOS o Linux

```bash
cp .env.example .env
sed -i.bak "s/^POSTGRES_PASSWORD=.*/POSTGRES_PASSWORD=$(openssl rand -hex 16)/" .env && rm .env.bak
docker compose up -d --build --wait
```

Abre <http://localhost:8080>.

### Si algo falla

- **Un puerto está ocupado** ("Ports are not available" o "access permissions"). En Windows, `setup.ps1` lo resuelve solo. En macOS o Linux, detén lo que lo usa (por ejemplo, un `dotnet run` que haya quedado abierto) o cambia `WEB_PORT`, `API_PORT` o `DB_PORT` en `.env`.
- **Un contenedor no arranca.** `docker compose ps -a` muestra el estado de cada uno y `docker compose logs api` (o `web`, o `db`) el error.
- **Cambiaste `POSTGRES_PASSWORD` después del primer arranque.** La contraseña se fija cuando se crea el volumen de la base. Ejecuta `docker compose down -v` para empezar de cero; se borran los datos.

## Uso diario

| Para | Comando |
|---|---|
| Levantar sin recompilar | `docker compose up -d` |
| Recompilar después de cambiar el código | `docker compose up -d --build --wait` (o `.\setup.ps1`) |
| Recompilar sola cada vez que guardas | `docker compose watch` |
| Ver el estado | `docker compose ps` |
| Ver el log de la API | `docker compose logs -f api` |
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
| `OMDB_API_KEY`, `ANIDB_CLIENT`, `ANIDB_CLIENT_VERSION` | Metadatos de IMDb (a través de OMDb) y de AniDB | vacíos |

`.env` no se sube a git.

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

La API queda en <http://localhost:5080> y su documentación en <http://localhost:5080/scalar>. Para volver a la API en Docker: `docker compose start api`.

## Pruebas

```powershell
dotnet test backend
cd frontend; npm ci; npx ng test --watch=false
```

GitHub Actions corre estas pruebas en cada pull request y, además, levanta los tres contenedores y consulta `/api/health` a través de nginx.

## Estructura

```text
├── docker-compose.yml          los tres contenedores
├── setup.ps1                   primer arranque en Windows
├── .env.example                configuración (se copia a .env)
├── PLAN.md                     plan del proyecto
├── backend/
│   ├── Dockerfile              imagen de la API
│   ├── Mediateca.slnx
│   ├── src/
│   │   ├── Mediateca.Api/             endpoints y configuración
│   │   ├── Mediateca.Core/            entidades, reglas e interfaces
│   │   └── Mediateca.Infrastructure/  EF Core, escáner y proveedores de metadatos
│   └── tests/
│       ├── Mediateca.UnitTests/
│       └── Mediateca.IntegrationTests/
└── frontend/                   Angular 22
    ├── Dockerfile              compila Angular y lo sirve con nginx
    └── nginx.conf              archivos de Angular y proxy de /api
```

## Licencia

[MIT](LICENSE)
