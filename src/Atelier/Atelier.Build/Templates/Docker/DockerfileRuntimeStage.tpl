FROM {{ baseImage }} AS runtime
WORKDIR /app

{{ installDeps }}

{{ userSetup }}

COPY --from=build --chown={{ uid }}:{{ gid }} /app/publish .
{{ certSection }}
RUN mkdir -p {{ logDir }} && chown -R {{ uid }}:{{ gid }} {{ logDir }} /app

USER {{ username }}

{{ envSection }}
{{ exposeSection }}
HEALTHCHECK --interval={{ healthInterval }}s --timeout={{ healthTimeout }}s --start-period={{ startupDelay }}s --retries={{ healthRetries }} \
    CMD ["curl", "-f", "http://localhost:{{ healthPort }}{{ healthPath }}"]

ENTRYPOINT ["dotnet", "{{ assemblyName }}.dll"]
