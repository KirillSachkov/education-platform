# syntax=docker/dockerfile:1.7
FROM node:22-alpine AS builder

WORKDIR /app

COPY package.json package-lock.json* ./
RUN --mount=type=cache,id=npm,target=/root/.npm \
    npm ci

COPY . .

ARG NEXT_PUBLIC_API_URL=http://localhost/api
ARG NEXT_PUBLIC_AUTH_ORIGIN=http://localhost
ARG NEXT_PUBLIC_APP_URL=http://localhost
ARG NEXT_PUBLIC_PRIMARY_AUTHOR_SLUG=sachkov
ARG NEXT_PUBLIC_CONSULTATION_LINK=https://t.me/sachkova_mng
ARG NEXT_PUBLIC_YANDEX_METRIKA_ID=
ARG NEXT_PUBLIC_VAPID_PUBLIC_KEY=
ENV NEXT_PUBLIC_API_URL=$NEXT_PUBLIC_API_URL \
    NEXT_PUBLIC_AUTH_ORIGIN=$NEXT_PUBLIC_AUTH_ORIGIN \
    NEXT_PUBLIC_APP_URL=$NEXT_PUBLIC_APP_URL \
    NEXT_PUBLIC_PRIMARY_AUTHOR_SLUG=$NEXT_PUBLIC_PRIMARY_AUTHOR_SLUG \
    NEXT_PUBLIC_CONSULTATION_LINK=$NEXT_PUBLIC_CONSULTATION_LINK \
    NEXT_PUBLIC_YANDEX_METRIKA_ID=$NEXT_PUBLIC_YANDEX_METRIKA_ID \
    NEXT_PUBLIC_VAPID_PUBLIC_KEY=$NEXT_PUBLIC_VAPID_PUBLIC_KEY

RUN mkdir -p public && npm run build && node scripts/collect-dependency-notices.mjs

FROM node:22-alpine AS runner

WORKDIR /app

ARG BUILD_REVISION=unknown
ARG BUILD_VERSION=dev
LABEL org.opencontainers.image.source="https://github.com/KirillSachkov/education-platform" \
      org.opencontainers.image.revision="${BUILD_REVISION}" \
      org.opencontainers.image.version="${BUILD_VERSION}" \
      org.opencontainers.image.title="frontend"

ENV NODE_ENV=production \
    PORT=3000 \
    HOSTNAME=0.0.0.0

RUN --mount=type=secret,id=image_notices,target=/tmp/image-notices.tar \
    if [ -f /tmp/image-notices.tar ]; then \
      mkdir -p /app/licenses && tar -xf /tmp/image-notices.tar -C /app/licenses; \
    fi
COPY --link --from=builder /app/dependency-notices/ /app/licenses/
COPY --link --from=builder /app/public ./public
COPY --link --from=builder /app/.next/standalone ./
COPY --link --from=builder /app/.next/static ./.next/static

# Owner supplies legal Markdown/PDF through read-only runtime mounts.
RUN mkdir -p .next/cache content/legal public/legal-docs && chown -R node:node .next

USER node

EXPOSE 3000

STOPSIGNAL SIGTERM

CMD ["node", "server.js"]
