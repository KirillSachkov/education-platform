# Template Service

Шаблон микросервиса SachkovTech — Clean Architecture, EF Core, PostgreSQL, Serilog, FluentValidation, интеграционные тесты.

## Создание нового микросервиса

### 1. Клонировать шаблон

```bash
git clone https://gitlab.com/sachkovtech/kernel/template-service.git
cd template-service
```

### 2. Установить шаблон в dotnet

```bash
dotnet new install .
```

После этого шаблон `st-service` появится в списке доступных:

```bash
dotnet new list st-service
```

### 3. Создать новый сервис

Перейди в папку, где хочешь создать проект, и выполни:

```bash
cd ~/code/sachkovtech
dotnet new st-service -n OrderService --dbName order_db --dbPort 5435 --appPort 5001
```

Это создаст папку `OrderService/` с полной структурой, где все неймспейсы, имена проектов и файлов будут заменены на `OrderService`.

#### Параметры

| Параметр    | По умолчанию  | Описание                              |
| ----------- | ------------- | ------------------------------------- |
| `-n`        | `MyService`   | Имя сервиса (неймспейсы, файлы, .sln) |
| `--dbName`  | `comment_db` | Имя базы данных PostgreSQL            |
| `--dbPort`  | `5434`        | Внешний порт PostgreSQL (docker)      |
| `--appPort` | `8004`        | Порт приложения (launchSettings)      |

### 4. Настроить NuGet

Скопируй и заполни конфиг для приватного NuGet-фида:

```bash
cd OrderService
cp nuget.config.example nuget.config
```

Открой `nuget.config` и заполни `YOUR_PROJECT_ID`, `YOUR_GITLAB_USERNAME`, `YOUR_GITLAB_TOKEN`.

### 5. Восстановить зависимости

```bash
dotnet restore
```

### 6. Поднять инфраструктуру

```bash
docker compose up -d
```

> После создания сервиса поправь `container_name` в `docker-compose.yml`, чтобы избежать конфликтов с другими сервисами.

### 7. Запустить

```bash
dotnet run --project src/OrderService.Web
```

Swagger будет доступен по адресу `http://localhost:5001/swagger`.

### 8. Инициализировать Git

```bash
git init
git add .
git commit -m "init: OrderService from template"
git remote add origin <url-вашего-репозитория>
git push -u origin main
```

## Что входит в шаблон

```text
OrderService/
├── src/
│   ├── OrderService.Web/              # Точка входа, DI, Middleware
│   ├── OrderService.Core/             # Handlers, Validators, Endpoints
│   ├── OrderService.Domain/           # Доменные сущности, Value Objects
│   ├── OrderService.Contracts/        # DTO, Request-модели
│   └── OrderService.Infrastructure.Postgres/  # EF Core, Repository, Migrations
├── tests/
│   └── OrderService.IntegrationTests/ # Testcontainers, Respawn
├── Directory.Build.props              # Общие настройки сборки
├── Directory.Packages.props           # Central Package Management
├── docker-compose.yml                 # PostgreSQL + Seq
├── Dockerfile                         # Multi-stage build
└── nuget.config.example               # Шаблон NuGet-конфига
```

## Удаление шаблона

```bash
dotnet new uninstall <путь-к-склонированному-template-service>
```
