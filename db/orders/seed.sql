SET QUOTED_IDENTIFIER ON;   -- required by the filtered index; sqlcmd has it OFF by default
GO

-- CampusTix · OrdersDb · test data. Idempotent: a repeated run does not create duplicates (WHERE NOT EXISTS by Id).
-- TicketTypes.Id and EventId match Catalog seed data (logical references, no FK).

SET NOCOUNT ON;

SET IDENTITY_INSERT dbo.Customers ON;
INSERT dbo.Customers (Id, Email, FullName, Phone, CreatedBy)
SELECT v.Id, v.Email, v.FullName, v.Phone, N'seed'
FROM (VALUES
    (1, N'olena.koval@chnu.edu.ua',   N'Олена Коваль',   N'+380501112233'),
    (2, N'sean.obrien@chnu.edu.ua',   N'Sean O''Brien',  NULL),
    (3, N'andrii.melnyk@chnu.edu.ua', N'Андрій Мельник', N'+380674445566')
) AS v (Id, Email, FullName, Phone)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Customers c WHERE c.Id = v.Id);
SET IDENTITY_INSERT dbo.Customers OFF;
GO

INSERT dbo.TicketTypes (Id, EventId, EventTitle, EventStartsAt, Name, Price, Stock)
SELECT v.Id, v.EventId, v.EventTitle, v.EventStartsAt, v.Name, v.Price, v.Stock
FROM (VALUES
    (1, 1, N'Осінній концерт ЧНУ',                  CAST('2026-11-14T18:00:00' AS DATETIME2), N'Standard',    150.00, 300),
    (2, 1, N'Осінній концерт ЧНУ',                  CAST('2026-11-14T18:00:00' AS DATETIME2), N'VIP',         400.00,  50),
    (3, 1, N'Осінній концерт ЧНУ',                  CAST('2026-11-14T18:00:00' AS DATETIME2), N'Student',      80.00,  50),
    (4, 2, N'CampusHack 2026',                      CAST('2026-11-21T09:00:00' AS DATETIME2), N'Participant',  50.00,  59),
    (5, 2, N'CampusHack 2026',                      CAST('2026-11-21T09:00:00' AS DATETIME2), N'Guest',        20.00,  20),
    (6, 3, N'Відкрита лекція: EF Core на практиці', CAST('2026-11-05T14:00:00' AS DATETIME2), N'Free',          1.00, 119),
    (7, 4, N'Кубок факультетів з волейболу',        CAST('2026-12-03T16:00:00' AS DATETIME2), N'Standard',     30.00, 798),
    (8, 4, N'Кубок факультетів з волейболу',        CAST('2026-12-03T16:00:00' AS DATETIME2), N'Student',      15.00, 400)
) AS v (Id, EventId, EventTitle, EventStartsAt, Name, Price, Stock)
WHERE NOT EXISTS (SELECT 1 FROM dbo.TicketTypes t WHERE t.Id = v.Id);
GO

SET IDENTITY_INSERT dbo.Orders ON;
INSERT dbo.Orders (Id, CustomerId, Status, OrderDate, TotalAmount, CreatedBy)
SELECT v.Id, v.CustomerId, v.Status, v.OrderDate, v.TotalAmount, N'seed'
FROM (VALUES
    (1, 1, N'Submitted',      CAST('2026-10-20T10:00:00' AS DATETIME2), 700.00),
    (2, 2, N'StockConfirmed', CAST('2026-10-21T11:30:00' AS DATETIME2),  50.00),
    (3, 1, N'Paid',           CAST('2026-10-22T09:15:00' AS DATETIME2),  60.00),
    (4, 3, N'Shipped',        CAST('2026-10-23T16:45:00' AS DATETIME2),   1.00),
    (5, 2, N'Cancelled',      CAST('2026-10-24T12:00:00' AS DATETIME2),  80.00)
) AS v (Id, CustomerId, Status, OrderDate, TotalAmount)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Orders o WHERE o.Id = v.Id);
SET IDENTITY_INSERT dbo.Orders OFF;
GO

