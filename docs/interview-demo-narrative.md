# Interview and demo narrative

This guide makes the Kalshi Integration Sandbox easier to present in interviews, portfolio reviews, or architecture walkthroughs.

## Short positioning statement

This project is a **production-minded integration sandbox** that demonstrates how I design and implement backend systems that sit between external inputs, business validation, persistence, outbound dependencies, and operator-facing workflows.

It is intentionally built to show:
- customer/integration boundary design
- operational workflow thinking
- reliability and traceability concerns
- clean architecture and SOLID discipline
- practical Microsoft-stack choices

## How to describe the project in 30 seconds

> Kalshi Integration Sandbox is a .NET 8 and Node.js portfolio project that models a realistic integration service. A client submits trade intents into an ASP.NET Core API, the app validates and persists them, creates and tracks orders, receives execution updates through a Node gateway that simulates an external integration layer, and exposes operational visibility through dashboard and health endpoints. I used it to demonstrate clean architecture, test coverage, reliability patterns, Azure-oriented deployment thinking, and production-style service boundaries.

## How it connects to customer integrations work

This project maps well to customer/integration engineering because it demonstrates:

- **external boundary management**
  - the Node gateway acts like a customer-facing or partner-facing integration edge
  - payload validation and structured failure handling happen at the boundary

- **service-to-service coordination**
  - the gateway forwards validated updates into the .NET backend
  - correlation and retry-safe behavior matter across that seam

- **operational accountability**
  - order status, event history, and positions are queryable
  - failures and dependency state are visible instead of being hidden in opaque app behavior

- **evolution-friendly architecture**
  - infrastructure concerns are kept out of the domain model
  - the system is easier to extend without rewriting core rules

## How it connects to operational workflows

The project is not just CRUD. It models a workflow with state, traceability, and operational visibility.

Good talking points:
- trade intents go through validation and risk checks before becoming orders
- orders have an explicit lifecycle and transition rules
- execution updates are ingested from an integration boundary rather than being assumed to happen internally
- order history is append-only so state changes can be audited
- health and dependency endpoints support operator confidence and troubleshooting

## Reliability and quality talking points

This repo includes several production-minded signals that are useful to call out:

- JWT authentication and policy-based authorization
- strongly typed config with startup validation
- EF Core migrations instead of ad hoc schema bootstrapping
- SQL Server / Azure SQL support in addition to SQLite
- request timing and dependency logging
- idempotency and retry-safe processing behavior
- `IHttpClientFactory` + resilience configuration for outbound HTTP
- unit, integration, and acceptance test layers
- Azure DevOps CI with format/build/test/coverage validation

## Microsoft stack choices to highlight

This repo is intentionally easy to explain in Microsoft-stack terms:

- **ASP.NET Core / .NET 8** for the main service implementation
- **EF Core** for persistence and migration workflow
- **SQL Server / Azure SQL support** for a credible Azure deployment story
- **Azure DevOps pipeline** for CI quality gates
- **Azure Container Apps deployment path** for practical cloud hosting
- **Azure-ready environment/configuration guidance** for secrets and deployment setup

That helps position the repo as more than a toy app — it reflects stack choices that can translate into enterprise or Azure-hosted work.

## Suggested live demo flow

Use this order for a concise but credible walkthrough.

### 1) Start with the architecture
Show the README diagram/structure and explain:
- .NET API = core service boundary
- Node gateway = simulated external integration edge
- persistence + event history + positions = operational state

### 2) Show the API surface
Call out:
- trade-intent intake
- order creation/query endpoints
- integration update endpoint
- health/readiness endpoints

### 3) Show one happy path
Narrate this flow:
1. submit a trade intent
2. create an order
3. send an execution update through the Node gateway
4. query the order and/or dashboard view to show the updated state

### 4) Show one reliability path
Pick one:
- invalid request rejected with problem details
- duplicate/replayed execution update handled safely
- protected endpoint requiring JWT token
- readiness/dependency endpoint showing operational visibility

### 5) Close with cloud/deployment credibility
Show that the repo includes:
- Dockerfiles
- docker-compose
- Azure deployment guide
- Azure IaC/deployment path work in progress / planned

## Suggested demo script

> The main thing I wanted to show here was not just a few REST endpoints, but the engineering around an integration workflow. A client submits a trade intent, the app validates it, applies risk rules, persists operational state, and turns it into an order. Then a separate Node gateway simulates the external integration edge and forwards execution updates back into the .NET backend. That lets me show clean boundaries, retry/idempotency thinking, append-only event history, operational visibility, and Azure-ready deployment patterns in one repo.

## What to emphasize if asked “why not just build one service?”

Because the split is the point.

The Node gateway exists to demonstrate:
- boundary separation between internal application logic and external integration concerns
- payload validation and forwarding responsibilities at the edge
- dependency visibility and failure handling across service boundaries
- a more realistic story than pretending every event originates inside a single process

## Future enhancements to mention

These are useful as “next step” items during interviews:

- OpenTelemetry tracing and metrics with OTLP / Azure Monitor export
- stronger Azure DevOps PR quality gates and dependency scanning
- Azure infrastructure as code for Container Apps, ACR, Key Vault, and SQL resources
- further SOLID refinement as services grow
- richer dashboard filtering and operational drill-down views
- more advanced event publishing / broker-backed integration scenarios

## Recommended interviewer Q&A pivots

### If asked about architecture
Focus on:
- dependency direction
- domain isolation
- why the Node gateway exists
- how infrastructure is kept replaceable

### If asked about reliability
Focus on:
- idempotency
- append-only event history
- retries and outbound HTTP patterns
- startup config validation
- health/readiness endpoints

### If asked about scalability or maintainability
Focus on:
- clean boundaries
- provider abstraction where it matters
- test layering
- operational transparency
- future-ready Azure deployment model

## One-line takeaway

This project shows that I can build a service that is not only functional, but also **integration-aware, operationally credible, testable, and ready to evolve in a Microsoft/Azure environment**.
