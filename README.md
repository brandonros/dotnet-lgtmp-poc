# dotnet-lgtmp-poc

.NET 10 solution demonstrating full LGTMP (Loki, Grafana, Tempo, Mimir, Pyroscope) observability on Kubernetes. Two entry points — a web API and a console ETL app — sharing a common core library.

## Project structure

```
src/
├── DotnetLgtmpPoc.Core/           # Shared: DbContext, models, OTEL setup
│   ├── Data/AppDbContext.cs
│   ├── Models/Item.cs
│   └── Telemetry/OtelSetup.cs     # AddOtelDefaults() extension method
├── DotnetLgtmpPoc.Web/            # Entry point: ASP.NET Core minimal API
│   ├── Program.cs
│   └── Endpoints/ItemEndpoints.cs
├── DotnetLgtmpPoc.Console/        # Entry point: batch ETL console app
│   ├── Program.cs
│   └── Services/ItemImportService.cs
├── Directory.Build.props          # Shared build properties (TFM, nullable, etc.)
├── Directory.Packages.props       # Central package version management
└── default.nix                    # Nix build: two apps, two OCI images
```

## What it does

### Web (`DotnetLgtmpPoc.Web`)

Items CRUD API (`/api/items`) backed by PostgreSQL + EF Core. Every request produces:

- **Traces** (Tempo) — ASP.NET Core, HttpClient, EF Core auto-instrumented via OpenTelemetry
- **Metrics** (Mimir) — request duration, .NET runtime (GC, threadpool), process metrics
- **Logs** (Loki) — JSON console + OTLP exporter (trace-correlated)
- **Profiles** (Pyroscope) — native CLR profiler (CPU + allocations)

Every response includes `X-Trace-Id` and `X-Span-Id` headers for trace lookup in Tempo.

### Console (`DotnetLgtmpPoc.Console`)

Batch ETL job that imports items from a CSV file. Designed to run as an Argo CronWorkflow.

```
dotnet DotnetLgtmpPoc.Console.dll <filename>
```

Custom `ActivitySource` instrumentation produces spans for each ETL phase:

```
RunAsync
├── Extract      (CSV read)
├── Transform    (filter + map)
└── Load         (bulk insert)
    └── EF Core  (auto-instrumented)
```

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
nix build .#web-app.fetch-deps
./result deps.json
```

This produces `deps.json` which pins all NuGet packages for reproducible Nix builds. Re-run if you change package references.

### 2. Fix the Pyroscope hash (first time only)

The first image build will fail with a hash mismatch for the Pyroscope native profiler tarball. Copy the expected `sha256-...` from the error output into `default.nix`.

### 3. Build the OCI images (x86_64-linux)

```bash
nix build .#web-image       # web API image
nix build .#console-image   # console ETL image
```

### 4. Push to local registry

```bash
nix run .#web-image.copyToRegistry       # -> localhost:5000/dotnet-lgtmp-poc:latest
nix run .#console-image.copyToRegistry   # -> localhost:5000/dotnet-lgtmp-console:latest
```

### 5. Verify

```bash
# Pod running?
kubectl get pods -n dotnet-lgtmp-poc

# Health check
curl http://localhost:8080/health

# Create an item (note the X-Trace-Id in response headers)
curl -v -X POST http://localhost:8080/api/items \
  -H "Content-Type: application/json" \
  -d '{"name": "test", "description": "first item"}'

# List items
curl http://localhost:8080/api/items
```

### 6. Check Grafana

- **Traces**: Explore > Tempo > search for service `dotnet-lgtmp-poc` (or paste `X-Trace-Id`)
- **Metrics**: Explore > Mimir > `http_server_request_duration_seconds{service_name="dotnet-lgtmp-poc"}`
- **Logs**: Explore > Loki > `{namespace="dotnet-lgtmp-poc"}`
- **Profiles**: Explore > Pyroscope > app `dotnet-lgtmp-poc`

## Architecture

```
request --> ingress
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

- Pyroscope native profiler officially supports .NET 6/7/8. It likely works on .NET 10 (CLR profiler API is stable) but is untested. If it causes startup crashes, set `CORECLR_ENABLE_PROFILING=0` — eBPF profiling from Alloy will still work.
- The image uses `pullPolicy: Always` since the tag is `latest`. After pushing a new build, restart the pod: `kubectl rollout restart deployment -n dotnet-lgtmp-poc`
- PgBouncer transaction pooling is compatible with EF Core for standard CRUD.
