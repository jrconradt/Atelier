  {{ serviceName }}:
    build:
      context: .
      dockerfile: boutiques/{{ dockerfileName }}
    image: {{ imageName }}:latest
    profiles: ["tests"]
    volumes:
      - ./test-results:/app/test-results
