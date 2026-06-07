# InCleanHome.ApiGateway - Dockerfile (multi-stage)
# Stage 1: build + publish using the .NET 9 SDK
# Stage 2: lightweight runtime image that only carries the published output


# Stage 1: build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

WORKDIR /src

# Copy csproj first to leverage Docker layer cache for restored packages
COPY src/InCleanHome.ApiGateway/InCleanHome.ApiGateway.csproj src/InCleanHome.ApiGateway/
RUN dotnet restore "src/InCleanHome.ApiGateway/InCleanHome.ApiGateway.csproj"

# Now copy the rest of the source
COPY . .

# Publish in Release mode
RUN dotnet publish "src/InCleanHome.ApiGateway/InCleanHome.ApiGateway.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

#  Stage 2: runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime

WORKDIR /app

# Install wget for healthcheck (the base image has no curl by default)
RUN apt-get update \
    && apt-get install -y --no-install-recommends wget \
    && rm -rf /var/lib/apt/lists/*

# Run as non-root for safety
RUN useradd -m -u 10001 appuser
USER appuser

COPY --from=build --chown=appuser:appuser /app/publish .

# Default listening port (overridable by ASPNETCORE_URLS env var)
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "InCleanHome.ApiGateway.dll"]
