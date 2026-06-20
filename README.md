# InCleanHome API Gateway

> Single entry point for the InCleanHome microservices, built on YARP.

This service is the only one exposed to the public internet (or in local dev, the
only one the frontend talks to directly). It:

- Validates JWT tokens at the edge (defense at the perimeter; each microservice
  also validates again — defense in depth).
- Routes incoming HTTP requests to the right microservice based on path prefix.
- Reads its routing table from **Consul KV** at startup, with the local
  `appsettings.json` as fallback.
- Registers itself in Consul Service Discovery so other tools can find it.

## Routes

All routes are versioned under `/api/v1/`. The gateway is at `http://localhost:8080`
in local dev.

| Path prefix | Forwards to | Service |
|---|---|---|
| `/api/v1/auth/**` | iam-service:5001 | IAM (login, register, /me, Auth0) |
| `/api/v1/admin/**` | iam-service:5001 | IAM admin endpoints |
| `/api/v1/iam/**` | iam-service:5001 (prefix stripped) | IAM (alternative path) |
| `/api/v1/profiles/**` | profile-service:5002 | Profile (me, clients, workers, photos) |
| `/api/v1/clients/**` | profile-service:5002 | Client public profile lookups |
| `/api/v1/workers/**` | profile-service:5002 | Worker public profile lookups |
| `/api/v1/bookings/**` | booking-service:5003 | Booking requests |
| `/api/v1/service-payments/**` | payment-service:5004 | Payment for a booking |
| `/api/v1/payment-methods/**` | payment-service:5004 | Saved payment methods |
| `/api/v1/mercadopago/**` | payment-service:5004 | MercadoPago webhooks + checkout |
| `/api/v1/messages/**` | communication-service:5005 | Twilio chat messages |
| `/api/v1/conversations/**` | communication-service:5005 | Chat conversations |
| `/api/v1/twilio/**` | communication-service:5005 | Twilio access tokens / webhooks |
| `/api/v1/notifications/**` | communication-service:5005 | In-app + FCM push notifications |
| `/api/v1/reviews/**` | reviews-service:5006 | Service reviews |
| `/api/v1/reports/**` | reviews-service:5006 | User reports |
| `/api/v1/suspension-appeals/**` | reviews-service:5006 | Suspension appeals |
| `/api/v1/availability/**` | search-service:5007 | Worker availability slots |
| `/api/v1/catalog/**` | search-service:5007 | Worker discovery / catalog |

## Run it (with the rest of the platform)

```bash
cd ../incleanhome-platform
./scripts/start.sh
```

The gateway listens on port `8080`.

## Environment variables

| Variable | Required | Purpose |
|---|---|---|
| `JWT_SIGNING_KEY` | **YES** | HMAC-SHA256 key, same as IAM Service issues with |
| `CONSUL_HTTP_ADDR` | no | Default: `http://consul:8500` |
| `CONSUL_DISCOVERY_ENABLED` | no | Default: `true` |
| `SERVICE_NAME` | no | Default: `api-gateway` |

## Folder layout

```
src/InCleanHome.ApiGateway/
├── Program.cs                       # composition root
├── appsettings.json                 # FALLBACK config (real one is in Consul)
├── Configuration/
│   └── ConsulConfigurationLoader.cs # downloads config/api-gateway from KV at startup
└── Discovery/
    ├── ConsulServiceRegistration.cs # registers/deregisters this gateway
    └── ConsulRegistrationHostedService.cs
```

There's NO custom JWT middleware here — we use the standard `JwtBearer` from
ASP.NET Core, configured in `Program.cs`. Each microservice has its own JWT
validation too.
