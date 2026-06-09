apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata:
  name: {{ name }}
  labels:
    io.atelier.zone: "{{ zone }}"
  annotations:
    io.atelier.requires-mtls: "{{ mtls }}"
spec:
  podSelector:
    matchLabels:
      io.atelier.zone: "{{ zone }}"
  policyTypes:
    - Ingress
    - Egress
  ingress:
{{ ingress }}
  egress:
{{ egress }}
