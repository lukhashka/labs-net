-- CampusTix · OrdersDb · verification queries from "check yourself". Run after schema -> procedures -> seed.
-- Everything that changes data is rolled back, so the script can be repeated.
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;

PRINT '--- 1. Row counts (repeated seed must not change them)';
SELECT (SELECT COUNT(*) FROM dbo.Customers) AS Customers, (SELECT COUNT(*) FROM dbo.Orders) AS Orders,
       (SELECT COUNT(*) FROM dbo.OrderDetails) AS OrderDetails, (SELECT COUNT(*) FROM dbo.TicketTypes) AS TicketTypes,
       (SELECT COUNT(*) FROM dbo.OrderItems) AS OrderItems, (SELECT COUNT(*) FROM dbo.OrderStatusHistory) AS History,
       (SELECT COUNT(*) FROM dbo.PaymentRecords) AS Payments, (SELECT COUNT(*) FROM dbo.Tickets) AS Tickets;

PRINT '--- 2. Foreign keys: all of them point inside this database';
SELECT fk.name AS ForeignKey, OBJECT_NAME(fk.parent_object_id) AS FromTable, OBJECT_NAME(fk.referenced_object_id) AS ToTable
FROM sys.foreign_keys fk ORDER BY FromTable, ForeignKey;

PRINT '--- 3. Search + pagination (uses IX_Orders_CustomerId_Status)';
SELECT Id, CustomerId, Status, TotalAmount FROM dbo.Orders
WHERE CustomerId = 1 ORDER BY OrderDate DESC OFFSET 0 ROWS FETCH NEXT 10 ROWS ONLY;

PRINT '--- 4. CRUD with a name containing an apostrophe (rolled back)';
BEGIN TRANSACTION;
    DECLARE @Name NVARCHAR(200) = N'Conan O''Malley';
    INSERT dbo.Customers (Email, FullName) VALUES (N'conan@chnu.edu.ua', @Name);
    UPDATE dbo.Customers SET Phone = N'+380000000000', UpdatedAt = SYSUTCDATETIME() WHERE FullName = @Name;
    SELECT Id, FullName, Phone FROM dbo.Customers WHERE FullName = @Name;
    DELETE dbo.Customers WHERE FullName = @Name;
ROLLBACK;

PRINT '--- 5. Disallowed transition: ship an unpaid order -> controlled error 50002';
BEGIN TRY
    EXEC dbo.usp_ChangeOrderStatus @OrderId = 1, @NewStatus = N'Shipped', @ChangedBy = N'verify';
    PRINT 'FAILED: transition was executed';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;   -- doomed by XACT_ABORT of the procedure
    SELECT ERROR_NUMBER() AS ErrorNumber, ERROR_MESSAGE() AS ErrorMessage;
END CATCH;
SELECT Id, Status FROM dbo.Orders WHERE Id = 1;

PRINT '--- 6. Order not found -> controlled error 50001';
BEGIN TRY
    EXEC dbo.usp_ConfirmOrder @OrderId = 999999;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;   -- doomed by XACT_ABORT of the procedure
    SELECT ERROR_NUMBER() AS ErrorNumber, ERROR_MESSAGE() AS ErrorMessage;
END CATCH;

PRINT '--- 7. Tickets for an unpaid order -> controlled error 50002';
BEGIN TRY
    EXEC dbo.usp_IssueTickets @OrderId = 2;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;   -- doomed by XACT_ABORT of the procedure
    SELECT ERROR_NUMBER() AS ErrorNumber, ERROR_MESSAGE() AS ErrorMessage;
END CATCH;

PRINT '--- 8. Happy path Submitted -> StockConfirmed -> Paid -> Shipped, then rollback';
BEGIN TRANSACTION;
    EXEC dbo.usp_ConfirmOrder @OrderId = 1, @ChangedBy = N'verify';
    EXEC dbo.usp_RegisterPayment @OrderId = 1, @Amount = 700.00, @Method = N'Card', @ExternalRef = N'VERIFY-1';
    EXEC dbo.usp_IssueTickets @OrderId = 1;
    SELECT o.Status, (SELECT COUNT(*) FROM dbo.Tickets t WHERE t.OrderId = o.Id) AS TicketsIssued
    FROM dbo.Orders o WHERE o.Id = 1;
ROLLBACK;
SELECT Id, Status FROM dbo.Orders WHERE Id = 1;

PRINT '--- 9. Cancel returns stock only if it was written off (rolled back)';
BEGIN TRANSACTION;
    SELECT Stock AS StockBefore FROM dbo.TicketTypes WHERE Id = 4;
    EXEC dbo.usp_CancelOrder @OrderId = 2, @ChangedBy = N'verify';
    SELECT Stock AS StockAfter FROM dbo.TicketTypes WHERE Id = 4;
ROLLBACK;

PRINT '--- 10. CHECK and UNIQUE constraints are active';
BEGIN TRY
    INSERT dbo.TicketTypes (Id, EventId, EventTitle, EventStartsAt, Name, Price) VALUES (100, 1, N't', SYSUTCDATETIME(), N'x', -1);
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;   -- doomed by XACT_ABORT of the procedure
    SELECT ERROR_NUMBER() AS ErrorNumber, ERROR_MESSAGE() AS ErrorMessage;
END CATCH;
BEGIN TRY
    INSERT dbo.Customers (Email, FullName) VALUES (N'olena.koval@chnu.edu.ua', N'dup');
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;   -- doomed by XACT_ABORT of the procedure
    SELECT ERROR_NUMBER() AS ErrorNumber, ERROR_MESSAGE() AS ErrorMessage;
END CATCH;
