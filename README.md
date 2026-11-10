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

## Бази даних (Лаб. 01)

CampusTix продає квитки на кампусні події ЧНУ. Організатори публікують події, студенти купують квитки
й залишають відгуки. Система замінює гугл-форми та продаж на вході єдиним сервісом із контролем місць.

Скрипти: `db/p1` (OrdersDb), `db/p2` (CatalogDb), `db/p3` (ReviewsDb, MongoDB). Агрегати, процедури й вибір ключів описано в [docs/database.md](docs/database.md).

### Контексти

| # | Контекст | БД | Технологія | Сутності |
|---|---|---|---|---|
| 1 | Замовлення | `OrdersDb` (SQL Server) | ADO.NET + Dapper | Customer, Order, OrderDetails, OrderItem, TicketType (копія), OrderStatusHistory, PaymentRecord, Ticket, ProcessedEvent |
| 2 | Каталог | `CatalogDb` (SQL Server) | EF Core Code First | Event, EventDetail, Venue, Category, EventCategory, TicketType |
| 3 | Відгуки | `ReviewsDb` (MongoDB) | Clean Architecture + CQRS | reviews, discussions, messages, eventRatings |

Сутність належить одному контексту. Між базами є лише логічні посилання за Id, жодного `FOREIGN KEY`.

### Політика дублювання

| Поле | Де зберігається | Власник | Політика узгодженості |
|---|---|---|---|
| `OrderItems.EventTitle`, `TicketTypeName`, `UnitPrice` | OrdersDb | Event, TicketType (CatalogDb) | знімок на момент оформлення, не оновлюється ніколи |
| `TicketTypes.Name`, `Price`, `EventTitle`, `EventStartsAt` | OrdersDb (копія) | Event, TicketType (CatalogDb) | оновлюється подіями з Каталогу (Лаб. 08) |
| `TicketTypes.Stock` | OrdersDb | Замовлення | змінює лише сервіс Замовлень; початкове значення дорівнює `Capacity` |
| `reviews.eventTitle`, `discussions.eventTitle` | ReviewsDb | Event (CatalogDb) | оновлюється подією `event.renamed` (Лаб. 08) |
| `reviews.author.displayName` | ReviewsDb | користувач (Auth, Лаб. 09) | знімок на момент написання |

### ERD

**OrdersDb** ([джерело](docs/erd/orders.mmd))

![ERD OrdersDb](docs/erd/orders.png)

**CatalogDb** ([джерело](docs/erd/catalog.mmd))

![ERD CatalogDb](docs/erd/catalog.png)

### Розгортання з нуля

Потрібен Docker Desktop. Команди виконуються з кореня репозиторію в Git Bash.

#### SQL Server

```bash
export MSSQL_SA_PASSWORD='Your_strong_Pass1'
docker run -d --name campustix-sql -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD="$MSSQL_SA_PASSWORD" -p 11433:1433 mcr.microsoft.com/mssql/server:2022-latest
sql() { MSYS_NO_PATHCONV=1 docker exec -e SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" campustix-sql /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -f 65001 -b "$@"; }
docker cp db campustix-sql:/tmp/db

# 1) створити бази
sql -Q "IF DB_ID('OrdersDb') IS NULL CREATE DATABASE OrdersDb; IF DB_ID('CatalogDb') IS NULL CREATE DATABASE CatalogDb;"

# 2) OrdersDb: схема, процедури, дані, перевірка
sql -d OrdersDb -i /tmp/db/p1/schema.sql
sql -d OrdersDb -i /tmp/db/p1/procedures.sql
sql -d OrdersDb -i /tmp/db/p1/seed.sql
sql -d OrdersDb -i /tmp/db/p1/verify.sql

# 3) CatalogDb: схема, дані, перевірка
sql -d CatalogDb -i /tmp/db/p2/schema.sql
sql -d CatalogDb -i /tmp/db/p2/seed.sql
sql -d CatalogDb -i /tmp/db/p2/verify.sql
```

