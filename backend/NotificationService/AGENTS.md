# NotificationService

Owns the notification inbox, subscriptions, per-type preferences, delivery log, Web Push
subscriptions, campaigns, digest, and the realtime SSE stream in PostgreSQL schema
`notifications` (port 8006).

## Context routing

Inspect the affected event handler under `Core/Notifications/Handlers`, its template, the
dispatcher and channel, then the use case under `Core/Features` and its tests. Cross-service
backend rules come from [`../AGENTS.md`](../AGENTS.md).

## Boundary

Domain events become notifications only through a handler plus `NotificationDispatcher`; InApp,
Email and Web Push deliver in-process, while Telegram delivery is delegated via
`notification.created` to TelegramBotService, which reports back with `TelegramDeliveryRecorded`.
SSE connections live in memory: keep one replica unless `Sse:RedisFanoutEnabled` is on with Redis.

## Entrypoint and verification

Entrypoint: `src/NotificationService.Web/Program.cs`.
Run `dotnet test backend/NotificationService/tests/NotificationService.UnitTests` and
`dotnet test backend/NotificationService/tests/NotificationService.IntegrationTests`.
