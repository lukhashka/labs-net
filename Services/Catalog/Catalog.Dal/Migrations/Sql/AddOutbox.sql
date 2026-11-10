BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110132510_AddOutbox'
)
BEGIN
    CREATE TABLE [OutboxMessages] (
        [Id] uniqueidentifier NOT NULL,
        [EventType] nvarchar(200) NOT NULL,
        [Exchange] nvarchar(200) NOT NULL,
        [RoutingKey] nvarchar(200) NOT NULL,
        [Payload] nvarchar(max) NOT NULL,
        [CorrelationId] nvarchar(100) NULL,
        [TraceParent] nvarchar(100) NULL,
        [OccurredAt] datetime2 NOT NULL,
        [PublishedAt] datetime2 NULL,
        [Attempts] int NOT NULL,
        [LastError] nvarchar(1000) NULL,
        CONSTRAINT [PK_OutboxMessages] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110132510_AddOutbox'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_OutboxMessages_OccurredAt] ON [OutboxMessages] ([OccurredAt]) WHERE [PublishedAt] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110132510_AddOutbox'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261110132510_AddOutbox', N'10.0.14');
END;

COMMIT;
GO

