# Node Gateway

Lightweight Node.js integration gateway and webhook simulator for the Kalshi Integration Event Publisher.

## Structure

```text
src/
  clients/
  models/
  routes/
  services/
  config.js
  server.js

tests/
```

## Endpoints
- `GET /health`
- `POST /webhooks/simulate/execution-update`

## Purpose
- Simulate external Kalshi-style execution callbacks
- Validate inbound payload shape
- Forward valid execution updates to the .NET backend callback endpoint
- Keep the integration boundary clean and testable
- Provide a tiny, easy-to-explain seam for demos and interview walkthroughs

## Configuration

The gateway reads configuration from environment variables:

- `PORT` → HTTP port (default `3001`)
- `BACKEND_BASE_URL` → .NET backend base URL (default `http://localhost:5145`)

A checked-in template is available at:
- `.env.example`

Do not commit real `.env` files.

## Run
```bash
node src/server.js
```

Or export variables first:

```bash
export PORT=3001
export BACKEND_BASE_URL='http://localhost:5145'
node src/server.js
```

## Test
```bash
node --test
```
