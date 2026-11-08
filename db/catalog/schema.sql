SET QUOTED_IDENTIFIER ON;   -- required by the filtered index; sqlcmd has it OFF by default
GO

-- CampusTix · CatalogDb (Project #2, Lab 01 design -> Lab 03 EF Core Code First)
-- The same table and constraint names are produced by the InitialCreate migration of Lab 03.
-- CreatedBy/UpdatedBy are part of the design (Lab 01, step 6); the EF model of Lab 03 does not map them yet.
-- Idempotent.

IF OBJECT_ID(N'dbo.Venues', N'U') IS NULL
CREATE TABLE dbo.Venues (
    Id       INT IDENTITY(1,1) CONSTRAINT PK_Venues PRIMARY KEY,
    Name     NVARCHAR(150) NOT NULL,
    Address  NVARCHAR(300) NOT NULL,
    Capacity INT           NOT NULL CONSTRAINT CK_Venues_Capacity CHECK (Capacity > 0)
);
GO

IF OBJECT_ID(N'dbo.Categories', N'U') IS NULL
CREATE TABLE dbo.Categories (
    Id   INT IDENTITY(1,1) CONSTRAINT PK_Categories PRIMARY KEY,
    Name NVARCHAR(100) NOT NULL
);
GO

-- 1:N Venues -> Events. No cascade: a venue with events must not disappear.
IF OBJECT_ID(N'dbo.Events', N'U') IS NULL
CREATE TABLE dbo.Events (
    Id         INT IDENTITY(1,1) CONSTRAINT PK_Events PRIMARY KEY,
    Title      NVARCHAR(200) NOT NULL,
    Slug       NVARCHAR(200) NOT NULL,
    StartsAt   DATETIME2     NOT NULL,
    EndsAt     DATETIME2     NOT NULL,
    Status     NVARCHAR(20)  NOT NULL CONSTRAINT DF_Events_Status DEFAULT N'Draft',
    CoverUrl   NVARCHAR(500) NULL,
    VenueId    INT           NOT NULL CONSTRAINT FK_Events_Venues_VenueId REFERENCES dbo.Venues(Id),
    CreatedAt  DATETIME2     NOT NULL CONSTRAINT DF_Events_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy  NVARCHAR(100) NULL,
    UpdatedAt  DATETIME2     NULL,
    UpdatedBy  NVARCHAR(100) NULL,
    IsDeleted  BIT           NOT NULL CONSTRAINT DF_Events_IsDeleted DEFAULT 0,
    RowVersion ROWVERSION,                                 -- optimistic concurrency
    CONSTRAINT CK_Events_Dates CHECK (EndsAt > StartsAt)
);
GO

-- 1:1 Events <-> EventDetails: the FK is the PK of the dependent table
IF OBJECT_ID(N'dbo.EventDetails', N'U') IS NULL
CREATE TABLE dbo.EventDetails (
    EventId     INT            NOT NULL CONSTRAINT PK_EventDetails PRIMARY KEY
                CONSTRAINT FK_EventDetails_Events_EventId REFERENCES dbo.Events(Id) ON DELETE CASCADE,
    Description NVARCHAR(4000) NOT NULL,
    Organizer   NVARCHAR(200)  NULL,
    AgeLimit    INT            NOT NULL CONSTRAINT DF_EventDetails_AgeLimit DEFAULT 0
                CONSTRAINT CK_EventDetails_AgeLimit CHECK (AgeLimit BETWEEN 0 AND 21)
);
GO

-- M:N Events <-> Categories, the junction table has its own field AddedAt
IF OBJECT_ID(N'dbo.EventCategories', N'U') IS NULL
CREATE TABLE dbo.EventCategories (
    EventId    INT       NOT NULL CONSTRAINT FK_EventCategories_Events_EventId REFERENCES dbo.Events(Id) ON DELETE CASCADE,
    CategoryId INT       NOT NULL CONSTRAINT FK_EventCategories_Categories_CategoryId REFERENCES dbo.Categories(Id),
    AddedAt    DATETIME2 NOT NULL CONSTRAINT DF_EventCategories_AddedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_EventCategories PRIMARY KEY (EventId, CategoryId)
);
GO

IF OBJECT_ID(N'dbo.TicketTypes', N'U') IS NULL
CREATE TABLE dbo.TicketTypes (
    Id             INT IDENTITY(1,1) CONSTRAINT PK_TicketTypes PRIMARY KEY,
    EventId        INT           NOT NULL CONSTRAINT FK_TicketTypes_Events_EventId REFERENCES dbo.Events(Id) ON DELETE CASCADE,
    Name           NVARCHAR(50)  NOT NULL,
    Code           NVARCHAR(50)  NOT NULL,                 -- analogue of SKU
    Price          DECIMAL(18,2) NOT NULL CONSTRAINT CK_TicketTypes_Price CHECK (Price > 0),
    Capacity       INT           NOT NULL,
    AvailableStock INT           NOT NULL,
    CONSTRAINT CK_TicketTypes_Stock CHECK (AvailableStock >= 0 AND AvailableStock <= Capacity)
);
GO

-- Natural keys
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Venues_Name')
    CREATE UNIQUE INDEX IX_Venues_Name ON dbo.Venues (Name);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Categories_Name')
    CREATE UNIQUE INDEX IX_Categories_Name ON dbo.Categories (Name);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Events_Slug')
    CREATE UNIQUE INDEX IX_Events_Slug ON dbo.Events (Slug);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_TicketTypes_Code')
    CREATE UNIQUE INDEX IX_TicketTypes_Code ON dbo.TicketTypes (Code);
GO
-- Event list and sorting by date
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Events_StartsAt')
    CREATE INDEX IX_Events_StartsAt ON dbo.Events (StartsAt);
GO
-- Upcoming events of a venue
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Events_VenueId_StartsAt')
    CREATE INDEX IX_Events_VenueId_StartsAt ON dbo.Events (VenueId, StartsAt);
GO
-- "Recently added to a category"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_EventCategories_CategoryId_AddedAt')
    CREATE INDEX IX_EventCategories_CategoryId_AddedAt ON dbo.EventCategories (CategoryId, AddedAt);
GO
-- Ticket types of an event ordered by price
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_TicketTypes_EventId_Price')
    CREATE INDEX IX_TicketTypes_EventId_Price ON dbo.TicketTypes (EventId, Price);
GO
