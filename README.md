# dotnet-lgtmp-poc

.NET 10 minimal API that demonstrates full LGTMP (Loki, Grafana, Tempo, Mimir, Pyroscope) observability on the optiplex k3s cluster.

## What it does

Simple Items CRUD API (`/api/items`) backed by PostgreSQL + EF Core. Every request produces:

- **Traces** (Tempo) -- ASP.NET Core, HttpClient, EF Core auto-instrumented via OpenTelemetry
- **Metrics** (Mimir) -- request duration, .NET runtime (GC, threadpool), process metrics
- **Logs** (Loki) -- JSON console (scraped by Alloy from pod logs) + OTLP exporter (trace-correlated)
- **Profiles** (Pyroscope) -- native CLR profiler (CPU + allocations) via Pyroscope SDK + eBPF from Alloy

All telemetry ships via OTLP HTTP (port 4318) to Alloy, which fans out to the Grafana stack.

Every response includes `X-Trace-Id` and `X-Span-Id` headers so you can look up the exact trace in Tempo.

## Endpoints

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/items` | List all items |
| GET | `/api/items/{id}` | Get item by ID |
| POST | `/api/items` | Create item (`{"name": "...", "description": "..."}`) |
| PUT | `/api/items/{id}` | Update item |
| DELETE | `/api/items/{id}` | Delete item |
| GET | `/health` | Health check |

## Build & deploy

### 1. Generate NuGet dependency lock (first time / after changing packages)

Works on both macOS and Linux:

```bash
nix build .#app.fetch-deps
./result deps.json
```

This produces `deps.json` which pins all NuGet packages for reproducible Nix builds. Re-run if you change package references in the `.csproj`.

### 2. Fix the Pyroscope hash (first time only)

The first `nix build .#image` on optiplex will fail with a hash mismatch for the Pyroscope native profiler tarball. Copy the expected `sha256-...` from the error output into `default.nix`.

### 3. Build the OCI image (on optiplex / x86_64-linux)

```bash
nix build .#image
```

### 4. Push to local registry

```bash
nix run .#image.copyToRegistry
```

Pushes to `localhost:5000/dotnet-lgtmp-poc:latest`.

### 5. Deploy

The k8s deployment module lives in the monorepo. After pushing the image:

```bash
# in the monorepo
nix run .#nixidy -- build .#optiplex
nix run .#nixidy -- apply .#optiplex
```

### 7. Verify

```bash
# Pod running?
kubectl get pods -n dotnet-lgtmp-poc

# Health check
curl https://lgtmp-poc.brandonros.dev/health

# Create an item (note the X-Trace-Id in response headers)
curl -v -X POST https://lgtmp-poc.brandonros.dev/api/items \
  -H "Content-Type: application/json" \
  -d '{"name": "test", "description": "first item"}'

# List items
curl https://lgtmp-poc.brandonros.dev/api/items
```

### 8. Check Grafana

- **Traces**: Explore > Tempo > search for service `dotnet-lgtmp-poc` (or paste `X-Trace-Id`)
- **Metrics**: Explore > Mimir > `http_server_request_duration_seconds{service_name="dotnet-lgtmp-poc"}`
- **Logs**: Explore > Loki > `{namespace="dotnet-lgtmp-poc"}`
- **Profiles**: Explore > Pyroscope > app `dotnet-lgtmp-poc`

## Architecture

```
curl --> Traefik (lgtmp-poc.brandonros.dev)
           |
           v
      dotnet-lgtmp-poc (port 8080)
        |          |           |              |
        v          v           v              v
    PostgreSQL   Alloy:4318   Alloy:4318    Pyroscope:4040
    (EF Core)    (traces)     (metrics+logs) (profiles)
                   |           |
                   v           v
                 Tempo      Mimir + Loki
                   \          |    /
                    \         |   /
                     v        v  v
                       Grafana
```

## Notes

- Pyroscope native profiler officially supports .NET 6/7/8. It likely works on .NET 10 (CLR profiler API is stable) but is untested. If it causes startup crashes, set `CORECLR_ENABLE_PROFILING=0` in the k8s module -- eBPF profiling from Alloy will still work.
- The image uses `pullPolicy: Always` since the tag is `latest`. After pushing a new build, restart the pod: `kubectl rollout restart deployment -n dotnet-lgtmp-poc`
- PgBouncer transaction pooling is compatible with EF Core for standard CRUD.
