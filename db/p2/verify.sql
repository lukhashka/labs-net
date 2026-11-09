-- CampusTix · CatalogDb · verification queries
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;

PRINT '--- 1. Row counts (repeated seed must not change them)';
SELECT (SELECT COUNT(*) FROM dbo.Venues) AS Venues, (SELECT COUNT(*) FROM dbo.Categories) AS Categories,
       (SELECT COUNT(*) FROM dbo.Events) AS Events, (SELECT COUNT(*) FROM dbo.EventDetails) AS EventDetails,
       (SELECT COUNT(*) FROM dbo.EventCategories) AS EventCategories, (SELECT COUNT(*) FROM dbo.TicketTypes) AS TicketTypes;

PRINT '--- 2. Foreign keys: all of them point inside this database';
SELECT fk.name AS ForeignKey, OBJECT_NAME(fk.parent_object_id) AS FromTable, OBJECT_NAME(fk.referenced_object_id) AS ToTable
FROM sys.foreign_keys fk ORDER BY FromTable, ForeignKey;

PRINT '--- 3. M:N with its own field: recently added to the category';
SELECT e.Id, e.Title, ec.AddedAt FROM dbo.EventCategories ec
JOIN dbo.Events e ON e.Id = ec.EventId WHERE ec.CategoryId = 2 ORDER BY ec.AddedAt DESC;

PRINT '--- 4. Pagination of ticket types by price';
SELECT Id, Name, Price FROM dbo.TicketTypes ORDER BY Price DESC, Id OFFSET 2 ROWS FETCH NEXT 3 ROWS ONLY;

PRINT '--- 5. CHECK constraint: price must be positive';
BEGIN TRY
    INSERT dbo.TicketTypes (EventId, Name, Code, Price, Capacity, AvailableStock) VALUES (1, N'x', N'BAD-PRICE', -5, 1, 1);
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;   -- doomed by XACT_ABORT of the procedure
    SELECT ERROR_NUMBER() AS ErrorNumber, ERROR_MESSAGE() AS ErrorMessage;
END CATCH;
