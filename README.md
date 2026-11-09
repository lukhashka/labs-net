# CampusTix — мікросервісна платформа квитків на кампусні події ЧНУ

Студент: Луханін Богдан Сергійович, група 344Б.

**Домен.** CampusTix продає квитки на події університету (концерти, хакатони, лекції, спорт).
Організатори публікують події з типами квитків (Standard / VIP / Student), студенти оформлюють
замовлення й залишають відгуки.

| Контекст | Сервіс | БД | Технологія | Лаба |
|---|---|---|---|---|
| Замовлення | `Services/Orders` | OrdersDb (SQL Server) | ADO.NET + Dapper | 02 |
| Каталог подій | `Services/Catalog` | CatalogDb (SQL Server) | EF Core Code First | 03 |
| Відгуки | `Services/Reviews` | ReviewsDb (MongoDB) | Clean Architecture + CQRS | 04 |

Усі проєкти — в одному рішенні `Platform.slnx`.

---

## Платформа (Лаб. 05) — Aspire, YARP, Aggregator

```
Platform.AppHost          оркестрація: SQL Server, MongoDB, сервіси, Aggregator, Gateway (без бізнес-логіки)
Platform.ServiceDefaults  Serilog, OpenTelemetry, health checks, service discovery, CorrelationId
Platform.ApiGateway       YARP: єдина точка входу на порту 5000
Platform.Aggregator       композиція даних кількох сервісів, часткові відмови
Services/Orders, Catalog, Reviews   мікросервіси з Лаб. 02–04
```

Порядок запуску задано ланцюжками `WaitFor`: БД → сервіси → Aggregator → Gateway.

### Запуск

Потрібні .NET 10 SDK і запущений Docker Desktop. Пароль SQL Server задається один раз, він не потрапляє в Git:

```bash
dotnet user-secrets set "Parameters:sql-password" "Your_strong_Pass1" --project Platform.AppHost
```

```bash
dotnet run --project Platform.AppHost --launch-profile http
```

Посилання на Dashboard з токеном виводиться в консоль. Профіль `http` обрано, щоб Dashboard і OTLP працювали без довіреного dev-сертифіката.

### Перевірка

```bash
curl -i http://localhost:5000/api/orders/1
curl -i "http://localhost:5000/api/events?page=1"
curl -i "http://localhost:5000/api/reviews?eventId=1"
curl -i http://localhost:5000/api/aggregator/orders/1/full
curl -i http://localhost:5000/api/aggregator/events/2/full
```

**Trace.** Вкладка Traces, запис `gateway: GET /api/aggregator/{**catch-all}`: Gateway → Aggregator → сервіси → SQL і MongoDB.

**Логи за CorrelationId.** Взяти `X-Correlation-Id` з відповіді, у Structured logs додати фільтр `CorrelationId = <значення>`.

**Часткова відповідь.** У Dashboard зупинити `reviews-api` і повторити запит до агрегатора: статус 200, дані замовлення й подій, `"warnings":["reviews-api is unavailable"]`.
Якщо зупинити сервіс основних даних (`orders-api` для картки замовлення), відповідь буде 503 у форматі ProblemDetails.

---

## gRPC і кешування (Лаб. 06)

**Кеш у Каталозі.** Cache-aside на двох рівнях: `IMemoryCache` (L1) → Redis (L2) → база. Код: `Services/Catalog/Catalog.Bll/Caching`.

| Дані | Ключ | L1 | L2 |
|---|---|---|---|
| Картка події | `catalog:event:{id}` | 1 хв (sliding 30 с) | 10 хв |
| Перша сторінка афіші | `catalog:events:first-page` | 30 с | 5 хв |
| Категорії | `catalog:categories` | 5 хв | 1 год |
| Агрегат (Aggregator) | `aggregator:order:{id}:full`, `aggregator:event:{id}:full` | немає | 30 с |

Запис одразу видаляє пов'язані ключі. Якщо Redis недоступний, сервіс працює без L2, а пропущені інвалідації застосовує після відновлення.

**gRPC.** Контракти лежать у `Protos/` кожного сервісу: `catalog.proto`, `orders.proto`, `reviews.proto`. Aggregator збирає клієнтів із цих самих файлів і викликає сервіси паралельно. Для зовнішніх клієнтів він лишається HTTP-ендпоінтом.

HTTP/2 узгоджується через TLS, тому сервіси працюють у профілі `https`. Якщо dev-сертифікат не довірений, Aggregator у Development приймає лише сертифікат із OID ASP.NET Core; в інших середовищах діє звичайна перевірка.

