# Imagen web: compila Angular con Node y sirve el resultado con nginx.

FROM node:24-slim AS build
WORKDIR /app
ENV CI=true NG_CLI_ANALYTICS=false

# Primero las dependencias: mientras package-lock.json no cambie, Docker reutiliza esta capa.
COPY package.json package-lock.json ./
RUN npm ci --no-fund --no-audit

COPY . .
RUN npx ng build

FROM nginx:stable-alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/mediateca-web/browser /usr/share/nginx/html
