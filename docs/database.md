# CampusTix — будова баз даних

Контексти, політика дублювання, ERD і розгортання описані в кореневому [README](../README.md).

## Агрегати

| Контекст | Агрегат | Життєвий цикл |
|---|---|---|
| Замовлення | **Order** → OrderItem, OrderDetails, OrderStatusHistory, Ticket, PaymentRecord | Submitted → AwaitingValidation → StockConfirmed → Paid → Shipped; Cancelled лише до Paid |
| Замовлення | **Customer** | створюється при реєстрації |
| Каталог | **Event** → EventDetail, EventCategory, TicketType | Draft → Published → Cancelled |
| Каталог | **Category**, **Venue** | довідники |
| Відгуки | **Review** → comments[] | створення, коментування, редагування з version |
| Відгуки | **Discussion** → messages | відкрите / закрите |

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