```bash
# другий запит не має SQL-span у Traces
curl http://localhost:5000/api/events/1
curl http://localhost:5000/api/events/1
# після зміни ціни картка свіжа
curl -X PUT -H "Content-Type: application/json" -d '{"price":175}' http://localhost:5000/api/ticket-types/1/price
curl http://localhost:5000/api/events/1
# агрегат через gRPC
curl -i http://localhost:5000/api/aggregator/orders/1/full
```

Запуск без кешів для порівняння HTTP і gRPC:

```bash
dotnet run --project Platform.AppHost --launch-profile http -- --Benchmark:CacheEnabled=false
```

---

## Catalog (Лаб. 03) — EF Core

```
Services/Catalog/
├── Catalog.Domain/   сутності (Event, TicketType, Venue, Category, EventCategory, EventDetail), доменні винятки
├── Catalog.Dal/      CatalogDbContext, Configurations/ (IEntityTypeConfiguration<T>), Migrations/,
│                     Repositories/ (Generic + Eager/Explicit/M:N), UnitOfWork, Seeding/DbInitializer
├── Catalog.Bll/      Services/, Dtos/, Mapping/ (AutoMapper), Validation/ (FluentValidation), Specifications/
└── Catalog.Api/      Controllers/, Middleware/ (ProblemDetails, CorrelationId), Program.cs, appsettings.json, Dockerfile
```

Посилання між шарами: `Api → Bll → Dal → Domain`. Контролери не бачать `DbContext`.

### Схема CatalogDb

| Таблиця | Ключові обмеження | Зв'язок |
|---|---|---|
| Venues | `Name` UNIQUE, CHECK `Capacity > 0`, seed `HasData` | 1:N → Events |
| Events | `Slug` UNIQUE, CHECK `EndsAt > StartsAt`, `RowVersion`, аудит, soft delete | |
| EventDetails | PK = FK `EventId`, CHECK `AgeLimit BETWEEN 0 AND 21` | **1:1** з Events |
| Categories | `Name` UNIQUE, seed `HasData` (6 категорій) | |
| EventCategories | PK (`EventId`, `CategoryId`), **`AddedAt`** DEFAULT `SYSUTCDATETIME()` | **M:N** з власним полем |
| TicketTypes | `Code` UNIQUE (аналог SKU), CHECK `Price > 0`, `decimal(18,2)` | 1:N від Events |

Індекси під запити: `Events(StartsAt)` — список і сортування за датою; `Events(VenueId, StartsAt)` —
події місця; `EventCategories(CategoryId, AddedAt)` — «нещодавно додані в категорію»;
`TicketTypes(EventId, Price)` — квитки події за ціною.

### Запуск

Потрібно: .NET 10 SDK (проєкт сумісний з .NET 9), SQL Server LocalDB або SQL Server у Docker.

```bash
# 1. SQL Server (якщо немає LocalDB)
docker run -d --name catalog-sql -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD='Your_strong_Pass1' -p 1433:1433 mcr.microsoft.com/mssql/server:2022-latest
# і перевизначити рядок підключення:
export ConnectionStrings__CatalogDb="Server=localhost,1433;Database=CatalogDb;User Id=sa;Password=Your_strong_Pass1;TrustServerCertificate=True"

# 2. Запуск API: міграції та seed застосовуються автоматично на старті
dotnet run --project Services/Catalog/Catalog.Api --launch-profile http
# Swagger: http://localhost:5105/swagger
```

### Міграції

```bash
dotnet tool install --global dotnet-ef
dotnet ef migrations add InitialCreate --project Services/Catalog/Catalog.Dal --startup-project Services/Catalog/Catalog.Api
dotnet ef database update --project Services/Catalog/Catalog.Dal --startup-project Services/Catalog/Catalog.Api
dotnet ef migrations script --idempotent --project Services/Catalog/Catalog.Dal --startup-project Services/Catalog/Catalog.Api -o Services/Catalog/Catalog.Dal/Migrations/Sql/InitialCreate.sql
```

Згенерований скрипт: `Services/Catalog/Catalog.Dal/Migrations/Sql/InitialCreate.sql`.

**Сідінг** ідемпотентний: довідники (`Venues`, `Categories`) — `HasData` з фіксованими Id у міграції;
події, типи квитків і зв'язки з категоріями — `DbInitializer.SeedAsync` після `MigrateAsync()` з
перевіркою наявності. Вимикається `Seeding:Enabled=false` (у Production вимкнено за замовчуванням).

### Пагінація, фільтрація, сортування

