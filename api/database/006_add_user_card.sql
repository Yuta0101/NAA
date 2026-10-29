-- Adds NFC card login support.
-- One account can own several cards. A card is bound to at most one account;
-- unbinding only disables the row so the history of the binding is kept.
-- CARD_UID stores the normalized value typed by the keyboard-emulation reader
-- (for YP201 this is a 10-digit decimal string).
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID('dbo.USER_CARD', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.USER_CARD
    (
        UNIQUE_ID varchar(36) NOT NULL
            CONSTRAINT PK_USER_CARD PRIMARY KEY CLUSTERED,
        USER_ID varchar(36) NOT NULL
            CONSTRAINT FK_USER_CARD_USER REFERENCES dbo.USER_ACCOUNT (UNIQUE_ID),
        CARD_UID varchar(32) NOT NULL,
        ENABLED bit NOT NULL
            CONSTRAINT DF_USER_CARD_ENABLED DEFAULT (1),
        INSERT_DT datetime2(0) NULL,
        INSERT_OP varchar(50) NULL,
        UPDATE_DT datetime2(0) NULL,
        UPDATE_OP varchar(50) NULL
    );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID('dbo.USER_CARD')
      AND name = 'UX_USER_CARD_CARD_UID'
)
BEGIN
    CREATE UNIQUE INDEX UX_USER_CARD_CARD_UID
        ON dbo.USER_CARD (CARD_UID);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID('dbo.USER_CARD')
      AND name = 'IX_USER_CARD_USER_ID'
)
BEGIN
    CREATE INDEX IX_USER_CARD_USER_ID
        ON dbo.USER_CARD (USER_ID, ENABLED);
END;

COMMIT TRANSACTION;
