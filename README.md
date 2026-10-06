# Webhook Notification Service

A C# / .NET 9 service that delivers event notifications (webhooks) to other systems reliably. You publish an event such as `payment.captured`, and the service sends a **signed** HTTP POST to every registered URL. If a receiver is down or returns an error, it **retries with delays** and records every attempt. This is how payment platforms such as Stripe notify merchants.

## Features

- Register receiver URLs. Each gets a secret, shown once.
- Publish events. The event and its deliveries are stored in **one database transaction**, then the API returns `202 Accepted`.
- A background worker (`BackgroundService`) sends deliveries, retries failures and marks them `Failed` after the last attempt.
- Every message is signed with **HMAC-SHA256** so the receiver can verify it came from this service.
- Delivery history with status, attempt count, last HTTP status and last error.
- Health check, OpenAPI, and a Scalar UI in Development.
- 10 automated tests against a real PostgreSQL (Testcontainers).

## How it works

~~~mermaid
flowchart LR
    A[Your app] -->|POST /events| B[Webhook API]
    B -->|one transaction| C[(PostgreSQL: events and deliveries)]
    D[Delivery worker] -->|reads due deliveries| C
    D -->|signed POST| E[Receiver server]
    E -->|2xx = delivered, otherwise retry later| D
~~~

## Run it

You need Docker Desktop.

~~~bash
docker compose up --build
~~~

Open http://localhost:5080/scalar/v1 and try, in order:

1. `POST /endpoints` with `{"url": "http://localhost:8080/test-receiver/fail-first/2"}`. This built-in test receiver (Development only) fails the first 2 attempts, then succeeds.
2. `POST /events` with `{"type": "payment.captured", "payload": {"paymentId": "pay_1", "amount": 2500}}`
3. `GET /deliveries` a few times. The delivery goes from `Pending` to `Succeeded` after 3 attempts.
4. `GET /test-receiver/received` shows every attempt with its signature.

The URL uses port 8080 because that is where the app listens inside the Docker container.

## API

| Method | Path | What it does |
|---|---|---|
| POST | `/endpoints` | Register a receiver URL. Returns its secret once. |
| GET | `/endpoints` | List endpoints |
| DELETE | `/endpoints/{id}` | Switch an endpoint off |
| POST | `/events` | Publish an event to all active endpoints (202) |
| GET | `/deliveries?status=&limit=` | List deliveries. Status is Pending, Succeeded or Failed. |
| GET | `/deliveries/{id}` | One delivery |
| GET | `/health` | Health check |

## What the receiver gets

Headers: `X-Webhook-Id`, `X-Webhook-Event`, `X-Webhook-Timestamp`, `X-Webhook-Signature`

Body:

~~~json
{ "id": "event-guid", "type": "payment.captured", "createdAt": "2026-10-06T16:43:10Z", "data": { "paymentId": "pay_1", "amount": 2500 } }
~~~

The receiver verifies the signature like this:

~~~csharp
var expected = "sha256=" + Convert.ToHexString(
    HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret),
                        Encoding.UTF8.GetBytes($"{timestamp}.{rawBody}"))).ToLowerInvariant();
var valid = CryptographicOperations.FixedTimeEquals(
    Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(signatureHeader));
~~~

## Design decisions

- **At-least-once delivery.** A receiver may see the same message twice, for example if it processed the message but its reply was lost. Receivers should use `X-Webhook-Id` to ignore duplicates.
- **Retries are configurable** in `appsettings.json` (`Webhooks:RetryDelaysSeconds`). The sample uses 5, 10, 20 and 40 seconds so retries are easy to watch. Production-style values would be 10 s, 1 min, 5 min and 30 min. Max attempts = number of delays + 1.
- **Event and deliveries are saved in one transaction**, so an event is never accepted without its deliveries.
- **Failed deliveries are kept**, not deleted, so a person can see what went wrong.
- Redirects are not followed, and each request has a timeout.

## Tests

~~~bash
dotnet test
~~~

Covers the signature (against a known value), delivery with verification of the signature, fail-twice-then-succeed, giving up after 5 attempts, no active endpoints, disabled endpoints, and input validation. A fake receiver stands in for the merchant's server.

## Limitations and next steps

- Run **one** worker instance. To scale out, claim rows with `SELECT ... FOR UPDATE SKIP LOCKED`.
- Block private and internal IP addresses as receiver URLs (SSRF protection) before exposing this publicly.
- Encrypt secrets at rest and support secret rotation.
- Connect it to the [payment processing API](https://github.com/Ankey02/payment-processing-rest-api) so captures and refunds publish events.
