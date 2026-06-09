apiVersion: v1
kind: ConfigMap
metadata:
  name: service-mesh-policies
  namespace: default
data:
{{ zones }}
