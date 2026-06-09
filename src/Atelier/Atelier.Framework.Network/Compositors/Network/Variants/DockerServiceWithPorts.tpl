  {{ name }}:
    image: {{ image }}:latest
    networks:
      - {{ zone }}
    ports:
{{ ports }}