Окремий логін застосунку з доступом лише до своєї БД створює [db/p1/security.sql](db/p1/security.sql).

#### MongoDB

```bash
docker run -d --name campustix-mongo -p 27017:27017 mongo:8.0
docker cp db/p3 campustix-mongo:/tmp/p3
MSYS_NO_PATHCONV=1 docker exec campustix-mongo mongosh --quiet ReviewsDb /tmp/p3/collections.js
MSYS_NO_PATHCONV=1 docker exec campustix-mongo mongosh --quiet ReviewsDb /tmp/p3/seed.js
MSYS_NO_PATHCONV=1 docker exec campustix-mongo mongosh --quiet ReviewsDb /tmp/p3/queries.js
```

Усі скрипти ідемпотентні: повторний запуск не створює дублікатів.
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

## Resilience, метрики й health checks (Лаб. 07)

### Resilience

Усі `HttpClient` отримують Standard Resilience Handler із `ConfigureHttpClientDefaults` у `Platform.ServiceDefaults`: rate limiter, загальний таймаут, retry з jitter, circuit breaker і таймаут спроби. POST, PUT і DELETE за замовчуванням не повторюються (`DisableForUnsafeHttpMethods`), щоб не створити замовлення двічі.

gRPC-клієнти Aggregator (`Platform.Aggregator/Resilience`) спершу прибирають цей обробник (`RemoveAllResilienceHandlers`) і додають власний, тож обробники не нашаровуються. Захист має три рівні:

1. **Дедлайн виклику gRPC** обмежує весь виклик разом із повторами.
2. **`RetryPolicy` у `ServiceConfig` каналу** повторює статуси, які повертає працюючий сервер: `Unavailable`, `ResourceExhausted`. HTTP-обробник їх не бачить, бо gRPC повертає їх у трейлерах із HTTP 200.
3. **Polly всередині однієї gRPC-спроби** відповідає за таймаут спроби, мережеві повтори з експоненційною затримкою і jitter та circuit breaker. Повтори для POST тут дозволені, бо всі методи цих клієнтів лише читають дані.

| Параметр | orders-api | catalog-api | reviews-api |
|---|---|---|---|
| Критичність | критичний | критичний | некритичний |
| Дедлайн виклику | 6 с | 6 с | 2 с |
| gRPC MaxAttempts / InitialBackoff | 4 / 300 мс | 4 / 300 мс | 2 / 200 мс |
| Мережеві повтори Polly / затримка | 2 / 200 мс | 2 / 200 мс | 1 / 100 мс |
| Таймаут спроби | 1,5 с | 1,5 с | 0,5 с |
| FailureRatio / MinimumThroughput | 0,3 / 10 | 0,3 / 10 | 0,7 / 10 |
| SamplingDuration / BreakDuration | 30 с / 15 с | 30 с / 15 с | 30 с / 30 с |

Чому саме ці значення:

- **orders-api.** Без замовлення картку не зібрати, тому Aggregator повертає 503. Сервіс має більше спроб, а breaker розмикається вже при 30 % збоїв. Так система швидше перестає чекати сервіс, який систематично відмовляє.
- **catalog-api.** Без подій картка замовлення й сторінка події втрачають основний зміст. Політика та сама, що в Orders.
- **reviews-api.** Рейтинги не обов'язкові, і без них Aggregator повертає часткову відповідь (Лаб. 05). Тому сервіс швидко здається (дедлайн 2 с), терпить більше збоїв до розмикання (70 %) і отримує 30 с на відновлення.
- **Таймаути узгоджено.** Усі спроби Polly вміщуються в дедлайн: 1,5 × 3 + 0,2 + 0,4 = 5,1 с < 6 с; 0,5 × 2 + 0,1 = 1,1 с < 2 с. `DownstreamPolicy.EnsureConsistent` перевіряє це під час старту. Якби останню спробу обривав дедлайн, breaker рахував би скасування як успіх і ніколи б не розімкнувся.
- **Мінімальна пропускна здатність 10.** На навчальному стенді мало трафіку: за стандартних 100 запитів breaker тут не спрацював би.

