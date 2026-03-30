const http = require('http');
const { config } = require('./config');
const { handleWebhookSimulation } = require('./routes/webhook-routes');

// Keep the gateway tiny: one health endpoint, one simulated webhook endpoint,
// and injectable dependencies so tests can run without a live backend.
function createServer({ backendBaseUrl = config.backendBaseUrl, fetchImpl = fetch } = {}) {
  return http.createServer((req, res) => {
    if (req.method === 'GET' && req.url === '/health') {
      res.writeHead(200, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify({ status: 'ok', service: 'node-gateway' }));
      return;
    }

    if (req.method === 'POST' && req.url === '/webhooks/simulate/execution-update') {
      let body = '';
      req.on('data', chunk => {
        body += chunk;
      });
      req.on('end', async () => {
        try {
          await handleWebhookSimulation({ req, res, body, backendBaseUrl, fetchImpl });
        } catch (error) {
          res.writeHead(500, { 'Content-Type': 'application/json' });
          res.end(JSON.stringify({
            type: 'server_error',
            message: error.message,
          }));
        }
      });
      return;
    }

    res.writeHead(404, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({
      type: 'not_found',
      message: 'Route not found.',
    }));
  });
}

if (require.main === module) {
  const server = createServer();
  server.listen(config.port, () => {
    console.log(`Node gateway listening on port ${config.port}`);
  });
}

module.exports = {
  createServer,
};
