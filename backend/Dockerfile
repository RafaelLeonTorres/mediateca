# Imagen de la API: se compila con el SDK de .NET 11 y corre sobre la imagen de ASP.NET Core 11.

FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /src

# Primero solo los .csproj: mientras no cambien los paquetes, Docker reutiliza la capa del restore.
COPY src/Mediateca.Core/Mediateca.Core.csproj src/Mediateca.Core/
COPY src/Mediateca.Infrastructure/Mediateca.Infrastructure.csproj src/Mediateca.Infrastructure/
COPY src/Mediateca.Api/Mediateca.Api.csproj src/Mediateca.Api/
RUN dotnet restore src/Mediateca.Api/Mediateca.Api.csproj

COPY src/ src/
RUN dotnet publish src/Mediateca.Api/Mediateca.Api.csproj -c Release -o /app --no-restore -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:11.0

# curl solo lo usa el healthcheck de docker-compose.yml.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# /data guarda pósters, llaves de sesión y la caché de AniDB. En Compose es el volumen "appdata".
RUN mkdir -p /data && chown "$APP_UID" /data
ENV Storage__DataPath=/data

WORKDIR /app
COPY --from=build /app .

# Usuario sin privilegios que traen las imágenes de .NET. La API escucha en el puerto 8080.
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Mediateca.Api.dll"]
