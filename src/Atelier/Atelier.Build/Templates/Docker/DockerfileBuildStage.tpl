FROM {{ sdkImage }} AS build
WORKDIR /src

COPY src/Directory.Build.props ./
COPY Directory.Packages.props ./

COPY src/ src/
RUN find src -type f -not -name '*.csproj' -delete

COPY boutiques/{{ boutiqueDir }}/*.csproj boutiques/{{ boutiqueDir }}/
COPY boutiques/{{ boutiqueDir }}/*.cs boutiques/{{ boutiqueDir }}/

RUN dotnet restore boutiques/{{ boutiqueDir }}/{{ projectName }}.csproj

COPY src/ src/
COPY boutiques/{{ boutiqueDir }}/ boutiques/{{ boutiqueDir }}/

ARG BUILD_CONFIGURATION=Release
ARG SELF_CONTAINED=false

RUN if [ "$SELF_CONTAINED" = "true" ]; then \
        dotnet publish boutiques/{{ boutiqueDir }}/{{ projectName }}.csproj \
            -c $BUILD_CONFIGURATION \
            -o /app/publish \
            -r linux-x64 \
            --self-contained true \
            /p:PublishSingleFile=true \
            /p:PublishTrimmed=true; \
    else \
        dotnet publish boutiques/{{ boutiqueDir }}/{{ projectName }}.csproj \
            -c $BUILD_CONFIGURATION \
            -o /app/publish \
            /p:UseAppHost=false; \
    fi
