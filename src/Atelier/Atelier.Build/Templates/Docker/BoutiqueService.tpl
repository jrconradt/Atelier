  {{ boutiqueName }}:
    build:
      context: {{ context }}
      dockerfile: boutiques/{{ shortName }}/Dockerfile
      args:
        BUILD_CONFIGURATION: Release
        SELF_CONTAINED: ${SELF_CONTAINED:-false}
    image: {{ imageName }}:{{ immutableTag }}
    container_name: {{ containerName }}
    labels:
      io.atelier.boutique: "{{ boutiqueLabel }}"
      io.atelier.image.immutable: "{{ imageName }}:{{ immutableTag }}"
      org.opencontainers.image.version: "{{ immutableTag }}"
      org.opencontainers.image.description: "{{ description }}"
    restart: unless-stopped
{{ sections }}
    read_only: true
    tmpfs:
      - /tmp
      - /var/atelier
    cap_drop:
      - ALL
    security_opt:
      - no-new-privileges:true
    networks:
{{ networks }}