INSERT dbo.OrderDetails (OrderId, AttendeeName, AttendeeEmail, Comment)
SELECT v.OrderId, v.AttendeeName, v.AttendeeEmail, v.Comment
FROM (VALUES
    (1, N'Олена Коваль',   N'olena.koval@chnu.edu.ua',   N'Місця поруч, будь ласка'),
    (2, N'Sean O''Brien',  N'sean.obrien@chnu.edu.ua',   NULL),
    (3, N'Олена Коваль',   N'olena.koval@chnu.edu.ua',   NULL),
    (4, N'Андрій Мельник', N'andrii.melnyk@chnu.edu.ua', NULL)
) AS v (OrderId, AttendeeName, AttendeeEmail, Comment)
WHERE NOT EXISTS (SELECT 1 FROM dbo.OrderDetails d WHERE d.OrderId = v.OrderId);
GO

INSERT dbo.OrderItems (OrderId, TicketTypeId, EventTitle, TicketTypeName, UnitPrice, Units)
SELECT v.OrderId, v.TicketTypeId, v.EventTitle, v.TicketTypeName, v.UnitPrice, v.Units
FROM (VALUES
    (1, 1, N'Осінній концерт ЧНУ',                  N'Standard',    150.00, 2),
    (1, 2, N'Осінній концерт ЧНУ',                  N'VIP',         400.00, 1),
    (2, 4, N'CampusHack 2026',                      N'Participant',  50.00, 1),
    (3, 7, N'Кубок факультетів з волейболу',        N'Standard',     30.00, 2),
    (4, 6, N'Відкрита лекція: EF Core на практиці', N'Free',          1.00, 1),
    (5, 3, N'Осінній концерт ЧНУ',                  N'Student',      80.00, 1)
) AS v (OrderId, TicketTypeId, EventTitle, TicketTypeName, UnitPrice, Units)
WHERE NOT EXISTS (SELECT 1 FROM dbo.OrderItems i WHERE i.OrderId = v.OrderId AND i.TicketTypeId = v.TicketTypeId);
GO

INSERT dbo.OrderStatusHistory (OrderId, FromStatus, ToStatus, ChangedBy)
SELECT v.OrderId, v.FromStatus, v.ToStatus, N'seed'
FROM (VALUES
    (2, N'Submitted', N'StockConfirmed'),
    (3, N'StockConfirmed', N'Paid'),
    (4, N'Paid', N'Shipped'),
    (5, N'Submitted', N'Cancelled')
) AS v (OrderId, FromStatus, ToStatus)
WHERE NOT EXISTS (SELECT 1 FROM dbo.OrderStatusHistory h WHERE h.OrderId = v.OrderId AND h.ToStatus = v.ToStatus);
GO

INSERT dbo.PaymentRecords (OrderId, Amount, Method, ExternalRef)
SELECT v.OrderId, v.Amount, v.Method, v.ExternalRef
FROM (VALUES
    (3, 60.00, N'Card', N'SEED-PAY-0003'),
    (4,  1.00, N'Card', N'SEED-PAY-0004')
) AS v (OrderId, Amount, Method, ExternalRef)
WHERE NOT EXISTS (SELECT 1 FROM dbo.PaymentRecords p WHERE p.ExternalRef = v.ExternalRef);
GO

INSERT dbo.Tickets (Id, OrderId, TicketTypeId, HolderName)
SELECT v.Id, v.OrderId, v.TicketTypeId, v.HolderName
FROM (VALUES
    (CAST('7d2f6c1e-3b1a-4a55-9c1b-0a1f4b6e0004' AS UNIQUEIDENTIFIER), 4, 6, N'Андрій Мельник')
) AS v (Id, OrderId, TicketTypeId, HolderName)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Tickets t WHERE t.Id = v.Id);
GO
