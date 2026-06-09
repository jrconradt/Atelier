  {{ zoneName }}-policy.yaml: |
    apiVersion: security.istio.io/v1beta1
    kind: PeerAuthentication
    metadata:
      name: {{ zoneName }}-auth
      namespace: default
    spec:
      selector:
        matchLabels:
          zone: {{ zoneName }}
      mtls:
        mode: STRICT
