# Kubernetes

Kubernetes manifests for deploying the application using [Kustomize](https://kustomize.io/).

## Structure

```
k8s/
├── base/
│   ├── deployment.yaml      # Base deployment with OTEL env vars
│   ├── service.yaml         # ClusterIP service exposing port 8080
│   └── kustomization.yaml
└── overlays/
    ├── dev/
    │   └── kustomization.yaml   # 1 replica, Development environment
    └── prod/
        └── kustomization.yaml   # 3 replicas, Production environment
```

## Usage

```bash
kubectl apply -k k8s/overlays/dev
kubectl apply -k k8s/overlays/prod
```

## Environment Variables

The base `deployment.yaml` includes the following variables. Fill in the values before deploying or inject them via a Secret/ConfigMap:

| Variable | Description |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Development` or `Production` |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | OTLP collector URL (leave empty to disable export) |
| `OTEL_EXPORTER_OTLP_PROTOCOL` | `http/protobuf` (default) or `grpc` |
| `OTEL_EXPORTER_OTLP_HEADERS` | Auth headers required by your observability platform |
| `OTEL_SERVICE_NAME` | Service name shown in traces and metrics |
| `OTEL_RESOURCE_ATTRIBUTES` | Additional metadata (e.g. `deployment.environment=production`) |

> For sensitive values like `OTEL_EXPORTER_OTLP_HEADERS` or `ConnectionStrings`, use a Kubernetes `Secret` and reference it with `valueFrom.secretKeyRef`.