Кеш Redis необов'язковий. Його збій перехоплюється й логується, а дані читаються з бази. Після збою Redis 30 с не використовується, щоб запити не чекали його таймауту.

### Бізнес-метрики

Метрики реєструються через `IMeterFactory` як Singleton. Meter-и додано в ServiceDefaults разом із `Polly`.

| Meter | Інструмент | Тип | Теги |
|---|---|---|---|
| `Orders.Api` | `orders.created`, `orders.tickets.ordered` | Counter | `order.status` |
| | `orders.status.changed` | Counter | `order.status` |
| | `orders.operation.duration` | Histogram, мс | `operation`, `status`, `error.type` |
| | `orders.current` | ObservableGauge | `order.status` |
| `Catalog.Api` | `products.created`, `products.published`, `products.price_changed` | Counter | `event.status` |
| | `products.operation.duration` | Histogram, мс | `operation`, `status`, `error.type` |
| | `products.cache.entries` | ObservableGauge | — |
| `Reviews.WebApi` | `reviews.created`, `reviews.comments.added` | Counter | `review.rating` |
| | `reviews.operation.duration` | Histogram, мс | `operation`, `status`, `error.type` |

Тривалість записується і для успішних, і для невдалих операцій. Теги мають мало значень і не містять Id користувачів, подій чи замовлень, бо кожна комбінація тегів утворює окремий часовий ряд. Gauge `orders.current` читає знімок, який фоновий сервіс оновлює раз на 15 с одним запитом `GROUP BY`.

Нормальні значення на стенді: `orders.operation.duration` до 100 мс; `resilience.polly.strategy.events` з `OnRetry` близько нуля. Зростання повторів або `OnCircuitOpened` означає проблему в сервісі, до якого звертаються.

### Health checks

| Endpoint | Що перевіряє | Для чого |
|---|---|---|
| `/health/live`, `/alive` | лише процес (тег `live`) | liveness: збій перезапускає процес |
| `/health/ready` | залежності з тегом `ready`, JSON з деталями | readiness: збій виключає з балансування |
| `/health` | усі перевірки | стан ресурсу в Aspire Dashboard (`WithHttpHealthCheck`) |

Ендпоінти доступні в усіх середовищах, бо вони знадобляться пробам Kubernetes у Лаб. 10. Відповідь містить лише назви, статуси й тривалість перевірок, без рядків підключення.

| Сервіс | Перевірка | Статус при збої | Теги |
|---|---|---|---|
| orders-api | `orders-db` (SQL Server, `SELECT 1`, 5 с) | Unhealthy | `database`, `sql`, `ready` |
| catalog-api | `catalog-db` (SQL Server, `SELECT 1`, 5 с) | Unhealthy | `database`, `sql`, `ready` |
| catalog-api | `redis-cache` (PING, 3 с) | Degraded | `cache`, `redis`, `ready` |
| reviews-api | `reviews-db` (MongoDB ping, 5 с) | Unhealthy | `database`, `mongodb`, `ready` |
| aggregator | `orders-api`, `catalog-api` (gRPC Health, 2 с) | Unhealthy | `grpc`, `downstream`, `ready` |
| aggregator | `reviews-api` (gRPC Health, 2 с) | Degraded | `grpc`, `downstream`, `ready` |
| aggregator | `redis-cache` (PING, 3 с) | Degraded | `cache`, `redis`, `ready` |

Сервіси відповідають за стандартним gRPC Health Checking Protocol (`MapGrpcHealthChecksService`) на основі перевірок `ready`. Catalog виключає з нього кеш: Redis, що завис, не повинен робити весь сервіс недоступним для Aggregator.

### Перевірка

