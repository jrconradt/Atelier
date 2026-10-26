FROM {{ sdkImage }} AS build
WORKDIR /src

COPY global.json nuget.config .editorconfig Directory.Packages.props Directory.Build.Package.props README.md ./
COPY src/ src/
RUN find src -type f -not -name '*.csproj' -not -name '*.props' -delete

RUN dotnet restore {{ hostProject }}

COPY src/ src/

ARG BUILD_CONFIGURATION=Release
ARG SELF_CONTAINED=false

RUN if [ "$SELF_CONTAINED" = "true" ]; then \
        dotnet publish {{ hostProject }} \
            -c $BUILD_CONFIGURATION \
            -o /app/publish \
            -r linux-x64 \
            --self-contained true \
            /p:PublishSingleFile=true \
            /p:PublishTrimmed=true; \
    else \
        dotnet publish {{ hostProject }} \
            -c $BUILD_CONFIGURATION \
            -o /app/publish \
            /p:UseAppHost=false; \
    fi
