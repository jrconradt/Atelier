FROM {{ sdkImage }} AS build
WORKDIR /src

COPY src/Atelier/Atelier.slnx src/Atelier/
COPY src/Directory.Build.props src/
COPY Directory.Packages.props .

COPY src/Field/ src/Field/
COPY src/Atelier/Atelier.Benchmarks.Shared/ src/Atelier/Atelier.Benchmarks.Shared/
COPY src/Atelier/Atelier.Framework.Requisitions/ src/Atelier/Atelier.Framework.Requisitions/
COPY src/{{ subsystemName }}/ src/{{ subsystemName }}/

RUN dotnet publish {{ projectRelativePath }} \
    -c Release \
    -o /app/publish \
    --no-restore

FROM {{ runtimeImage }}
WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "{{ projectName }}.dll"]
CMD []
