SET QUOTED_IDENTIFIER ON;   -- required by the filtered index; sqlcmd has it OFF by default
GO

-- CampusTix · OrdersDb · stored procedures (6)
-- Business errors have their own numbers, DAL maps them to domain exceptions (Lab 02, B.5):
--   THROW 50001 -> NotFoundException (404),  THROW 50002 -> BusinessConflictException (409).
-- XACT_ABORT ON: any error rolls the whole transaction back.
-- UPDLOCK: the order row stays locked until the end of the transaction, two parallel transitions are serialized.

CREATE OR ALTER PROCEDURE dbo.usp_GetOrderWithItems
    @OrderId BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT o.Id, o.CustomerId, o.Status, o.OrderDate, o.TotalAmount,
           i.OrderId, i.TicketTypeId, t.EventId, i.EventTitle, i.TicketTypeName, i.UnitPrice, i.Units
    FROM dbo.Orders o
    LEFT JOIN dbo.OrderItems i ON i.OrderId = o.Id
    LEFT JOIN dbo.TicketTypes t ON t.Id = i.TicketTypeId
    WHERE o.Id = @OrderId AND o.IsDeleted = 0;
END
GO

-- Submitted/AwaitingValidation -> StockConfirmed. Tickets are written off by the service in the same transaction.
CREATE OR ALTER PROCEDURE dbo.usp_ConfirmOrder
    @OrderId BIGINT,
    @ChangedBy NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    DECLARE @Status NVARCHAR(30) =
        (SELECT Status FROM dbo.Orders WITH (UPDLOCK) WHERE Id = @OrderId AND IsDeleted = 0);

    IF @Status IS NULL
        THROW 50001, 'Order not found', 1;
    IF @Status NOT IN (N'Submitted', N'AwaitingValidation')
        THROW 50002, 'Stock can be confirmed only for a new order', 1;

    UPDATE dbo.Orders
    SET Status = N'StockConfirmed', UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @ChangedBy
    WHERE Id = @OrderId;

    INSERT dbo.OrderStatusHistory (OrderId, FromStatus, ToStatus, ChangedBy)
    VALUES (@OrderId, @Status, N'StockConfirmed', @ChangedBy);

    COMMIT TRANSACTION;
END
GO

-- Transitions without side effects. Transitions with side effects have their own procedures:
-- -> StockConfirmed (usp_ConfirmOrder + write-off), -> Cancelled (usp_CancelOrder + stock return).
CREATE OR ALTER PROCEDURE dbo.usp_ChangeOrderStatus
    @OrderId BIGINT,
    @NewStatus NVARCHAR(30),
    @ChangedBy NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    DECLARE @Status NVARCHAR(30) =
        (SELECT Status FROM dbo.Orders WITH (UPDLOCK) WHERE Id = @OrderId AND IsDeleted = 0);

    IF @Status IS NULL
        THROW 50001, 'Order not found', 1;

    IF NOT EXISTS (SELECT 1 FROM (VALUES
            (N'Submitted',      N'AwaitingValidation'),
            (N'StockConfirmed', N'Paid'),
            (N'Paid',           N'Shipped')) AS allowed (FromStatus, ToStatus)
        WHERE allowed.FromStatus = @Status AND allowed.ToStatus = @NewStatus)
    BEGIN
        DECLARE @Message NVARCHAR(200) = CONCAT(N'Transition ', @Status, N' -> ', @NewStatus, N' is not allowed');
        THROW 50002, @Message, 1;
    END

    UPDATE dbo.Orders
    SET Status = @NewStatus, UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @ChangedBy
    WHERE Id = @OrderId;

    INSERT dbo.OrderStatusHistory (OrderId, FromStatus, ToStatus, ChangedBy)
    VALUES (@OrderId, @Status, @NewStatus, @ChangedBy);

    COMMIT TRANSACTION;
END
GO

