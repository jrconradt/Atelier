FROM {{ sdkImage }} AS build
WORKDIR /src

COPY src/Atelier/Atelier.slnx src/Atelier/
COPY src/Directory.Build.props src/
COPY Directory.Packages.props .

{{ dependencyCopies }}

COPY src/{{ subsystemDir }}/ src/{{ subsystemDir }}/

RUN dotnet restore src/{{ subsystemDir }}/{{ solutionFileName }}

RUN dotnet build src/{{ subsystemDir }}/{{ solutionFileName }} \
    -c Debug \
    --no-restore

FROM {{ sdkImage }}
WORKDIR /app

COPY --from=build /src/src/{{ subsystemDir }}/ /app/

RUN mkdir -p /app/test-results

ENTRYPOINT ["dotnet", "test", "{{ solutionFileName }}", \
    "--no-build", \
    "-c", "Debug", \
    "--logger", "trx;LogFileName=/app/test-results/{{ subsystemName }}-tests.trx", \
    "--logger", "console;verbosity=normal"]
CMD []
