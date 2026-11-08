IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027115626_InitialCreate'
)
BEGIN
    CREATE TABLE [Categories] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_Categories] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027115626_InitialCreate'
)
BEGIN
    CREATE TABLE [Venues] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(150) NOT NULL,
        [Address] nvarchar(300) NOT NULL,
        [Capacity] int NOT NULL,
        CONSTRAINT [PK_Venues] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Venues_Capacity] CHECK ([Capacity] > 0)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027115626_InitialCreate'
)
BEGIN
    CREATE TABLE [Events] (
        [Id] int NOT NULL IDENTITY,
        [Title] nvarchar(200) NOT NULL,
        [Slug] nvarchar(200) NOT NULL,
        [StartsAt] datetime2 NOT NULL,
        [EndsAt] datetime2 NOT NULL,
        [Status] nvarchar(20) NOT NULL DEFAULT N'Draft',
        [CoverUrl] nvarchar(500) NULL,
        [VenueId] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Events] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Events_Dates] CHECK ([EndsAt] > [StartsAt]),
        CONSTRAINT [FK_Events_Venues_VenueId] FOREIGN KEY ([VenueId]) REFERENCES [Venues] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027115626_InitialCreate'
)
BEGIN
    CREATE TABLE [EventCategories] (
        [EventId] int NOT NULL,
        [CategoryId] int NOT NULL,
        [AddedAt] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [PK_EventCategories] PRIMARY KEY ([EventId], [CategoryId]),
        CONSTRAINT [FK_EventCategories_Categories_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [Categories] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_EventCategories_Events_EventId] FOREIGN KEY ([EventId]) REFERENCES [Events] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027115626_InitialCreate'
)
BEGIN
    CREATE TABLE [EventDetails] (
        [EventId] int NOT NULL,
        [Description] nvarchar(4000) NOT NULL,
        [Organizer] nvarchar(200) NULL,
        [AgeLimit] int NOT NULL DEFAULT 0,
        CONSTRAINT [PK_EventDetails] PRIMARY KEY ([EventId]),
        CONSTRAINT [CK_EventDetails_AgeLimit] CHECK ([AgeLimit] BETWEEN 0 AND 21),
        CONSTRAINT [FK_EventDetails_Events_EventId] FOREIGN KEY ([EventId]) REFERENCES [Events] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027115626_InitialCreate'
)
BEGIN
    CREATE TABLE [TicketTypes] (
        [Id] int NOT NULL IDENTITY,
        [EventId] int NOT NULL,
        [Name] nvarchar(50) NOT NULL,
        [Code] nvarchar(50) NOT NULL,
        [Price] decimal(18,2) NOT NULL,
        [Capacity] int NOT NULL,
        [AvailableStock] int NOT NULL,
        CONSTRAINT [PK_TicketTypes] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_TicketTypes_Price] CHECK ([Price] > 0),
        CONSTRAINT [CK_TicketTypes_Stock] CHECK ([AvailableStock] >= 0 AND [AvailableStock] <= [Capacity]),
        CONSTRAINT [FK_TicketTypes_Events_EventId] FOREIGN KEY ([EventId]) REFERENCES [Events] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027115626_InitialCreate'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Name') AND [object_id] = OBJECT_ID(N'[Categories]'))
        SET IDENTITY_INSERT [Categories] ON;
    EXEC(N'INSERT INTO [Categories] ([Id], [Name])
    VALUES (1, N''Концерт''),
    (2, N''Хакатон''),
    (3, N''Лекція''),
    (4, N''Спорт''),
    (5, N''Вечірка''),
    (6, N''Воркшоп'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Name') AND [object_id] = OBJECT_ID(N'[Categories]'))
        SET IDENTITY_INSERT [Categories] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027115626_InitialCreate'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Address', N'Capacity', N'Name') AND [object_id] = OBJECT_ID(N'[Venues]'))
        SET IDENTITY_INSERT [Venues] ON;
    EXEC(N'INSERT INTO [Venues] ([Id], [Address], [Capacity], [Name])
    VALUES (1, N''вул. Коцюбинського, 2'', 400, N''Актова зала ЧНУ (корпус 1)''),
    (2, N''вул. Університетська, 28'', 1200, N''Спорткомплекс ЧНУ''),
    (3, N''вул. Університетська, 28, ауд. 25'', 80, N''Коворкінг ФМІ'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Address', N'Capacity', N'Name') AND [object_id] = OBJECT_ID(N'[Venues]'))
        SET IDENTITY_INSERT [Venues] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027115626_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Categories_Name] ON [Categories] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027115626_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_EventCategories_CategoryId_AddedAt] ON [EventCategories] ([CategoryId], [AddedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027115626_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Events_Slug] ON [Events] ([Slug]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027115626_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Events_StartsAt] ON [Events] ([StartsAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027115626_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Events_VenueId_StartsAt] ON [Events] ([VenueId], [StartsAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027115626_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TicketTypes_Code] ON [TicketTypes] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027115626_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_TicketTypes_EventId_Price] ON [TicketTypes] ([EventId], [Price]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027115626_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Venues_Name] ON [Venues] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027115626_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261027115626_InitialCreate', N'10.0.14');
END;

COMMIT;
GO

