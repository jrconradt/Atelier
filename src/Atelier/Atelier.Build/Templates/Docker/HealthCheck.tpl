    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:{{ port }}{{ path }}"]
      interval: {{ interval }}s
      timeout: {{ timeout }}s
      start_period: {{ startupDelay }}s
      retries: {{ retries }}
