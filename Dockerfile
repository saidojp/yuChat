# YuChat single-container image: React build served as static files by the ASP.NET Core API,
# so /api, /hubs/chat and the httpOnly refresh cookie share one origin.

# Stage 1: build the Vite frontend (Vite 8 requires Node ^20.19 || >=22.12).
FROM node:24-alpine AS frontend-build
WORKDIR /src/frontend
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

# Stage 2: restore and publish the .NET 10 backend.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-build
WORKDIR /src
COPY backend/backend.csproj backend/
RUN dotnet restore backend/backend.csproj
COPY backend/ backend/
RUN dotnet publish backend/backend.csproj -c Release -o /app/publish --no-restore
COPY --from=frontend-build /src/frontend/dist/ /app/publish/wwwroot/

# Stage 3: runtime image.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_NOLOGO=1 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
COPY --from=backend-build /app/publish ./

# Heroku injects PORT; the base image sets ASPNETCORE_HTTP_PORTS=8080, so clear it and bind explicitly.
CMD ["sh", "-c", "unset ASPNETCORE_HTTP_PORTS; exec dotnet YuChat.Api.dll --urls http://0.0.0.0:${PORT:-8080}"]
