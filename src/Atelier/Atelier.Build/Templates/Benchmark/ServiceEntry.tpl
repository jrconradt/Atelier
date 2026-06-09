  {{ serviceName }}:
    build:
      context: .
      dockerfile: boutiques/{{ dockerfileName }}
    image: {{ imageName }}:latest
    profiles: ["benchmarks"]