Запит: `page` (з 1), `pageSize` (за замовчуванням 20, максимум 100), фільтри, `sortBy`, `sortDir`.
Відповідь — у тілі (`items, totalCount, page, pageSize, totalPages, hasNext, hasPrevious`) + заголовок
`X-Total-Count`. Усе виконується в SQL (`OFFSET … FETCH`) до `ToListAsync()`.

### Приклади запитів

```bash
# список подій: пошук, категорія, ціна, сортування, сторінка (Specification EventsFilterSpec)
curl "http://localhost:5105/api/events?search=Hack&categoryId=2&minPrice=10&sortBy=price&sortDir=desc&page=1&pageSize=5"
# пагінація типів квитків: totalCount = кількість усіх записів
curl "http://localhost:5105/api/ticket-types?page=2&pageSize=5&sortBy=price&sortDir=desc"
# картка події — Eager Loading (1 SQL-запит)
curl http://localhost:5105/api/events/1
# події категорії — Explicit Loading (2 SQL-запити)
curl http://localhost:5105/api/categories/1/events
# нещодавно додані в категорію — LINQ M:N через EventCategory.AddedAt
curl "http://localhost:5105/api/categories/2/recent-events?days=30"
# найближчі події місця — друга специфікація UpcomingEventsByVenueSpec
curl http://localhost:5105/api/venues/1/events
# створення: 201 + Location
curl -i -H "Content-Type: application/json" -d '{"eventId":1,"name":"Early","code":"CONC26-EARLY","price":99.5,"capacity":10}' http://localhost:5105/api/ticket-types
# той самий код ще раз -> 409
curl -i -H "Content-Type: application/json" -d '{"eventId":1,"name":"Early","code":"CONC26-EARLY","price":99.5,"capacity":10}' http://localhost:5105/api/ticket-types
# порожні поля -> 400 ValidationProblemDetails; неіснуючий Id -> 404
curl -i -H "Content-Type: application/json" -d '{"eventId":1,"name":"","code":"","price":0,"capacity":0}' http://localhost:5105/api/ticket-types
curl -i http://localhost:5105/api/events/999
# списання більше, ніж є -> 409 (DomainException з TicketType.RemoveStock)
curl -i -H "Content-Type: application/json" -d '{"quantity":5000}' http://localhost:5105/api/ticket-types/1/stock/remove
```

### Обробка помилок і логування

`GlobalExceptionHandler` (`IExceptionHandler` + `AddProblemDetails`): `EntityNotFoundException → 404`,
`ValidationException → 400`, `ConflictException`/`DomainException`/`DbUpdateConcurrencyException`/дубль ключа `→ 409`,
решта `→ 500` без деталей поза Development. Кожен виняток логується один раз — в обробнику.

Serilog налаштовано в `appsettings.json` (рівні, sink-и Console + File `logs/catalog-*.log` у JSON).
Властивості: `CorrelationId` (заголовок `X-Correlation-Id`), `UserId`, `RequestPath`, `Service`.
SQL-команди EF Core (`Microsoft.EntityFrameworkCore.Database.Command`) пишуться на рівні Information.

---

## Orders (Лаб. 02) — ADO.NET + Dapper

```
Services/Orders/
├── Orders.Domain/   моделі таблиць OrdersDb, доменні винятки
├── Orders.Dal/      IGenericRepository<T>, CustomerRepository (чистий ADO.NET),
│                    BaseDapperRepository<T> → OrderRepository, TicketTypeRepository (Dapper), UnitOfWork
├── Orders.Bll/      сервіси, DTO, профіль AutoMapper
├── Orders.Api/      контролери, DomainExceptionHandler (ProblemDetails), Swagger, Serilog
├── Orders.AppHost/  API + SQL Server у контейнері однією командою
└── Orders.Tests/    xUnit + NSubstitute
```

Залежності: `Api → Bll → Dal`, `Domain` бачать `Dal` і `Bll`. SQL є лише в `Orders.Dal` (і в `db/orders/`).

### Запуск

Потрібні .NET 10 SDK і запущений Docker Desktop. Схема, процедури й тестові дані з `db/orders/` застосовуються на старті.

```bash
dotnet run --project Services/Orders/Orders.AppHost --launch-profile http
# Dashboard: посилання з токеном є в консолі; Swagger: http://localhost:5103/swagger
```

Без Aspire сервіс бере рядок підключення з `appsettings.json` (LocalDB), його можна перевизначити змінною `ConnectionStrings__OrdersDb`.

### Транзакції