```bash
curl http://localhost:5105/health/ready                     # catalog: catalog-db і redis-cache
curl http://localhost:5163/health/ready                     # aggregator: залежності через gRPC Health
curl http://localhost:5000/api/aggregator/orders/1/full     # зупиніть reviews-api в Dashboard і повторіть
```

| Сценарій | Результат |
|---|---|
| Зупинено reviews-api | перші 5 відповідей 200 з `warnings` за ~1,15 с, далі breaker розімкнено й відповідь за 15–30 мс |
| reviews-api запущено знову | у логах `opened` → `half-open` → `closed`, відповіді знову повні |
| Зупинено Redis | `/health/ready` Catalog і Aggregator мають статус Degraded з кодом 200; агрегація після першого запиту триває 30–50 мс |
| Зупинено SQL Server | `/health/live` відповідає 200 за 15 мс, `/health/ready` — 503 (`orders-db: Unhealthy`) |
| Створено три замовлення | `orders.created{order.status=Submitted}` = 3, `orders.current` показує кількість замовлень за статусами |

Відоме обмеження: перший запит одразу після падіння Redis може повернути попередження про catalog-api. Catalog чекає таймаут Redis довше, ніж триває спроба Aggregator. Наступні запити вже йдуть без Redis.

---

## Події та RabbitMQ (Лаб. 08)

Каталог є власником подій і типів квитків. Коли змінюється поле, копію якого тримають інші сервіси (таблиця «Політика дублювання» вище), Каталог публікує подію, а Orders і Reviews оновлюють свої копії. Код брокера лежить у `Platform.EventBus`, обробники — в `Services/*/*.Api/IntegrationEvents` (у Reviews — `Reviews.WebApi/IntegrationEvents`).

| Подія | Routing key | Хто споживає | Що оновлюється |
|---|---|---|---|
| `TicketTypeCreatedEvent` | `ticket-type.created` | orders-api | новий рядок `TicketTypes`, `Stock = Capacity` |
| `TicketTypePriceChangedEvent` | `ticket-type.price_changed` | orders-api | `TicketTypes.Name`, `Price`; `OrderItems.UnitPrice` не змінюється |
| `TicketTypeDeletedEvent` | `ticket-type.deleted` | orders-api | `Stock = 0`: рядок лишається, бо на нього посилаються замовлення |
| `EventRenamedEvent` | `event.renamed` | orders-api, reviews-api | `TicketTypes.EventTitle`; `eventTitle` в усіх reviews і discussions |
| `EventRescheduledEvent` | `event.rescheduled` | orders-api | `TicketTypes.EventStartsAt` |

### Топологія

| Об'єкт | Тип | Призначення |
|---|---|---|
| `catalog-events` | topic, durable | усі події Каталогу |
| `orders.catalog-sync` | черга, bindings `ticket-type.*`, `event.*` | копія довідника в OrdersDb |
| `reviews.catalog-sync` | черга, binding `event.renamed` | назва події в ReviewsDb |
| `catalog-events.dlx` | direct | dead letter exchange, routing key дорівнює імені черги |
| `orders.catalog-sync.dlq`, `reviews.catalog-sync.dlq` | черги | повідомлення для ручного розбору |

Топологію оголошує код під час старту. DLX має тип direct, а не fanout, тому повідомлення, відхилене чергою Orders, не потрапляє в DLQ Reviews.

### Надійність

