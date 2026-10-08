# TelegramBotService

Owns the Telegram bot (TelegramBotFlow screens and commands), platform account links, plan chat
bindings with join-request approval, welcome messages, and Telegram notification delivery in
PostgreSQL schema `telegrambot` (port 8008).

## Context routing

Inspect the affected feature under `Core/Features` (endpoint, handler, screen, use case), the
consumer under `Core/Messaging/Consumers`, and the integration tests. Cross-service backend rules
come from [`../AGENTS.md`](../AGENTS.md).

## Boundary

Chat access decisions re-read active plan grants from AccessService over HTTP; plan grant events
only trigger invites, welcomes and kicks. Account linking verifies the deep-link token through
AuthService. `notification.created` delivery is deduplicated in Redis and every outcome is
published as `TelegramDeliveryRecorded`.

## Entrypoint and verification

Entrypoint: `src/TelegramBotService.Web/Program.cs`.
Run `dotnet test backend/TelegramBotService/tests/TelegramBotService.IntegrationTests`.
