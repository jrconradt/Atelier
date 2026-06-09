FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/Directory.Build.props ./
COPY Directory.Packages.props ./

COPY src/ src/
RUN find src -type f -not -name '*.csproj' -delete

COPY boutiques/example/*.csproj boutiques/example/
COPY boutiques/example/*.cs boutiques/example/

RUN dotnet restore boutiques/example/Atelier.Host.Example.csproj

COPY src/ src/
COPY boutiques/example/ boutiques/example/

ARG BUILD_CONFIGURATION=Release
ARG SELF_CONTAINED=false

RUN if [ "$SELF_CONTAINED" = "true" ]; then \
        dotnet publish boutiques/example/Atelier.Host.Example.csproj \
            -c $BUILD_CONFIGURATION \
            -o /app/publish \
            -r linux-x64 \
            --self-contained true \
            /p:PublishSingleFile=true \
            /p:PublishTrimmed=true; \
    else \
        dotnet publish boutiques/example/Atelier.Host.Example.csproj \
            -c $BUILD_CONFIGURATION \
            -o /app/publish \
            /p:UseAppHost=false; \
    fi

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

RUN apk add --no-cache curl bash

RUN addgroup -g 64198 appuser && adduser -u 64198 -G appuser -S -H appuser

COPY --from=build --chown=64198:64198 /app/publish .
RUN mkdir -p /var/atelier/logs && chown -R 64198:64198 /var/atelier/logs /app

USER appuser

ENV ASPNETCORE_ENVIRONMENT="Production"

EXPOSE 8080
       8081
       9090


HEALTHCHECK --interval=10s --timeout=5s --start-period=5s --retries=3 \
    CMD ["curl", "-f", "http://localhost:9090/health"]

ENTRYPOINT ["dotnet", "Atelier.Host.Example.dll"]

