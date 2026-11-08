# CampusTix — бази даних (Лаб. 01)

CampusTix продає квитки на кампусні події ЧНУ. Організатори публікують події, студенти купують квитки
й залишають відгуки. Система замінює гугл-форми та продаж на вході єдиним сервісом із контролем місць.

## Контексти

| # | Контекст | БД | Технологія | Сутності |
|---|---|---|---|---|
| 1 | Замовлення | `OrdersDb` (SQL Server) | ADO.NET + Dapper | Customer, Order, OrderDetails, OrderItem, TicketType (копія), OrderStatusHistory, PaymentRecord, Ticket, ProcessedEvent |
| 2 | Каталог | `CatalogDb` (SQL Server) | EF Core Code First | Event, EventDetail, Venue, Category, EventCategory, TicketType |
| 3 | Відгуки | `ReviewsDb` (MongoDB) | Clean Architecture + CQRS | reviews, discussions, messages, eventRatings |

Сутність належить одному контексту. Між базами є лише логічні посилання за Id, жодного `FOREIGN KEY`.

## Агрегати

| Контекст | Агрегат | Життєвий цикл |
|---|---|---|
| Замовлення | **Order** → OrderItem, OrderDetails, OrderStatusHistory, Ticket, PaymentRecord | Submitted → AwaitingValidation → StockConfirmed → Paid → Shipped; Cancelled лише до Paid |
| Замовлення | **Customer** | створюється при реєстрації |
| Каталог | **Event** → EventDetail, EventCategory, TicketType | Draft → Published → Cancelled |
| Каталог | **Category**, **Venue** | довідники |
| Відгуки | **Review** → comments[] | створення, коментування, редагування з version |
| Відгуки | **Discussion** → messages | відкрите / закрите |

## Політика дублювання

| Поле | Де зберігається | Власник | Політика узгодженості |
|---|---|---|---|
| `OrderItems.EventTitle`, `TicketTypeName`, `UnitPrice` | OrdersDb | Event, TicketType (CatalogDb) | знімок на момент оформлення, не оновлюється ніколи |
| `TicketTypes.Name`, `Price`, `EventTitle`, `EventStartsAt` | OrdersDb (копія) | Event, TicketType (CatalogDb) | оновлюється подіями з Каталогу (Лаб. 08) |
| `TicketTypes.Stock` | OrdersDb | Замовлення | змінює лише сервіс Замовлень; початкове значення дорівнює `Capacity` |
| `reviews.eventTitle`, `discussions.eventTitle` | ReviewsDb | Event (CatalogDb) | оновлюється подією `event.renamed` (Лаб. 08) |
| `reviews.author.displayName` | ReviewsDb | користувач (Auth, Лаб. 09) | знімок на момент написання |

## ERD

- OrdersDb: [erd/orders.mmd](erd/orders.mmd), зображення [erd/orders.png](erd/orders.png)
- CatalogDb: [erd/catalog.mmd](erd/catalog.mmd), зображення [erd/catalog.png](erd/catalog.png)

## Розгортання з нуля

Потрібен Docker Desktop. Команди виконуються з кореня репозиторію в Git Bash.

### SQL Server

```bash
export MSSQL_SA_PASSWORD='Your_strong_Pass1'
docker run -d --name campustix-sql -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD="$MSSQL_SA_PASSWORD" -p 11433:1433 mcr.microsoft.com/mssql/server:2022-latest
sql() { MSYS_NO_PATHCONV=1 docker exec -e SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" campustix-sql /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -f 65001 -b "$@"; }
docker cp db campustix-sql:/tmp/db

# 1) створити бази
sql -Q "IF DB_ID('OrdersDb') IS NULL CREATE DATABASE OrdersDb; IF DB_ID('CatalogDb') IS NULL CREATE DATABASE CatalogDb;"

# 2) OrdersDb: схема, процедури, дані, перевірка
sql -d OrdersDb -i /tmp/db/orders/schema.sql
sql -d OrdersDb -i /tmp/db/orders/procedures.sql
sql -d OrdersDb -i /tmp/db/orders/seed.sql
sql -d OrdersDb -i /tmp/db/orders/verify.sql

# 3) CatalogDb: схема, дані, перевірка
sql -d CatalogDb -i /tmp/db/catalog/schema.sql
sql -d CatalogDb -i /tmp/db/catalog/seed.sql
sql -d CatalogDb -i /tmp/db/catalog/verify.sql
```

Окремий логін застосунку з доступом лише до своєї БД створює [orders/security.sql](orders/security.sql).

### MongoDB

```bash
docker run -d --name campustix-mongo -p 27017:27017 mongo:8.0
docker cp db/reviews campustix-mongo:/tmp/reviews
MSYS_NO_PATHCONV=1 docker exec campustix-mongo mongosh --quiet ReviewsDb /tmp/reviews/mongo-init.js
MSYS_NO_PATHCONV=1 docker exec campustix-mongo mongosh --quiet ReviewsDb /tmp/reviews/queries.js
```

Усі скрипти ідемпотентні: повторний запуск не створює дублікатів.

## Збережувані процедури OrdersDb

| Процедура | Що робить | Помилки |
|---|---|---|
| `usp_GetOrderWithItems` | замовлення з позиціями | |
| `usp_ConfirmOrder` | `Submitted`/`AwaitingValidation` → `StockConfirmed` | 50001, 50002 |
| `usp_ChangeOrderStatus` | транзакційна зміна статусу за таблицею допустимих переходів | 50001, 50002 |
| `usp_RegisterPayment` | `StockConfirmed` → `Paid` + `PaymentRecords` | 50001, 50002, 2627 |
| `usp_IssueTickets` | `Paid` → `Shipped` + рядок `Tickets` на кожну одиницю | 50001, 50002 |
| `usp_CancelOrder` | скасування до оплати, повертає місця | 50001, 50002 |

`50001` означає «не знайдено», `50002` — бізнес-конфлікт. Сервіс перетворює їх на HTTP 404 і 409.

## Вибір ключів і конкурентність

- `BIGINT IDENTITY` для замовлень: вставка йде з одного сервісу, послідовний ключ дешевий для кластерного індексу.
- `UNIQUEIDENTIFIER` для `Tickets.Id`: код квитка показується як QR і не повинен вгадуватися. Первинний ключ некластерний, щоб випадкові значення не фрагментували таблицю.
- `TicketTypes.Id` без `IDENTITY`: значення приходить із Каталогу.
- `RowVersion` у `Orders`, `Customers`, `Events`: два одночасні редагування не затруть зміни одне одного. Переходи статусу додатково захищені `UPDLOCK` у процедурах.
