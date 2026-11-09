SET QUOTED_IDENTIFIER ON;   -- required by the filtered index; sqlcmd has it OFF by default
GO

-- CampusTix · OrdersDb (Project #1, Lab 01 -> Lab 02)
-- Idempotent: can be executed repeatedly. Tables are created only if they do not exist yet.
-- Keys: BIGINT IDENTITY - all inserts come from one source (Orders service), sequential keys are cheap
-- for the clustered index. Exceptions: Tickets.Id is a GUID (shown as QR, must not be guessable)
-- and ProcessedEvents.EventId (comes from outside, Lab 08).

IF OBJECT_ID(N'dbo.Customers', N'U') IS NULL
CREATE TABLE dbo.Customers (
    Id             BIGINT IDENTITY(1,1) CONSTRAINT PK_Customers PRIMARY KEY,
    Email          NVARCHAR(256) NOT NULL CONSTRAINT UQ_Customers_Email UNIQUE,
    FullName       NVARCHAR(200) NOT NULL,
    Phone          NVARCHAR(30)  NULL,
    ExternalUserId NVARCHAR(100) NULL,                 -- "sub" of the user from Auth (Lab 09)
    CreatedAt      DATETIME2     NOT NULL CONSTRAINT DF_Customers_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy      NVARCHAR(100) NULL,
    UpdatedAt      DATETIME2     NULL,
    UpdatedBy      NVARCHAR(100) NULL,
    IsDeleted      BIT           NOT NULL CONSTRAINT DF_Customers_IsDeleted DEFAULT 0,
    RowVersion     ROWVERSION
);
GO

-- 1:N Customers -> Orders. No cascade: a customer with order history must not disappear.
IF OBJECT_ID(N'dbo.Orders', N'U') IS NULL
CREATE TABLE dbo.Orders (
    Id          BIGINT IDENTITY(1,1) CONSTRAINT PK_Orders PRIMARY KEY,
    CustomerId  BIGINT        NOT NULL CONSTRAINT FK_Orders_Customers REFERENCES dbo.Customers(Id),
    Status      NVARCHAR(30)  NOT NULL CONSTRAINT DF_Orders_Status DEFAULT N'Submitted'
                CONSTRAINT CK_Orders_Status CHECK (Status IN (N'Submitted', N'AwaitingValidation',
                    N'StockConfirmed', N'Paid', N'Shipped', N'Cancelled')),   -- as OrderStatus in eShop
    OrderDate   DATETIME2     NOT NULL CONSTRAINT DF_Orders_OrderDate DEFAULT SYSUTCDATETIME(),
    TotalAmount DECIMAL(18,2) NOT NULL CONSTRAINT DF_Orders_TotalAmount DEFAULT 0
                CONSTRAINT CK_Orders_TotalAmount CHECK (TotalAmount >= 0),
    CreatedAt   DATETIME2     NOT NULL CONSTRAINT DF_Orders_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy   NVARCHAR(100) NULL,
    UpdatedAt   DATETIME2     NULL,
    UpdatedBy   NVARCHAR(100) NULL,
    IsDeleted   BIT           NOT NULL CONSTRAINT DF_Orders_IsDeleted DEFAULT 0,
    RowVersion  ROWVERSION                                            -- optimistic concurrency
);
GO

-- 1:1 Orders <-> OrderDetails: the FK is the PK of the dependent table
IF OBJECT_ID(N'dbo.OrderDetails', N'U') IS NULL
CREATE TABLE dbo.OrderDetails (
    OrderId       BIGINT         NOT NULL CONSTRAINT PK_OrderDetails PRIMARY KEY
                  CONSTRAINT FK_OrderDetails_Orders REFERENCES dbo.Orders(Id) ON DELETE CASCADE,
    AttendeeName  NVARCHAR(200)  NOT NULL,
    AttendeeEmail NVARCHAR(256)  NOT NULL,
    Comment       NVARCHAR(1000) NULL
);
GO

-- Local read copy of the Catalog reference data. Id = Id in CatalogDb, no IDENTITY and no FK to the other DB.
-- Name/Price/EventTitle/EventStartsAt are updated by events (Lab 08); Stock is owned by this service.
IF OBJECT_ID(N'dbo.TicketTypes', N'U') IS NULL
CREATE TABLE dbo.TicketTypes (
    Id              BIGINT        NOT NULL CONSTRAINT PK_TicketTypes PRIMARY KEY,
    EventId         BIGINT        NOT NULL,                           -- logical reference to CatalogDb
    EventTitle      NVARCHAR(200) NOT NULL,
    EventStartsAt   DATETIME2     NOT NULL,
    Name            NVARCHAR(50)  NOT NULL,
    Price           DECIMAL(18,2) NOT NULL CONSTRAINT CK_TicketTypes_Price CHECK (Price > 0),
    Stock           INT           NOT NULL CONSTRAINT DF_TicketTypes_Stock DEFAULT 0
                    CONSTRAINT CK_TicketTypes_Stock CHECK (Stock >= 0),
    SourceUpdatedAt DATETIME2     NULL
);
GO

-- M:N Orders <-> TicketTypes. EventTitle/TicketTypeName/UnitPrice are a snapshot at order time, never updated.
IF OBJECT_ID(N'dbo.OrderItems', N'U') IS NULL
CREATE TABLE dbo.OrderItems (
    OrderId        BIGINT        NOT NULL CONSTRAINT FK_OrderItems_Orders REFERENCES dbo.Orders(Id) ON DELETE CASCADE,
    TicketTypeId   BIGINT        NOT NULL CONSTRAINT FK_OrderItems_TicketTypes REFERENCES dbo.TicketTypes(Id),
    EventTitle     NVARCHAR(200) NOT NULL,
    TicketTypeName NVARCHAR(50)  NOT NULL,
    UnitPrice      DECIMAL(18,2) NOT NULL CONSTRAINT CK_OrderItems_UnitPrice CHECK (UnitPrice > 0),
    Units          INT           NOT NULL CONSTRAINT CK_OrderItems_Units CHECK (Units > 0),
    CONSTRAINT PK_OrderItems PRIMARY KEY (OrderId, TicketTypeId)
);
GO

IF OBJECT_ID(N'dbo.OrderStatusHistory', N'U') IS NULL
CREATE TABLE dbo.OrderStatusHistory (
    Id         BIGINT IDENTITY(1,1) CONSTRAINT PK_OrderStatusHistory PRIMARY KEY,
    OrderId    BIGINT        NOT NULL CONSTRAINT FK_OrderStatusHistory_Orders REFERENCES dbo.Orders(Id) ON DELETE CASCADE,
    FromStatus NVARCHAR(30)  NULL,
    ToStatus   NVARCHAR(30)  NOT NULL,
    ChangedAt  DATETIME2     NOT NULL CONSTRAINT DF_OrderStatusHistory_ChangedAt DEFAULT SYSUTCDATETIME(),
    ChangedBy  NVARCHAR(100) NULL
);
GO

IF OBJECT_ID(N'dbo.PaymentRecords', N'U') IS NULL
CREATE TABLE dbo.PaymentRecords (
    Id          BIGINT IDENTITY(1,1) CONSTRAINT PK_PaymentRecords PRIMARY KEY,
    OrderId     BIGINT        NOT NULL CONSTRAINT FK_PaymentRecords_Orders REFERENCES dbo.Orders(Id),
    Amount      DECIMAL(18,2) NOT NULL CONSTRAINT CK_PaymentRecords_Amount CHECK (Amount > 0),
    Method      NVARCHAR(30)  NOT NULL,
    ExternalRef NVARCHAR(100) NOT NULL CONSTRAINT UQ_PaymentRecords_ExternalRef UNIQUE,   -- idempotency of a payment
    PaidAt      DATETIME2     NOT NULL CONSTRAINT DF_PaymentRecords_PaidAt DEFAULT SYSUTCDATETIME()
);
GO

IF OBJECT_ID(N'dbo.Tickets', N'U') IS NULL
CREATE TABLE dbo.Tickets (
    Id           UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Tickets PRIMARY KEY NONCLUSTERED
                 CONSTRAINT DF_Tickets_Id DEFAULT NEWID(),
    OrderId      BIGINT        NOT NULL CONSTRAINT FK_Tickets_Orders REFERENCES dbo.Orders(Id),
    TicketTypeId BIGINT        NOT NULL CONSTRAINT FK_Tickets_TicketTypes REFERENCES dbo.TicketTypes(Id),
    HolderName   NVARCHAR(200) NOT NULL,
    IssuedAt     DATETIME2     NOT NULL CONSTRAINT DF_Tickets_IssuedAt DEFAULT SYSUTCDATETIME(),
    CheckedInAt  DATETIME2     NULL
);
GO

-- Idempotency of integration events (Lab 08)
IF OBJECT_ID(N'dbo.ProcessedEvents', N'U') IS NULL
CREATE TABLE dbo.ProcessedEvents (
    EventId     UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ProcessedEvents PRIMARY KEY,
    ProcessedAt DATETIME2        NOT NULL CONSTRAINT DF_ProcessedEvents_ProcessedAt DEFAULT SYSUTCDATETIME()
);
GO

-- Indexes. Every index exists for a specific query.

-- "My orders with a given status" (screen "My tickets")
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Orders_CustomerId_Status')
    CREATE INDEX IX_Orders_CustomerId_Status ON dbo.Orders (CustomerId, Status);
GO
-- Filtered: list of live orders of a customer, deleted rows do not bloat the index
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Orders_Active')
    CREATE INDEX IX_Orders_Active ON dbo.Orders (CustomerId) WHERE IsDeleted = 0;
GO
-- Covering: "available tickets of an event" (Aggregator) is answered from the index alone
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_TicketTypes_EventId')
    CREATE INDEX IX_TicketTypes_EventId ON dbo.TicketTypes (EventId) INCLUDE (Name, Price, Stock);
GO
-- Tickets issued for an order
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Tickets_OrderId')
    CREATE INDEX IX_Tickets_OrderId ON dbo.Tickets (OrderId);
GO
-- Status timeline of an order
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OrderStatusHistory_OrderId')
    CREATE INDEX IX_OrderStatusHistory_OrderId ON dbo.OrderStatusHistory (OrderId, ChangedAt);
GO

-- Denormalized read view: order list without aggregating in the application
CREATE OR ALTER VIEW dbo.v_OrderSummary AS
SELECT o.Id, o.Status, o.OrderDate, o.TotalAmount, c.Id AS CustomerId, c.FullName, c.Email,
       (SELECT COALESCE(SUM(i.Units), 0) FROM dbo.OrderItems i WHERE i.OrderId = o.Id) AS TicketsCount
FROM dbo.Orders o
JOIN dbo.Customers c ON c.Id = o.CustomerId
WHERE o.IsDeleted = 0;
GO