-- StockConfirmed -> Paid + PaymentRecords. A repeated ExternalRef violates UNIQUE (2627) -> 409.
CREATE OR ALTER PROCEDURE dbo.usp_RegisterPayment
    @OrderId BIGINT,
    @Amount DECIMAL(18,2),
    @Method NVARCHAR(30),
    @ExternalRef NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    DECLARE @Status NVARCHAR(30), @Total DECIMAL(18,2);
    SELECT @Status = Status, @Total = TotalAmount
    FROM dbo.Orders WITH (UPDLOCK) WHERE Id = @OrderId AND IsDeleted = 0;

    IF @Status IS NULL
        THROW 50001, 'Order not found', 1;
    IF @Status <> N'StockConfirmed'
        THROW 50002, 'Only an order with confirmed stock can be paid', 1;
    IF @Amount <> @Total
        THROW 50002, 'Payment amount does not match the order total', 1;

    INSERT dbo.PaymentRecords (OrderId, Amount, Method, ExternalRef)
    VALUES (@OrderId, @Amount, @Method, @ExternalRef);

    UPDATE dbo.Orders SET Status = N'Paid', UpdatedAt = SYSUTCDATETIME() WHERE Id = @OrderId;

    INSERT dbo.OrderStatusHistory (OrderId, FromStatus, ToStatus, ChangedBy)
    VALUES (@OrderId, @Status, N'Paid', N'payment');

    COMMIT TRANSACTION;
END
GO

-- Paid -> Shipped + one row in Tickets per unit. Refuses an unpaid order.
CREATE OR ALTER PROCEDURE dbo.usp_IssueTickets
    @OrderId BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    DECLARE @Status NVARCHAR(30) =
        (SELECT Status FROM dbo.Orders WITH (UPDLOCK) WHERE Id = @OrderId AND IsDeleted = 0);

    IF @Status IS NULL
        THROW 50001, 'Order not found', 1;
    IF @Status <> N'Paid'
        THROW 50002, 'Tickets can be issued only for a paid order', 1;

    INSERT dbo.Tickets (OrderId, TicketTypeId, HolderName)
    SELECT i.OrderId, i.TicketTypeId, COALESCE(d.AttendeeName, c.FullName)
    FROM dbo.OrderItems i
    JOIN dbo.Orders o ON o.Id = i.OrderId
    JOIN dbo.Customers c ON c.Id = o.CustomerId
    LEFT JOIN dbo.OrderDetails d ON d.OrderId = i.OrderId
    CROSS APPLY (SELECT TOP (i.Units) 1 AS n FROM sys.all_objects) AS units
    WHERE i.OrderId = @OrderId;

    UPDATE dbo.Orders SET Status = N'Shipped', UpdatedAt = SYSUTCDATETIME() WHERE Id = @OrderId;

    INSERT dbo.OrderStatusHistory (OrderId, FromStatus, ToStatus, ChangedBy)
    VALUES (@OrderId, @Status, N'Shipped', N'tickets');

    COMMIT TRANSACTION;
END
GO

-- Cancel is allowed only before payment. If tickets were already written off, they return to stock.
CREATE OR ALTER PROCEDURE dbo.usp_CancelOrder
    @OrderId BIGINT,
    @ChangedBy NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    DECLARE @Status NVARCHAR(30) =
        (SELECT Status FROM dbo.Orders WITH (UPDLOCK) WHERE Id = @OrderId AND IsDeleted = 0);

    IF @Status IS NULL
        THROW 50001, 'Order not found', 1;
    IF @Status NOT IN (N'Submitted', N'AwaitingValidation', N'StockConfirmed')
        THROW 50002, 'Order can be cancelled only before payment', 1;

    IF @Status = N'StockConfirmed'
        UPDATE t SET t.Stock = t.Stock + i.Units
        FROM dbo.TicketTypes t
        JOIN dbo.OrderItems i ON i.TicketTypeId = t.Id
        WHERE i.OrderId = @OrderId;

    UPDATE dbo.Orders
    SET Status = N'Cancelled', UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @ChangedBy
    WHERE Id = @OrderId;

    INSERT dbo.OrderStatusHistory (OrderId, FromStatus, ToStatus, ChangedBy)
    VALUES (@OrderId, @Status, N'Cancelled', @ChangedBy);

    COMMIT TRANSACTION;
END
GO