- **Transactional outbox у Каталозі.** Подія записується в таблицю `OutboxMessages` тим самим `SaveChanges`, що й зміна. `OutboxPublisher` надсилає її в RabbitMQ лише після commit і з підтвердженням брокера (publisher confirms). Якщо транзакція не вдалася, події немає; якщо застосунок упав після commit, подія не губиться.
- **Формат повідомлення.** JSON у camelCase; `Persistent`, `MessageId = EventId`, `Type`; заголовки `x-correlation-id`, `x-schema-version`, `traceparent`.
- **Consumer.** `BackgroundService` на базі `RabbitMqConsumer`: `BasicQos(10)`, `AsyncEventingBasicConsumer`, `autoAck: false`, коректна зупинка.
- **Рішення щодо повідомлення.** Успіх дає `BasicAck`. Некоректний JSON, невідомий тип, новіша версія схеми або недійсні дані одразу дають `BasicNack(requeue: false)` і DLQ. Тимчасовий збій (БД недоступна) повторюється до 3 разів: копія перепублікується зі збільшеним `x-retry-count` через default exchange прямо в чергу цього сервісу, а оригінал підтверджується. Після трьох спроб повідомлення йде в DLQ.
- **Ідемпотентність.** Orders записує `EventId` у `dbo.ProcessedEvents` (PRIMARY KEY) у тій самій транзакції, що й зміну. У Reviews MongoDB працює без replica set, тому транзакцій немає. Кроки впорядковано так, що повтор безпечний: перевірка `processedEvents`, перевірка версії, `UpdateMany`, а потім запис часу зміни й `EventId`.
- **Порядок подій.** Нова зміна застосовується, лише якщо вона новіша за застосовану: `TicketTypes.SourceUpdatedAt` для полів типу квитка, `EventSourceUpdatedAt` для полів події, колекція `catalogSync` у Reviews.
- **Трасування.** Контекст іде в заголовку `traceparent`. Outbox зберігає його в рядку, тому trace HTTP-запиту продовжується у фоновому publisher. Consumer створює span типу Consumer з тегами `messaging.*`, а `ServiceDefaults` слухає джерело `Platform.Messaging`.
- **Метрики.** `messaging.messages.handled` (теги `result` = ack / retry / dead_letter) і gauge `messaging.dlq.depth`.

### Перевірка

```bash
curl -X PUT -H "Content-Type: application/json" -d '{"price":160}' http://localhost:5000/api/ticket-types/1/price
curl http://localhost:5103/api/tickets/1          # копія в Orders: 160
curl http://localhost:5103/api/orders/1           # позиції замовлення: стара ціна 150
curl -X PUT -H "Content-Type: application/json" -d '{"title":"CampusHack 2026: Final"}' http://localhost:5000/api/events/2/title
curl "http://localhost:5102/api/reviews?eventId=2"  # eventTitle оновлено
```

Management UI відкривається з Aspire Dashboard (ресурс `messaging`, endpoint management).

| Сценарій | Результат |
|---|---|
| Зміна ціни типу 1 | копія в Orders 150 → 160 за ~1 с; у замовленні 1 лишилось 150 |
| Перейменування події 2 | `eventTitle` у 2 відгуках і `EventTitle` двох типів квитків в Orders оновлено |
| Та сама подія двічі (той самий `EventId`, друга з ціною 999) | друга пропущена, ціна лишилась 410 |
| Некоректний JSON через Management API | `orders.catalog-sync.dlq` має 1 повідомлення з `x-death: rejected`, DLQ Reviews порожня |
| orders-api зупинено, змінено три ціни, запущено | після старту всі три зміни застосовано |
| SQL Server зупинено під час обробки | у логах `retry 1 of 3` … `retry 3 of 3`, потім `sent to DLQ` |
| Trace перейменування | один trace: gateway → catalog-api (SELECT, UPDATE) → publish → orders-api і reviews-api (find, update reviews, update discussions) |

---

## Аутентифікація та авторизація (Лаб. 09)

Платформа приймає токени трьох провайдерів. Кожен сервіс перевіряє токен локально ключами його видавця й сам вирішує, чи дозволена дія. До провайдера на кожен запит сервіс не звертається.

| Провайдер | Проєкт | Токен | Як сервіс перевіряє |
|---|---|---|---|
| Auth Service (частина 1) | `Services/Auth/Auth.Api` | HS256, `iss = campustix-auth`, ролі в claim `role` | спільний секрет `jwt-secret` (параметр AppHost) |
| Duende IdentityServer (частина 2) | `Platform.IdentityServer` | RS256, `aud` = ApiResource, `scope` | Authority = `http://localhost:5010`, ключі з discovery |
| Keycloak (частина 3) | AppHost + `infra/keycloak` | realm `DevRealmDrivers`, RPT з `authorization.permissions` | `AddKeycloakJwtBearer`, ключі realm |

