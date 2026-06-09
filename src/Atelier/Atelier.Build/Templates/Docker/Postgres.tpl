  postgres:
    image: {{ image }}
    container_name: atelier-postgres
    environment:
      - POSTGRES_USER={{ user }}
      - POSTGRES_PASSWORD=${POSTGRES_PASSWORD:?POSTGRES_PASSWORD must be set}
      - POSTGRES_DB={{ database }}
    ports:
      - "127.0.0.1:{{ port }}:{{ port }}"
    volumes:
      - {{ volume }}:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U {{ user }}"]
      interval: 5s
      timeout: 3s
      retries: 5
    networks:
      - {{ network }}
