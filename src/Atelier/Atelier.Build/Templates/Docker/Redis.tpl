  redis:
    image: {{ image }}
    container_name: atelier-redis
    command: ["redis-server", "--requirepass", "${REDIS_PASSWORD:?REDIS_PASSWORD must be set}"]
    ports:
      - "127.0.0.1:{{ port }}:{{ port }}"
    volumes:
      - {{ volume }}:/data
    healthcheck:
      test: ["CMD", "redis-cli", "-a", "${REDIS_PASSWORD}", "ping"]
      interval: 5s
      timeout: 3s
      retries: 5
    networks:
{{ networks }}