Схема за замовчуванням `Bearer` дивиться лише на `iss` і передає токен відповідному обробнику (`Platform.ServiceDefaults/Authentication`). Кожен ендпоінт має `[RequirePermission("orders:read")]`. Звідки беруться permissions:

- **Keycloak.** Зі scopes у `authorization.permissions` (RPT).
- **IdentityServer.** Зі scopes: `orders.read` → `orders:read`; `orders.write` → create, update, delete, approve. Те саме для `products`.
- **Auth Service.** З ролей: `Admin` отримує все, `User` — `orders:read`, `orders:create` і `products:read`.

gRPC-методи для Aggregator мають scope-політики (`[Authorize(Policy = "orders.read")]`).

Gateway перевіряє токен до проксування (політика маршруту `default`). `/auth/**` пропускає анонімно, `/users/**` і `/api/payments/**` спрямовує в Auth Service і Payments. Заголовок `Authorization` йде далі без змін, а `X-User-Id` Gateway ставить сам з перевіреного `sub` і прибирає значення від клієнта.

### Частина 1. Auth Service

ASP.NET Identity на окремій базі `IdentityDb`: пароль від 8 символів з цифрою, великою літерою й символом; lockout після 5 спроб на 15 хв. Access token живе 15 хв (claims `sub`, `unique_name`, `email`, `role`). Refresh token живе 7 днів: у базі лише його SHA-256 разом із датами, відкликанням і `ReplacedByTokenId`, а клієнт отримує його в HttpOnly cookie (`SameSite=Strict`, `Path=/auth`). Під час ротації старий токен відкликається. Повторне використання відкликаного токена відкликає всі токени користувача.

Ендпоінти: `register`, `login`, `refresh-token`, `revoke-token`, `logout`, `verify-email`, `resend-verification`, `forgot-password`, `reset-password`, `change-password` у `/auth`, а також `/users` з ролями (таблиця 2 методички). Листи в Development не надсилаються: `GET /auth/dev/mailbox?email=...` показує їх. Адміністратор `admin` створюється з `appsettings.Development.json`.

```bash
curl -X POST -H "Content-Type: application/json" -d '{"userName":"admin","password":"Admin123!dev"}' http://localhost:5000/auth/login
curl -H "Authorization: Bearer <accessToken>" http://localhost:5000/api/orders
```

| Сценарій (через Gateway) | Результат |
|---|---|
| Запит без токена | 401 |
| Реєстрація / дубль / слабкий пароль | 201 / 409 / 400 |
| Login до підтвердження email | 403 |
| verify-email / той самий токен удруге | 204 / 400 |
| Login | 200; cookie `httponly; samesite=strict; path=/auth`; claims `sub`, `unique_name`, `role`; життя 15 хв |
| User: `GET /api/orders`, `POST /api/orders` з чужим `customerId` | бачить лише свої замовлення; замовлення створено на його профіль |
| User: чуже замовлення, DELETE, payments, `/users`, зміна ціни | 403 |
| User: свій `/users/{id}`, `GET /api/events/1` | 200 |
| Refresh / повтор старого токена / наступний refresh | 200 / 401 / 401 (усі токени відкликано) |
| Скидання пароля / той самий токен удруге / вхід зі старим паролем | 204 / 400 / 401 |
| Admin: `/users`, `POST /api/payments/process`, призначення ролі | 200 / 201 / 204 |
| Admin: агрегат через Gateway | 200, Aggregator ходить у сервіси з токеном Client Credentials |

### Частина 2. Duende IdentityServer

