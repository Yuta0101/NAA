SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @AuditTables table
(
    SchemaName sysname NOT NULL,
    TableName sysname NOT NULL
);

INSERT INTO @AuditTables (SchemaName, TableName)
VALUES
    ('dbo', 'USER_ACCOUNT'),
    ('dbo', 'QUESTIONS_RECORDS'),
    ('dbo', 'ANSWERS_RECORDS'),
    ('dbo', 'CONVERSATION_RECORDS');

DECLARE @SchemaName sysname;
DECLARE @TableName sysname;
DECLARE @ObjectId int;
DECLARE @DefaultConstraint sysname;
DECLARE @DataType nvarchar(128);
DECLARE @Sql nvarchar(max);

DECLARE AuditTableCursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT SchemaName, TableName
    FROM @AuditTables;

OPEN AuditTableCursor;
FETCH NEXT FROM AuditTableCursor INTO @SchemaName, @TableName;

WHILE @@FETCH_STATUS = 0
BEGIN
    SET @ObjectId = OBJECT_ID(QUOTENAME(@SchemaName) + '.' + QUOTENAME(@TableName));

    IF @ObjectId IS NOT NULL
       AND COL_LENGTH(@SchemaName + '.' + @TableName, 'UPDATE_DT') IS NOT NULL
    BEGIN
        SET @DefaultConstraint = NULL;
        SET @DataType = NULL;

        SELECT
            @DefaultConstraint = dc.name,
            @DataType =
                CASE
                    WHEN ty.name IN ('datetime2', 'datetimeoffset', 'time')
                        THEN ty.name + '(' + CONVERT(varchar(3), c.scale) + ')'
                    WHEN ty.name IN ('varchar', 'char', 'varbinary', 'binary')
                        THEN ty.name + '(' + CASE WHEN c.max_length = -1 THEN 'max' ELSE CONVERT(varchar(10), c.max_length) END + ')'
                    WHEN ty.name IN ('nvarchar', 'nchar')
                        THEN ty.name + '(' + CASE WHEN c.max_length = -1 THEN 'max' ELSE CONVERT(varchar(10), c.max_length / 2) END + ')'
                    WHEN ty.name IN ('decimal', 'numeric')
                        THEN ty.name + '(' + CONVERT(varchar(3), c.precision) + ',' + CONVERT(varchar(3), c.scale) + ')'
                    ELSE ty.name
                END
        FROM sys.columns AS c
        INNER JOIN sys.types AS ty
            ON ty.user_type_id = c.user_type_id
        LEFT JOIN sys.default_constraints AS dc
            ON dc.object_id = c.default_object_id
        WHERE c.object_id = @ObjectId
          AND c.name = 'UPDATE_DT';

        IF @DefaultConstraint IS NOT NULL
        BEGIN
            SET @Sql = N'ALTER TABLE '
                + QUOTENAME(@SchemaName) + N'.' + QUOTENAME(@TableName)
                + N' DROP CONSTRAINT ' + QUOTENAME(@DefaultConstraint) + N';';
            EXEC sys.sp_executesql @Sql;
        END;

        SET @Sql = N'ALTER TABLE '
            + QUOTENAME(@SchemaName) + N'.' + QUOTENAME(@TableName)
            + N' ALTER COLUMN [UPDATE_DT] ' + @DataType + N' NULL;';
        EXEC sys.sp_executesql @Sql;

        IF COL_LENGTH(@SchemaName + '.' + @TableName, 'INSERT_DT') IS NOT NULL
           AND COL_LENGTH(@SchemaName + '.' + @TableName, 'UPDATE_OP') IS NOT NULL
        BEGIN
            SET @Sql = N'UPDATE '
                + QUOTENAME(@SchemaName) + N'.' + QUOTENAME(@TableName)
                + N' SET [UPDATE_DT] = NULL'
                + N' WHERE [UPDATE_OP] IS NULL'
                + N'   AND [INSERT_DT] IS NOT NULL'
                + N'   AND [UPDATE_DT] IS NOT NULL'
                + N'   AND ABS(DATEDIFF_BIG(SECOND, DATEADD(HOUR, 8, [INSERT_DT]), [UPDATE_DT])) <= 2;';
            EXEC sys.sp_executesql @Sql;
        END;
    END;

    FETCH NEXT FROM AuditTableCursor INTO @SchemaName, @TableName;
END;

CLOSE AuditTableCursor;
DEALLOCATE AuditTableCursor;

COMMIT TRANSACTION;
