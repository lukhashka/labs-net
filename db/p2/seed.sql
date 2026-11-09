SET QUOTED_IDENTIFIER ON;   -- required by the filtered index; sqlcmd has it OFF by default
GO

-- CampusTix · CatalogDb · reference and test data. Idempotent. Ids match OrdersDb.TicketTypes and reviews.eventId.
SET NOCOUNT ON;

SET IDENTITY_INSERT dbo.Venues ON;
INSERT dbo.Venues (Id, Name, Address, Capacity)
SELECT v.Id, v.Name, v.Address, v.Capacity
FROM (VALUES
    (1, N'Актова зала ЧНУ (корпус 1)', N'вул. Коцюбинського, 2',              400),
    (2, N'Спорткомплекс ЧНУ',          N'вул. Університетська, 28',          1200),
    (3, N'Коворкінг ФМІ',              N'вул. Університетська, 28, ауд. 25',   80)
) AS v (Id, Name, Address, Capacity)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Venues x WHERE x.Id = v.Id);
SET IDENTITY_INSERT dbo.Venues OFF;
GO

SET IDENTITY_INSERT dbo.Categories ON;
INSERT dbo.Categories (Id, Name)
SELECT v.Id, v.Name
FROM (VALUES (1, N'Концерт'), (2, N'Хакатон'), (3, N'Лекція'), (4, N'Спорт'), (5, N'Вечірка'), (6, N'Воркшоп')) AS v (Id, Name)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Categories x WHERE x.Id = v.Id);
SET IDENTITY_INSERT dbo.Categories OFF;
GO

SET IDENTITY_INSERT dbo.Events ON;
INSERT dbo.Events (Id, Title, Slug, StartsAt, EndsAt, VenueId, CreatedBy)
SELECT v.Id, v.Title, v.Slug, v.StartsAt, v.EndsAt, v.VenueId, N'seed'
FROM (VALUES
    (1, N'Осінній концерт ЧНУ',                  N'osinnii-kontsert-2026', CAST('2026-11-14T18:00:00' AS DATETIME2), CAST('2026-11-14T21:00:00' AS DATETIME2), 1),
    (2, N'CampusHack 2026',                      N'campushack-2026',       CAST('2026-11-21T09:00:00' AS DATETIME2), CAST('2026-11-22T18:00:00' AS DATETIME2), 3),
    (3, N'Відкрита лекція: EF Core на практиці', N'lecture-ef-core',       CAST('2026-11-05T14:00:00' AS DATETIME2), CAST('2026-11-05T15:30:00' AS DATETIME2), 1),
    (4, N'Кубок факультетів з волейболу',        N'volleyball-cup-2026',   CAST('2026-12-03T16:00:00' AS DATETIME2), CAST('2026-12-03T20:00:00' AS DATETIME2), 2)
) AS v (Id, Title, Slug, StartsAt, EndsAt, VenueId)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Events x WHERE x.Id = v.Id);
SET IDENTITY_INSERT dbo.Events OFF;
GO

INSERT dbo.EventDetails (EventId, Description, Organizer, AgeLimit)
SELECT v.EventId, v.Description, v.Organizer, v.AgeLimit
FROM (VALUES
    (1, N'Студентський концерт до дня університету',            N'Студрада ЧНУ', 0),
    (2, N'24-годинний хакатон для студентів ФМІ',               N'Кафедра КН',  16),
    (3, N'Code First, міграції, завантаження пов''язаних даних', N'Кафедра КН',  0)
) AS v (EventId, Description, Organizer, AgeLimit)
WHERE NOT EXISTS (SELECT 1 FROM dbo.EventDetails x WHERE x.EventId = v.EventId);
GO

INSERT dbo.EventCategories (EventId, CategoryId)
SELECT v.EventId, v.CategoryId
FROM (VALUES (1, 1), (1, 5), (2, 2), (2, 6), (3, 3), (4, 4)) AS v (EventId, CategoryId)
WHERE NOT EXISTS (SELECT 1 FROM dbo.EventCategories x WHERE x.EventId = v.EventId AND x.CategoryId = v.CategoryId);
GO

SET IDENTITY_INSERT dbo.TicketTypes ON;
INSERT dbo.TicketTypes (Id, EventId, Name, Code, Price, Capacity, AvailableStock)
SELECT v.Id, v.EventId, v.Name, v.Code, v.Price, v.Capacity, v.Capacity
FROM (VALUES
    (1, 1, N'Standard',    N'CONC26-STD',   150.00, 300),
    (2, 1, N'VIP',         N'CONC26-VIP',   400.00,  50),
    (3, 1, N'Student',     N'CONC26-STU',    80.00,  50),
    (4, 2, N'Participant', N'HACK26-PRT',    50.00,  60),
    (5, 2, N'Guest',       N'HACK26-GST',    20.00,  20),
    (6, 3, N'Free',        N'LECT-EF-FREE',   1.00, 120),
    (7, 4, N'Standard',    N'VOLL26-STD',    30.00, 800),
    (8, 4, N'Student',     N'VOLL26-STU',    15.00, 400)
) AS v (Id, EventId, Name, Code, Price, Capacity)
WHERE NOT EXISTS (SELECT 1 FROM dbo.TicketTypes x WHERE x.Id = v.Id);
SET IDENTITY_INSERT dbo.TicketTypes OFF;
GO