EF-сховища конфігурації та операційних даних і ASP.NET Identity в окремій базі `IdentityServerDb`, міграції трьох контекстів (`Data/Migrations`) і Quickstart UI (`Pages/`). Бази Identity займають той самий контейнер SQL Server, що й бізнес-бази, щоб заощадити пам'ять ПК, але це окремі бази.

| Що | Значення |
|---|---|
| ApiScopes | `orders.read`, `orders.write`, `products.read`, `products.write`, `reviews.read` |
| ApiResources | `orders_api`, `products_api`, `reviews_api`, `payments_api` (стають `aud`) |
| `postman` | Authorization Code + PKCE, секрет, refresh token |
| `aggregator_service` | Client Credentials, `orders.read products.read reviews.read` |
| `swagger` | public client, PKCE обов'язковий, без секрету |

| Перевірка | Результат |
|---|---|
| Client Credentials | токен без `sub`, `aud = [orders_api, products_api]`; хибний секрет дає `invalid_client` |
| Authorization Code (alice) | login → code → token з `sub`; хибний `code_verifier` і повтор коду дають `invalid_grant` |
| Токен з `aud = orders_api` | 200 в Orders, 401 у Catalog і Payments |
| Public client без PKCE | сторінка помилки IdentityServer |

Над HTTP у Development cookie IdentityServer мають `SameSite=Lax`. Зі стандартним `None` без `Secure` браузер їх відкидає, і вхід повертався на сторінку логіну.

### Частина 3. Keycloak + Pulumi

Keycloak запускається через Aspire (`AddKeycloak("keycloak", 8080).WithDataVolume()`, траєкторія Б). Realm, resource servers, ролі, policies, permissions, clients і користувачів описує Pulumi-проєкт `infra/keycloak`; команди й пояснення є в [infra/keycloak/README.md](infra/keycloak/README.md). Демо-сервіс `Services/Payments/Payments.Api` має ендпоінти-заглушки для матриці.

| Endpoint | Permission | admin | manager | user | guest |
|---|---|---|---|---|---|
| `GET /api/orders` | `orders:read` | ✓ | ✓ | ✓ | ✗ |
| `POST /api/orders` | `orders:create` | ✓ | ✗ | ✓ | ✗ |
| `PUT /api/orders/{id}` | `orders:update` | ✓ | ✓ | ✗ | ✗ |
| `DELETE /api/orders/{id}` | `orders:delete` | ✓ | ✗ | ✗ | ✗ |
| `POST /api/orders/{id}/approve` | `orders:approve` | ✓ | ✓ | ✗ | ✗ |
| `GET /api/events` | `products:read` | ✓ | ✓ | ✓ | ✓ |
| `POST /api/events` | `products:create` | ✓ | ✗ | ✗ | ✗ |
| `PUT /api/events/{id}/title` | `products:update` | ✓ | ✓ | ✗ | ✗ |
| `DELETE /api/events/{id}` | `products:delete` | ✓ | ✗ | ✗ | ✗ |
| `POST /api/events/{id}/publish` | `products:publish` | ✓ | ✓ | ✗ | ✗ |
| `GET /api/payments`, `POST /api/payments/process`, `POST /api/payments/refund` | `payments:*` | ✓ | ✗ | ✗ | ✗ |

У CampusTix «products» — це події та типи квитків Каталогу. Решта ендпоінтів Orders і Catalog теж мають `[RequirePermission]`: зміна статусу, скасування й оплата вимагають `orders:update`, видача квитків — `orders:approve`, ціна й залишок — `products:update`.

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
└── Orders.Tests/    xUnit + NSubstitute
```

`Platform.AppHost` запускає API разом із SQL Server у контейнері однією командою.

Залежності: `Api → Bll → Dal`, `Domain` бачать `Dal` і `Bll`. SQL є лише в `Orders.Dal` (і в `db/orders/`).

### Запуск

Потрібні .NET 10 SDK і запущений Docker Desktop. Схема, процедури й тестові дані з `db/orders/` застосовуються на старті.

```bash
dotnet run --project Platform.AppHost --launch-profile http
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
