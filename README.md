# InCleanHome API Gateway
> Single entry point for the InCleanHome microservices platform.

Built on **YARP** (Yet Another Reverse Proxy) running on **.NET 9**. This service:

- Routes incoming HTTP requests to the right microservice based on URL path.
- Validates JWT tokens once at the edge so internal services can trust the headers.
- Applies CORS so the Vue frontend can reach the system from a different origin.
- Reads its configuration (routes, clusters, JWT settings) from **Consul KV** at startup.
- Optionally registers itself in Consul for service discovery.

This service is part of the larger [InCleanHome platform](https://github.com/UPC-pre-SI657-2610-7943-Grupo3/incleanhome-platform).

## Architecture in one paragraph
The gateway boots, asks Consul for `config/api-gateway` (a JSON blob), feeds that into
ASP.NET's `IConfiguration`, and YARP picks up the routing tables from there. JWT settings
come from the same blob; the signing key is read from an environment variable (it is a
secret and does not belong in Consul). If Consul is unreachable at startup, the gateway
falls back to the local `appsettings.json` so it can still come up. After it's running,
an opt-in hosted service registers it in Consul service catalog with a health check.

## Folder layout

```
incleanhome-api-gateway/
├── InCleanHome.ApiGateway.sln
├── Dockerfile
└── src/InCleanHome.ApiGateway/
    ├── InCleanHome.ApiGateway.csproj
    ├── Program.cs
    ├── appsettings.json                       # fallback config only
    ├── appsettings.Development.json
    ├── Configuration/
    │   └── ConsulConfigurationLoader.cs       # GETs config/api-gateway from KV
    └── Discovery/
        ├── ConsulServiceRegistration.cs       # HTTP client for register/deregister
        └── ConsulRegistrationHostedService.cs # lifecycle hook
```

The gateway will be available at <http://localhost:8080>.

## Routes currently configured
| External path (what Vue calls) | Routed to | Notes |
|---|---|---|
| `/api/v1/auth/**` | `iam-service:5001` | Login, register, refresh |
| `/api/v1/iam/**` | `iam-service:5001` | Admin operations (prefix is stripped) |
| `/api/v1/profiles/**` | `profile-service:5002` | Profile CRUD |

When new microservices are added, their routes go into `api-gateway.json`
in the platform repo, not into this code.

## Endpoints owned by the gateway itself
| Path | Purpose |
|---|---|
| `/` | Quick status (service name, config source, version) |
| `/health` | Health check for Docker and Consul probes |


## How it cooperates with other repos
| Repo | Relationship |
|---|---|
| `incleanhome-platform` | Owns the docker-compose, env vars, Consul JSON for this gateway |
| `incleanhome-iam-service` | Target of `/api/v1/iam` and `/api/v1/auth` routes |
| `incleanhome-profile-service` | Target of `/api/v1/profiles` routes |

## Implementation notes
- **YARP version**: 2.2.0. Reads routes via the standard `LoadFromConfig` extension
  pointing at the `ReverseProxy` section.
- **JWT validation**: standard `Microsoft.AspNetCore.Authentication.JwtBearer` with
  HS256 (symmetric). For Auth0-issued tokens (RS256 with JWKS), this can be extended
  in a follow-up.
- **Discovery**: thin custom HTTP client against Consul agent API. We chose this
  over `Microsoft.Extensions.ServiceDiscovery` for now because we need explicit
  control over service registration lifecycle and tags. Future iterations may
  switch to the Microsoft package for load balancing across multiple instances.
- **Logging**: Serilog to console with request logging enabled.

## License
For academic use - InCleanHome team.