`POST /api/orders/{id}/confirm` у межах однієї транзакції міняє статус (`usp_ConfirmOrder`) і списує квитки
(`TicketTypeRepository.DecreaseStockAsync`). Рівень ізоляції — `READ COMMITTED`: він не тримає спільних блокувань до
кінця транзакції, тому рідше дає взаємні блокування. Гонки закриті явно: `UPDLOCK` у процедурі й атомарний `UPDATE ... WHERE Stock >= @Quantity`.
### Приклади запитів

```bash
curl http://localhost:5103/api/orders/1
curl "http://localhost:5103/api/orders?customerId=1&page=1&pageSize=5"
curl "http://localhost:5103/api/tickets?eventId=2"
# створення: 201 + Location
curl -i -H "Content-Type: application/json" -d '{"customerId":1,"attendeeName":"Test","attendeeEmail":"t@chnu.edu.ua","items":[{"ticketTypeId":5,"units":15}]}' http://localhost:5103/api/orders
# підтвердження: 204; повторне: 409; неіснуюче: 404
curl -i -X POST http://localhost:5103/api/orders/1/confirm
curl -i -X POST http://localhost:5103/api/orders/1/confirm
curl -i -X POST http://localhost:5103/api/orders/999/confirm
# недопустимий перехід: 409
curl -i -H "Content-Type: application/json" -d '{"status":"Shipped"}' http://localhost:5103/api/orders/3/status
# ім'я з апострофом не ламає SQL
curl -i -H "Content-Type: application/json" -d "{\"email\":\"m.obrien@chnu.edu.ua\",\"fullName\":\"Mary O'Brien\"}" http://localhost:5103/api/customers
```

**Демо відкату.** Створити два замовлення: A на 15 квитків типу 5, B на 1 квиток типу 4 і 15 типу 5 (на складі типу 5 їх 20).
Підтвердити A, потім B. B поверне 409, його статус лишиться `Submitted`, а залишок типу 4 не зміниться, хоча цей квиток списувався першим.

---

## Reviews (Лаб. 04) — Clean Architecture + CQRS (MediatR) + MongoDB

```
Services/Reviews/
├── Reviews.Domain/          BaseEntity (ObjectId, CreatedAt, UpdatedAt, Version), Review/Comment/Discussion,
│                            ValueObject + Author, Rating, доменні винятки, IReviewRepository, IDiscussionRepository
├── Reviews.Application/     Commands/ і Queries/ (MediatR), Common/Behaviors (Logging, Validation, Performance),
│                            IReviewQueries, FluentValidation, AutoMapper-профіль, AddApplication()
├── Reviews.Infrastructure/  MongoDbContext (IOptions), BsonClassMap + camelCase, репозиторії з optimistic
│                            concurrency, ReviewQueries (aggregation + проєкції), індекси, IDataSeeder, AddInfrastructure()
└── Reviews.WebApi/          контролери лише через ISender, GlobalExceptionHandler (ProblemDetails), Program.cs
```

Залежності: `Domain → нічого`, `Application → Domain`, `Infrastructure → Application, Domain`,
`WebApi → Application, Infrastructure`. Domain посилається лише на `MongoDB.Bson` (тип ObjectId), без драйвера.

### Запуск

```bash
docker run -d --name reviews-mongo -p 27017:27017 mongo:7
dotnet run --project Services/Reviews/Reviews.WebApi --launch-profile http
# Swagger: http://localhost:5102/swagger
# індекси створюються автоматично на старті, seed — ідемпотентний (InsertManyAsync лише в порожню колекцію)
mongosh ReviewsDb_Dev --eval "db.reviews.getIndexes()"
```

Налаштування — секція `MongoDb` в `appsettings.json` / `appsettings.Development.json`
(override: `MongoDb__ConnectionString`).

### Приклади запитів

```bash
curl "http://localhost:5102/api/reviews?eventId=1&page=1&pageSize=10"
# 201 + Location з ObjectId
curl -i -H "Content-Type: application/json" -d '{"eventId":1,"eventTitle":"Autumn concert","authorUserId":"u-1","authorName":"Bohdan","rating":5,"text":"Great!"}' http://localhost:5102/api/reviews
# rating = 10 -> 400 ще до виклику обробника (видно в LoggingBehavior)
curl -i -H "Content-Type: application/json" -d '{"eventId":1,"eventTitle":"x","authorUserId":"u-2","authorName":"A","rating":10,"text":"t"}' http://localhost:5102/api/reviews
# той самий автор вдруге -> 400 (асинхронне правило з запитом до Mongo); застаріла Version у PUT -> 409
# неіснуючий Id -> 404; некоректний ObjectId -> 400; Mongo недоступна -> 503
```
