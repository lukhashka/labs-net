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
