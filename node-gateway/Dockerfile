FROM node:22-alpine AS runtime
WORKDIR /app

COPY node-gateway/package.json ./package.json
COPY node-gateway/src ./src

ENV PORT=3001
EXPOSE 3001

CMD ["node", "src/server.js"]
