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
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    CREATE TABLE [AuditEntries] (
        [Id] bigint NOT NULL IDENTITY,
        [Utc] datetime2 NOT NULL,
        [User] nvarchar(120) NULL,
        [Action] nvarchar(80) NOT NULL,
        [EntityName] nvarchar(80) NULL,
        [EntityId] nvarchar(40) NULL,
        [DetailJson] nvarchar(max) NULL,
        CONSTRAINT [PK_AuditEntries] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    CREATE TABLE [Categories] (
        [Id] int NOT NULL IDENTITY,
        [Code] nvarchar(32) NOT NULL,
        [Name] nvarchar(120) NOT NULL,
        [ParentId] int NULL,
        [Colour] nvarchar(9) NULL,
        [Description] nvarchar(400) NULL,
        [DisplayOrder] int NOT NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_Categories] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Categories_Categories_ParentId] FOREIGN KEY ([ParentId]) REFERENCES [Categories] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    CREATE TABLE [EventDays] (
        [Id] int NOT NULL IDENTITY,
        [Date] date NOT NULL,
        [Name] nvarchar(120) NULL,
        [OpensAt] time NOT NULL,
        [ClosesAt] time NOT NULL,
        [Closed] bit NOT NULL,
        [ClosedUtc] datetime2 NULL,
        CONSTRAINT [PK_EventDays] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    CREATE TABLE [FormSchemas] (
        [Id] int NOT NULL IDENTITY,
        [Entity] int NOT NULL,
        [Name] nvarchar(160) NOT NULL,
        [Version] int NOT NULL,
        [SchemaJson] nvarchar(max) NOT NULL,
        [IsActive] bit NOT NULL,
        [Notes] nvarchar(400) NULL,
        [CreatedUtc] datetime2 NOT NULL,
        [UpdatedUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_FormSchemas] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    CREATE TABLE [Halls] (
        [Id] int NOT NULL IDENTITY,
        [Code] nvarchar(16) NOT NULL,
        [Name] nvarchar(120) NOT NULL,
        [WidthM] float NOT NULL,
        [DepthM] float NOT NULL,
        [DisplayOrder] int NOT NULL,
        [IsActive] bit NOT NULL,
        [Notes] nvarchar(400) NULL,
        [CreatedUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_Halls] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    CREATE TABLE [OutboxEmails] (
        [Id] bigint NOT NULL IDENTITY,
        [ToAddress] nvarchar(320) NOT NULL,
        [ToName] nvarchar(200) NULL,
        [Subject] nvarchar(400) NOT NULL,
        [HtmlBody] nvarchar(max) NOT NULL,
        [TextBody] nvarchar(max) NULL,
        [AttachmentsJson] nvarchar(max) NOT NULL,
        [Kind] nvarchar(32) NOT NULL,
        [Status] int NOT NULL,
        [Attempts] int NOT NULL,
        [Error] nvarchar(2000) NULL,
        [CreatedUtc] datetime2 NOT NULL,
        [SentUtc] datetime2 NULL,
        CONSTRAINT [PK_OutboxEmails] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    CREATE TABLE [ReaderEndpoints] (
        [Id] int NOT NULL IDENTITY,
        [ReaderCode] nvarchar(40) NOT NULL,
        [Host] nvarchar(120) NOT NULL,
        [Port] int NOT NULL,
        [Model] nvarchar(80) NULL,
        [IsEnabled] bit NOT NULL,
        [Notes] nvarchar(400) NULL,
        [UpdatedUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_ReaderEndpoints] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    CREATE TABLE [Settings] (
        [Key] nvarchar(80) NOT NULL,
        [ValueJson] nvarchar(max) NOT NULL,
        [UpdatedUtc] datetime2 NOT NULL,
        [UpdatedBy] nvarchar(120) NULL,
        CONSTRAINT [PK_Settings] PRIMARY KEY ([Key])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    CREATE TABLE [TagPositions] (
        [Epc] nvarchar(64) NOT NULL,
        [HallId] int NULL,
        [X] float NOT NULL,
        [Y] float NOT NULL,
        [Zone] nvarchar(8) NULL,
        [KioskId] int NULL,
        [Confidence] float NOT NULL,
        [UncertaintyM] float NOT NULL,
        [BestRssi] float NOT NULL,
        [AntennaCount] int NOT NULL,
        [FirstSeenUtc] datetime2 NOT NULL,
        [LastSeenUtc] datetime2 NOT NULL,
        [ReadCount] bigint NOT NULL,
        CONSTRAINT [PK_TagPositions] PRIMARY KEY ([Epc])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    CREATE TABLE [Visitors] (
        [Id] int NOT NULL IDENTITY,
        [BadgeEpc] nvarchar(64) NOT NULL,
        [RegistrationCode] nvarchar(24) NOT NULL,
        [FullName] nvarchar(200) NOT NULL,
        [Email] nvarchar(320) NOT NULL,
        [Phone] nvarchar(60) NULL,
        [Company] nvarchar(200) NULL,
        [JobTitle] nvarchar(160) NULL,
        [Country] nvarchar(120) NULL,
        [ProfileJson] nvarchar(max) NOT NULL,
        [ConsentEmail] bit NOT NULL,
        [ConsentTracking] bit NOT NULL,
        [Language] nvarchar(8) NULL,
        [IsActive] bit NOT NULL,
        [RegisteredUtc] datetime2 NOT NULL,
        [UpdatedUtc] datetime2 NOT NULL,
        [AccessToken] nvarchar(32) NOT NULL,
        CONSTRAINT [PK_Visitors] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    CREATE TABLE [Exhibitors] (
        [Id] int NOT NULL IDENTITY,
        [Code] nvarchar(32) NOT NULL,
        [CompanyName] nvarchar(200) NOT NULL,
        [CategoryId] int NULL,
        [SubCategoryId] int NULL,
        [ContactName] nvarchar(200) NULL,
        [Email] nvarchar(320) NULL,
        [Phone] nvarchar(60) NULL,
        [Website] nvarchar(300) NULL,
        [Country] nvarchar(120) NULL,
        [Summary] nvarchar(1000) NULL,
        [LogoPath] nvarchar(400) NULL,
        [ProfileJson] nvarchar(max) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedUtc] datetime2 NOT NULL,
        [UpdatedUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_Exhibitors] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Exhibitors_Categories_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [Categories] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Exhibitors_Categories_SubCategoryId] FOREIGN KEY ([SubCategoryId]) REFERENCES [Categories] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    CREATE TABLE [DailyReports] (
        [Id] int NOT NULL IDENTITY,
        [VisitorId] int NOT NULL,
        [EventDate] date NOT NULL,
        [Html] nvarchar(max) NOT NULL,
        [InterestJson] nvarchar(max) NOT NULL,
        [MissedJson] nvarchar(max) NOT NULL,
        [StandsVisited] int NOT NULL,
        [StandsMissed] int NOT NULL,
        [TotalDwellSeconds] int NOT NULL,
        [Status] int NOT NULL,
        [GeneratedUtc] datetime2 NOT NULL,
        [OutboxEmailId] bigint NULL,
        CONSTRAINT [PK_DailyReports] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DailyReports_Visitors_VisitorId] FOREIGN KEY ([VisitorId]) REFERENCES [Visitors] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    CREATE TABLE [DeliveryJobs] (
        [Id] int NOT NULL IDENTITY,
        [VisitorId] int NOT NULL,
        [EventDate] date NOT NULL,
        [ZipPath] nvarchar(400) NULL,
        [ZipSizeBytes] bigint NOT NULL,
        [ItemCount] int NOT NULL,
        [DownloadToken] nvarchar(48) NOT NULL,
        [TransferProvider] nvarchar(32) NULL,
        [TransferUrl] nvarchar(1000) NULL,
        [TransferExpiresUtc] datetime2 NULL,
        [Status] int NOT NULL,
        [Error] nvarchar(2000) NULL,
        [Attempts] int NOT NULL,
        [CreatedUtc] datetime2 NOT NULL,
        [CompletedUtc] datetime2 NULL,
        [OutboxEmailId] bigint NULL,
        CONSTRAINT [PK_DeliveryJobs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DeliveryJobs_Visitors_VisitorId] FOREIGN KEY ([VisitorId]) REFERENCES [Visitors] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    CREATE TABLE [CatalogueAssets] (
        [Id] int NOT NULL IDENTITY,
        [ExhibitorId] int NOT NULL,
        [FileName] nvarchar(260) NOT NULL,
        [ContentType] nvarchar(160) NOT NULL,
        [SizeBytes] bigint NOT NULL,
        [StoragePath] nvarchar(400) NOT NULL,
        [IsActive] bit NOT NULL,
        [UploadedUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_CatalogueAssets] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CatalogueAssets_Exhibitors_ExhibitorId] FOREIGN KEY ([ExhibitorId]) REFERENCES [Exhibitors] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    CREATE TABLE [Kiosks] (
        [Id] int NOT NULL IDENTITY,
        [ExhibitorId] int NOT NULL,
        [HallId] int NOT NULL,
        [StandNumber] nvarchar(24) NOT NULL,
        [X] float NOT NULL,
        [Y] float NOT NULL,
        [WidthM] float NOT NULL,
        [DepthM] float NOT NULL,
        [QrToken] nvarchar(32) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_Kiosks] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Kiosks_Exhibitors_ExhibitorId] FOREIGN KEY ([ExhibitorId]) REFERENCES [Exhibitors] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Kiosks_Halls_HallId] FOREIGN KEY ([HallId]) REFERENCES [Halls] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    CREATE TABLE [CatalogueRequests] (
        [Id] bigint NOT NULL IDENTITY,
        [VisitorId] int NOT NULL,
        [KioskId] int NOT NULL,
        [ExhibitorId] int NOT NULL,
        [EventDate] date NOT NULL,
        [RequestedUtc] datetime2 NOT NULL,
        [Source] nvarchar(16) NOT NULL,
        [Included] bit NOT NULL,
        CONSTRAINT [PK_CatalogueRequests] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CatalogueRequests_Kiosks_KioskId] FOREIGN KEY ([KioskId]) REFERENCES [Kiosks] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_CatalogueRequests_Visitors_VisitorId] FOREIGN KEY ([VisitorId]) REFERENCES [Visitors] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    CREATE TABLE [Visits] (
        [Id] bigint NOT NULL IDENTITY,
        [VisitorId] int NOT NULL,
        [KioskId] int NOT NULL,
        [ExhibitorId] int NOT NULL,
        [HallId] int NOT NULL,
        [CategoryId] int NULL,
        [SubCategoryId] int NULL,
        [EventDate] date NOT NULL,
        [StartedUtc] datetime2 NOT NULL,
        [EndedUtc] datetime2 NOT NULL,
        [DwellSeconds] int NOT NULL,
        [Level] int NOT NULL,
        [SampleCount] int NOT NULL,
        [MeanConfidence] float NOT NULL,
        [MeanMarginM] float NOT NULL,
        [IsOpen] bit NOT NULL,
        CONSTRAINT [PK_Visits] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Visits_Kiosks_KioskId] FOREIGN KEY ([KioskId]) REFERENCES [Kiosks] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Visits_Visitors_VisitorId] FOREIGN KEY ([VisitorId]) REFERENCES [Visitors] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditEntries_Utc] ON [AuditEntries] ([Utc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_CatalogueAssets_ExhibitorId] ON [CatalogueAssets] ([ExhibitorId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_CatalogueRequests_EventDate_VisitorId] ON [CatalogueRequests] ([EventDate], [VisitorId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_CatalogueRequests_KioskId] ON [CatalogueRequests] ([KioskId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_CatalogueRequests_VisitorId_KioskId_EventDate] ON [CatalogueRequests] ([VisitorId], [KioskId], [EventDate]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Categories_Code] ON [Categories] ([Code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Categories_ParentId] ON [Categories] ([ParentId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DailyReports_EventDate_Status] ON [DailyReports] ([EventDate], [Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DailyReports_VisitorId_EventDate] ON [DailyReports] ([VisitorId], [EventDate]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_DeliveryJobs_DownloadToken] ON [DeliveryJobs] ([DownloadToken]) WHERE [DownloadToken] <> ''''');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DeliveryJobs_EventDate_Status] ON [DeliveryJobs] ([EventDate], [Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DeliveryJobs_VisitorId_EventDate] ON [DeliveryJobs] ([VisitorId], [EventDate]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_EventDays_Date] ON [EventDays] ([Date]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Exhibitors_CategoryId] ON [Exhibitors] ([CategoryId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Exhibitors_Code] ON [Exhibitors] ([Code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Exhibitors_CompanyName] ON [Exhibitors] ([CompanyName]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Exhibitors_SubCategoryId] ON [Exhibitors] ([SubCategoryId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_FormSchemas_Entity] ON [FormSchemas] ([Entity]) WHERE [IsActive] = 1');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_FormSchemas_Entity_Name_Version] ON [FormSchemas] ([Entity], [Name], [Version]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Halls_Code] ON [Halls] ([Code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Kiosks_ExhibitorId] ON [Kiosks] ([ExhibitorId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Kiosks_HallId_StandNumber] ON [Kiosks] ([HallId], [StandNumber]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Kiosks_QrToken] ON [Kiosks] ([QrToken]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OutboxEmails_Status_CreatedUtc] ON [OutboxEmails] ([Status], [CreatedUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ReaderEndpoints_ReaderCode] ON [ReaderEndpoints] ([ReaderCode]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_TagPositions_LastSeenUtc] ON [TagPositions] ([LastSeenUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Visitors_AccessToken] ON [Visitors] ([AccessToken]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Visitors_BadgeEpc] ON [Visitors] ([BadgeEpc]) WHERE [BadgeEpc] <> ''''');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Visitors_Email] ON [Visitors] ([Email]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Visitors_RegistrationCode] ON [Visitors] ([RegistrationCode]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Visits_CategoryId_EventDate] ON [Visits] ([CategoryId], [EventDate]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Visits_EventDate_Level] ON [Visits] ([EventDate], [Level]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Visits_IsOpen] ON [Visits] ([IsOpen]) WHERE [IsOpen] = 1');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Visits_KioskId_EventDate] ON [Visits] ([KioskId], [EventDate]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Visits_VisitorId_EventDate] ON [Visits] ([VisitorId], [EventDate]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817022814_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260817022814_InitialCreate', N'8.0.10');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817031104_MobileAppAndProgramme'
)
BEGIN
    CREATE TABLE [MobileSessions] (
        [Id] bigint NOT NULL IDENTITY,
        [VisitorId] int NOT NULL,
        [TokenHash] nvarchar(64) NOT NULL,
        [Platform] nvarchar(32) NULL,
        [DeviceName] nvarchar(120) NULL,
        [AppVersion] nvarchar(40) NULL,
        [CreatedUtc] datetime2 NOT NULL,
        [LastSeenUtc] datetime2 NOT NULL,
        [ExpiresUtc] datetime2 NOT NULL,
        [RevokedUtc] datetime2 NULL,
        CONSTRAINT [PK_MobileSessions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_MobileSessions_Visitors_VisitorId] FOREIGN KEY ([VisitorId]) REFERENCES [Visitors] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817031104_MobileAppAndProgramme'
)
BEGIN
    CREATE TABLE [Sessions] (
        [Id] int NOT NULL IDENTITY,
        [Code] nvarchar(32) NOT NULL,
        [Title] nvarchar(300) NOT NULL,
        [Kind] int NOT NULL,
        [SpeakerName] nvarchar(200) NULL,
        [SpeakerTitle] nvarchar(200) NULL,
        [SpeakerOrganisation] nvarchar(200) NULL,
        [Abstract] nvarchar(2000) NULL,
        [HallId] int NULL,
        [RoomName] nvarchar(160) NULL,
        [ExhibitorId] int NULL,
        [CategoryId] int NULL,
        [SubCategoryId] int NULL,
        [EventDate] date NOT NULL,
        [StartsAt] time NOT NULL,
        [EndsAt] time NOT NULL,
        [Capacity] int NOT NULL,
        [RequiresBooking] bit NOT NULL,
        [Language] nvarchar(8) NULL,
        [IsActive] bit NOT NULL,
        [CreatedUtc] datetime2 NOT NULL,
        [UpdatedUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_Sessions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Sessions_Categories_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [Categories] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Sessions_Categories_SubCategoryId] FOREIGN KEY ([SubCategoryId]) REFERENCES [Categories] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Sessions_Exhibitors_ExhibitorId] FOREIGN KEY ([ExhibitorId]) REFERENCES [Exhibitors] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Sessions_Halls_HallId] FOREIGN KEY ([HallId]) REFERENCES [Halls] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817031104_MobileAppAndProgramme'
)
BEGIN
    CREATE TABLE [VisitorLoginCodes] (
        [Id] bigint NOT NULL IDENTITY,
        [VisitorId] int NOT NULL,
        [EmailSentTo] nvarchar(320) NOT NULL,
        [CodeHash] nvarchar(64) NOT NULL,
        [CreatedUtc] datetime2 NOT NULL,
        [ExpiresUtc] datetime2 NOT NULL,
        [ConsumedUtc] datetime2 NULL,
        [Attempts] int NOT NULL,
        [RequestedFromIp] nvarchar(64) NULL,
        [OutboxEmailId] bigint NULL,
        CONSTRAINT [PK_VisitorLoginCodes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_VisitorLoginCodes_Visitors_VisitorId] FOREIGN KEY ([VisitorId]) REFERENCES [Visitors] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817031104_MobileAppAndProgramme'
)
BEGIN
    CREATE TABLE [SessionBookmarks] (
        [Id] bigint NOT NULL IDENTITY,
        [VisitorId] int NOT NULL,
        [SessionId] int NOT NULL,
        [CreatedUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_SessionBookmarks] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SessionBookmarks_Sessions_SessionId] FOREIGN KEY ([SessionId]) REFERENCES [Sessions] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_SessionBookmarks_Visitors_VisitorId] FOREIGN KEY ([VisitorId]) REFERENCES [Visitors] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817031104_MobileAppAndProgramme'
)
BEGIN
    CREATE UNIQUE INDEX [IX_MobileSessions_TokenHash] ON [MobileSessions] ([TokenHash]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817031104_MobileAppAndProgramme'
)
BEGIN
    CREATE INDEX [IX_MobileSessions_VisitorId] ON [MobileSessions] ([VisitorId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817031104_MobileAppAndProgramme'
)
BEGIN
    CREATE INDEX [IX_SessionBookmarks_SessionId] ON [SessionBookmarks] ([SessionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817031104_MobileAppAndProgramme'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SessionBookmarks_VisitorId_SessionId] ON [SessionBookmarks] ([VisitorId], [SessionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817031104_MobileAppAndProgramme'
)
BEGIN
    CREATE INDEX [IX_Sessions_CategoryId] ON [Sessions] ([CategoryId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817031104_MobileAppAndProgramme'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Sessions_Code] ON [Sessions] ([Code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817031104_MobileAppAndProgramme'
)
BEGIN
    CREATE INDEX [IX_Sessions_EventDate_HallId] ON [Sessions] ([EventDate], [HallId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817031104_MobileAppAndProgramme'
)
BEGIN
    CREATE INDEX [IX_Sessions_EventDate_Kind] ON [Sessions] ([EventDate], [Kind]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817031104_MobileAppAndProgramme'
)
BEGIN
    CREATE INDEX [IX_Sessions_EventDate_StartsAt] ON [Sessions] ([EventDate], [StartsAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817031104_MobileAppAndProgramme'
)
BEGIN
    CREATE INDEX [IX_Sessions_ExhibitorId] ON [Sessions] ([ExhibitorId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817031104_MobileAppAndProgramme'
)
BEGIN
    CREATE INDEX [IX_Sessions_HallId] ON [Sessions] ([HallId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817031104_MobileAppAndProgramme'
)
BEGIN
    CREATE INDEX [IX_Sessions_SubCategoryId] ON [Sessions] ([SubCategoryId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817031104_MobileAppAndProgramme'
)
BEGIN
    CREATE INDEX [IX_VisitorLoginCodes_ExpiresUtc] ON [VisitorLoginCodes] ([ExpiresUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817031104_MobileAppAndProgramme'
)
BEGIN
    CREATE INDEX [IX_VisitorLoginCodes_VisitorId_CreatedUtc] ON [VisitorLoginCodes] ([VisitorId], [CreatedUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817031104_MobileAppAndProgramme'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260817031104_MobileAppAndProgramme', N'8.0.10');
END;
GO

COMMIT;
GO

