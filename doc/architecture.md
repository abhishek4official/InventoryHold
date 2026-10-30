# Architecture

How the Inventory Hold Microservice fits together. For setup and the reasoning behind each decision, see the [README](../README.md).

- [System overview](#system-overview)
- [Code layers](#code-layers)
- [Placing a hold](#placing-a-hold)
- [Releasing a hold](#releasing-a-hold)
- [Hold lifecycle](#hold-lifecycle)
- [Reading inventory](#reading-inventory)
- [Data model](#data-model)
- [Events](#events)
- [Request signing](#request-signing)

## System overview

Five containers start with `docker-compose up --build`. The dashed boxes are outside this repository.

```mermaid
flowchart LR
    browser["Browser<br/>React, MUI, TanStack Query"]
    checkout["Checkout system"]:::external
    consumers["Downstream consumers"]:::external

    subgraph compose["docker-compose"]
        nginx["frontend: nginx<br/>:3000<br/>Serves the app, signs API calls"]
        api["api: .NET 10<br/>:8080<br/>Hold rules, expiry sweeper"]
        mongo[("mongo<br/>Source of truth")]
        redis[("redis<br/>Cache and nonces")]
        rabbit["rabbitmq<br/>Hold events"]
    end

    browser -->|"HTTP, unsigned"| nginx
    nginx -->|"/api, signed"| api
    checkout -->|"HTTP, signed"| api
    api -->|"Atomic updates"| mongo
    api -->|"Cache-aside, fail-open"| redis
    api -->|"Publish with confirms"| rabbit
    rabbit -.-> consumers

    classDef external stroke-dasharray: 5 5
```

| Service | Host port | Reachable from |
|---|---|---|
| `frontend` | 3000 | Any machine |
| `api` | 8080 | Any machine |
| `mongo` | 27017 | This machine only |
| `redis` | 6379 | This machine only |
| `rabbitmq` | 5672, and 15672 for the management UI | This machine only |

## Code layers

Dependencies point inward. `HoldService` knows only interfaces, so the unit tests can replace every piece of infrastructure with a mock.

```mermaid
flowchart TB
    tests["InventoryHold.UnitTests<br/>NUnit, Moq"]
    webapi["InventoryHold.WebApi<br/>Controllers, HMAC middleware,<br/>error mapping, expiry sweeper"]
    infra["InventoryHold.Infrastructure<br/>MongoDB, Redis and RabbitMQ implementations"]

    subgraph domain["InventoryHold.Domain"]
        services["Services<br/>HoldService, ShipDateEstimator"]
        interfaces["Repositories and Abstractions<br/>IHoldRepository, IProductRepository,<br/>IInventoryUnitRepository, IInventoryCache,<br/>IEventPublisher, INonceStore"]
        security["Security<br/>HmacSignatureVerifier"]
    end

    contracts["InventoryHold.Contracts<br/>Requests, responses, enums, events"]

    webapi --> infra
    infra --> domain
    tests --> domain
    domain --> contracts
    services --> interfaces
    security --> interfaces
    infra -. "implements" .-> interfaces
```

## Placing a hold

`POST /api/holds`. The conditional update is what prevents overselling: the stock check and the deduction are one atomic operation on one document.

```mermaid
sequenceDiagram
    autonumber
    actor Client
    participant API as API and HoldService
    participant Mongo as MongoDB
    participant Redis
    participant Rabbit as RabbitMQ

    Client->>API: POST /api/holds
    API->>API: Validate the request
    Note over API: Invalid request: 400
    API->>Mongo: Load products for the requested SKUs
    Note over API: Unknown SKU: 404

    loop Each line, in sorted SKU order
        API->>Mongo: updateOne where available >= quantity<br/>$inc available down, held up
        alt Not enough stock
            Mongo-->>API: Nothing modified
            API->>Mongo: Return units and stock already reserved
            API-->>Client: 409 insufficient_stock
        else Reserved
            Mongo-->>API: Modified
            opt Product is tracked by serial number
                API->>Mongo: findOneAndUpdate one free unit per item
            end
        end
    end

    API->>Mongo: Insert the hold as Active
    API->>Redis: Delete cached inventory and hold
    API->>Rabbit: Publish HoldCreated
    Note over API,Rabbit: Redis or RabbitMQ failures are logged<br/>and do not fail the request
    API-->>Client: 201 with Location header
```

## Releasing a hold

`DELETE /api/holds/{holdId}`. A compare-and-set on the status is the gate. However many callers race, including the expiry sweeper, only one gets the document back, so stock can't be restored twice.

```mermaid
sequenceDiagram
    autonumber
    actor Client
    participant API as API and HoldService
    participant Mongo as MongoDB
    participant Redis
    participant Rabbit as RabbitMQ

    Client->>API: DELETE /api/holds/{holdId}
    API->>Mongo: Load the hold
    Note over API: Not found: 404<br/>Already released: 409<br/>Expired: 410

    API->>Mongo: findOneAndUpdate where status is Active<br/>set status to Released
    alt Lost the race to another caller
        Mongo-->>API: No document
        API-->>Client: 409 or 410, with no stock change
    else Won the race
        Mongo-->>API: The updated hold
        API->>Mongo: Free the hold's units
        API->>Mongo: $inc available up, held down, per line
        API->>Redis: Delete cached inventory and hold
        API->>Rabbit: Publish HoldReleased
        API-->>Client: 200 with the released hold
    end
```

## Hold lifecycle

A hold moves one way only, and every closed hold is kept as a record.

```mermaid
stateDiagram-v2
    [*] --> Active: POST /api/holds
    Active --> Released: DELETE before expiry
    Active --> Expired: Past expiry, found by sweeper or a request
    Released --> [*]
    Expired --> [*]

    note right of Active
        Stock is moved from available to held.
        Serial-tracked units are assigned to the hold.
    end note

    note right of Expired
        Found by the sweeper every 30 seconds by default,
        or sooner by GET, DELETE or the status list.
        Stock is restored and HoldExpired is published.
    end note
```

## Reading inventory

`GET /api/inventory` uses cache-aside. A cached value can be slightly out of date for display, but stock decisions never read the cache, so it can't cause overselling. If Redis is down, the failure is logged and the request carries on using MongoDB.

```mermaid
flowchart TD
    start(["GET /api/inventory"]) --> read{"In Redis?"}
    read -->|"Yes"| hit["Return the cached response"]
    read -->|"No"| load["Load products and units from MongoDB"]
    read -->|"Redis is down"| load
    load --> build["Build the response, with low stock<br/>flags and approximate ship dates"]
    build --> store["Cache for 30 seconds"]
    store --> done["Return the response"]
    mutation["Any hold created, released or expired"] -.->|"Deletes the key"| read
```

| Read | Cached for | Cleared by |
|---|---|---|
| `GET /api/inventory` | 30 seconds | Any stock change |
| `GET /api/holds/{holdId}` | 60 seconds, or the time left on the hold if shorter | Any change to that hold |
| `GET /api/holds?status=` | Not cached | Not applicable |

## Data model

Three MongoDB collections. A hold's lines are embedded because they belong to the hold and are always read with it.

```mermaid
erDiagram
    PRODUCTS ||--o{ INVENTORY_UNITS : "has, if serial-tracked"
    HOLDS ||--|{ HOLD_LINES : embeds
    PRODUCTS ||--o{ HOLD_LINES : "referenced by SKU"
    HOLDS |o--o{ INVENTORY_UNITS : "holds"

    PRODUCTS {
        string _id PK "SKU"
        string Name
        string PartNumber
        string Manufacturer
        string Category
        string WarehouseLocation
        string UnitOfMeasure
        int Available
        int Held
        int ReorderPoint
        int HandlingDays
        bool IsSerialized
        date LastUpdatedAt
    }

    INVENTORY_UNITS {
        string _id PK "Serial number"
        string Sku FK
        string HoldId FK "Null when free"
        string LotNumber
        string Condition
        date ReceivedAt
        date LastUpdatedAt
    }

    HOLDS {
        string _id PK "GUID"
        string CustomerId
        string Status "Active, Released or Expired"
        date CreatedAt
        date ExpiresAt
        date ClosedAt "Null while Active"
    }

    HOLD_LINES {
        string Sku
        string ProductName "Copied when held"
        string PartNumber "Copied when held"
        int Quantity
        string ApproxShipDate "Quoted when held"
        string_array SerialNumbers
    }
```

## Events

A durable topic exchange lets each consumer bind to only what it needs. The `inventory-hold.events` queue is included so that events can be inspected in the management UI; a real consumer would declare its own queue.

```mermaid
flowchart LR
    api["API"] -->|"hold.created"| exchange
    api -->|"hold.released"| exchange
    api -->|"hold.expired"| exchange
    exchange{{"inventory.holds<br/>Topic exchange, durable"}} -->|"hold.#"| queue[["inventory-hold.events<br/>Durable queue"]]
    exchange -.->|"hold.released"| future["A consumer's own queue"]:::external

    classDef external stroke-dasharray: 5 5
```

Each event carries everything a consumer needs: `eventId`, `eventType`, `occurredAt`, `holdId`, `customerId`, `items` (SKU, product name, part number, quantity, approximate ship date and serial numbers), `expiresAt` and `schemaVersion`.

## Request signing

Optional HMAC-SHA256 signing, off under `docker-compose` unless `HMAC_ENABLED=true`, and on by default everywhere else. A browser can't keep a secret, so nginx signs on its behalf. See [Security](../README.md#security) for what this does and doesn't protect.

```mermaid
sequenceDiagram
    autonumber
    actor Browser
    participant Nginx as nginx
    actor Checkout as Checkout system
    participant MW as API HMAC middleware
    participant Redis
    participant Controller as Controllers

    alt From the web app
        Browser->>Nginx: Request with no signature
        Nginx->>Nginx: Sign with HMAC_WEB_SECRET,<br/>replacing any signature headers sent
        Nginx->>MW: Request with signature headers
    else From another system
        Checkout->>MW: Request signed with HMAC_CHECKOUT_SECRET
    end

    MW->>MW: Are all four headers present?
    Note over MW: No: 401 missing_signature
    MW->>MW: Does the signature match, compared in constant time?
    Note over MW: No: 401 invalid_signature
    MW->>MW: Is the timestamp within 5 minutes?
    Note over MW: No: 401 stale_timestamp
    MW->>Redis: SET nonce only if new, kept for 10 minutes
    Note over MW,Redis: Already used: 401 replayed_request<br/>Redis down: continue and log a warning
    MW->>Controller: Genuine request
```

The signature covers these values, one per line:

```
METHOD
PATH_AND_QUERY
TIMESTAMP
NONCE
SHA256_HEX_OF_BODY
```
