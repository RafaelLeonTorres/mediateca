# Imágenes del backend: la API (target "api") y el worker (target "worker"). Se compilan con el SDK de
# .NET 11 y corren sobre la imagen de ASP.NET Core 11. El último stage es la API, así que un
# `docker build` sin --target construye la API; docker-compose.yml elige el target de cada servicio.

FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /src

# Primero solo los .csproj: mientras no cambien los paquetes, Docker reutiliza la capa del restore.
COPY src/Mediateca.Core/Mediateca.Core.csproj src/Mediateca.Core/
COPY src/Mediateca.Infrastructure/Mediateca.Infrastructure.csproj src/Mediateca.Infrastructure/
COPY src/Mediateca.Api/Mediateca.Api.csproj src/Mediateca.Api/
COPY src/Mediateca.Worker/Mediateca.Worker.csproj src/Mediateca.Worker/
RUN dotnet restore src/Mediateca.Api/Mediateca.Api.csproj \
    && dotnet restore src/Mediateca.Worker/Mediateca.Worker.csproj

COPY src/ src/
RUN dotnet publish src/Mediateca.Api/Mediateca.Api.csproj -c Release -o /app/api --no-restore -p:UseAppHost=false \
    && dotnet publish src/Mediateca.Worker/Mediateca.Worker.csproj -c Release -o /app/worker --no-restore -p:UseAppHost=false

# Base de la API y del worker.
FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS runtime

# curl solo lo usa el healthcheck de la API en docker-compose.yml. tzdata hace falta para que la
# variable TZ sirva: los horarios del escaneo automático se interpretan en esa zona horaria.
RUN apt-get update \
    && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends curl tzdata \
    && rm -rf /var/lib/apt/lists/*

# /data guarda pósters, llaves de sesión y la caché de AniDB. En Compose es el volumen "appdata".
RUN mkdir -p /data && chown "$APP_UID" /data
ENV Storage__DataPath=/data

WORKDIR /app

# Usuario sin privilegios que traen las imágenes de .NET.
USER $APP_UID

# Worker: corre los escaneos y programa el escaneo automático. No escucha en ningún puerto.
FROM runtime AS worker
COPY --from=build /app/worker .
ENTRYPOINT ["dotnet", "Mediateca.Worker.dll"]

# API (el último stage: es lo que construye un `docker build` sin --target). Escucha en el puerto 8080.
FROM runtime AS api
COPY --from=build /app/api .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Mediateca.Api.dll"]
