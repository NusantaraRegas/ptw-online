using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ptw.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeedDefaultSignatureSpecimen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Replaces the shared placeholder specimen that SeedLocationMasterAndUserSignatures gave
            // every seeded account with the default signature specimen agreed on 2026-10-21, so a
            // new database starts with the same specimen on every seeded account. The new PNG is
            // appended as the next signature version and the placeholder is superseded, never
            // overwritten, because issued print packages froze the old bytes. Only accounts created
            // by the user directory seed whose active specimen is still that placeholder qualify:
            // a specimen an operator uploaded (any other hash) is left untouched, and a second run
            // is a no-op because the active specimen is no longer the placeholder. Credentials are
            // not part of this migration; Development-only local passwords are applied by
            // DevelopmentDirectorySeeder on the `--migrate` path so they never reach another
            // environment.
            migrationBuilder.Sql(
                """
                DECLARE @now datetimeoffset = SYSUTCDATETIME();
                DECLARE @actor nvarchar(200) = N'system:migration';
                DECLARE @correlation nvarchar(200) = N'migration:20261021025910';
                -- SHA-256 of the shared placeholder PNG seeded by 20260925040421_SeedLocationMasterAndUserSignatures.
                DECLARE @placeholderSha256 nchar(64) = N'A8B9EA3907F92F8ED6FB65783E993C58D2725F8B5384B3B6DFE22FB429BE540F';
                -- Default specimen: 60x60 RGBA PNG, 476 bytes.
                DECLARE @png varbinary(max) = 0x89504E470D0A1A0A0000000D494844520000003C0000003C08060000003AFCD972000000017352474200AECE1CE90000000467414D410000B18F0BFC6105000000097048597300000EC200000EC20115284A8000000171494441546843ED978DAA83300C4675EFFFC8C2DDBE8B8120F64FD32F1A72C04D61AE3949ACEDFAF7632182E1D675DDAFF87CF66F1A9EB2802EEC8DA930F9E9B884893044DF200BE8939637F90C4727855B78BF47EF92158ECE90F0DBDB190C57F8EDAFED6EE108D50543158EB028CB49EB0CB47394257756F84894C94AE8AA70A41D64753F2CD5B5166E75CDCC043785AD063F4A1EFF7756728F14852D0268496A2C935BA32A7C2580114981250BCC5E4B085A6411BC1C2D98B2E0B4C23AF01AF23B613470B62C280A9702B92B097A133A836E612D7A27D092AC45227BA8B6B4C622002D7B3686304B161467696B4A49D449604011D6B27A38B62CA05558E3212A50853D4505DA7E58CBE25CAED94C171639A9AA97A830ADA55B625E6D4D11D6435CA970EBFE11059759BA9752724A09E85179B4F00C68B3F45348E1E8A4707452383A291C9D148E0856CFB282CE0A4727BC305A195B481CFFE7BF8FDC1E4626852DD8B66D3F7B1EF90C5BF0DC1C2ECB17E48DF8069D3F09560000000049454E44AE426082;
                DECLARE @pngSha256 nchar(64) = CONVERT(nchar(64), HASHBYTES('SHA2_256', @png), 2);

                DECLARE @targets TABLE
                (
                    [SubjectId] nvarchar(200) NOT NULL PRIMARY KEY,
                    [SupersededId] uniqueidentifier NOT NULL,
                    [SupersededVersion] int NOT NULL,
                    [NewId] uniqueidentifier NOT NULL,
                    [NewVersion] int NOT NULL
                );

                -- AggregateId mirrors UserDirectoryStore.DeterministicGuid: the first 16 bytes of
                -- SHA-256 over the upper-cased subject id.
                INSERT INTO @targets ([SubjectId], [SupersededId], [SupersededVersion], [NewId], [NewVersion])
                SELECT v.[SubjectId], v.[Id], v.[Version], NEWID(),
                       (SELECT MAX(m.[Version]) FROM [sec].[UserSignatureVersion] m WHERE m.[SubjectId] = v.[SubjectId]) + 1
                FROM [sec].[UserSignatureVersion] v
                WHERE v.[IsActive] = 1
                  AND v.[Sha256] = @placeholderSha256
                  AND EXISTS (SELECT 1 FROM [sec].[UserAccount] a WHERE a.[SubjectId] = v.[SubjectId])
                  AND EXISTS (
                      SELECT 1
                      FROM [audit].[ConfigurationAuditEvent] e
                      WHERE e.[AggregateType] = 'UserAccount'
                        AND e.[EventType] = 'user_account_seeded'
                        AND e.[AggregateId] = CONVERT(uniqueidentifier, SUBSTRING(HASHBYTES('SHA2_256', CONVERT(varchar(200), UPPER(v.[SubjectId]))), 1, 16)));

                -- The filtered unique index allows one active specimen per subject, so the
                -- placeholder is superseded before the default specimen is inserted.
                UPDATE v
                SET v.[IsActive] = 0,
                    v.[SupersededAt] = @now
                FROM [sec].[UserSignatureVersion] v
                INNER JOIN @targets t ON t.[SupersededId] = v.[Id];

                INSERT INTO [sec].[UserSignatureVersion]
                    ([Id], [SubjectId], [Version], [MediaType], [Content], [Sha256], [UploadedBy], [UploadedAt],
                     [IsActive], [SupersededAt])
                SELECT t.[NewId], t.[SubjectId], t.[NewVersion], 'image/png', @png, @pngSha256, @actor, @now, 1, NULL
                FROM @targets t;

                DECLARE @signatureEvents TABLE
                (
                    [EventId] uniqueidentifier NOT NULL PRIMARY KEY,
                    [AggregateId] uniqueidentifier NOT NULL,
                    [PayloadJson] nvarchar(max) NOT NULL
                );

                INSERT INTO @signatureEvents ([EventId], [AggregateId], [PayloadJson])
                SELECT NEWID(),
                       CONVERT(uniqueidentifier, SUBSTRING(HASHBYTES('SHA2_256', CONVERT(varchar(200), UPPER(t.[SubjectId]))), 1, 16)),
                       CONCAT('{"subjectId":"', STRING_ESCAPE(t.[SubjectId], 'json'),
                              '","signatureId":"', LOWER(CONVERT(varchar(36), t.[NewId])),
                              '","signatureVersion":', t.[NewVersion],
                              ',"sha256":"', RTRIM(@pngSha256),
                              '","supersededSignatureId":"', LOWER(CONVERT(varchar(36), t.[SupersededId])),
                              '","supersededSignatureVersion":', t.[SupersededVersion],
                              ',"source":"default-specimen-2026-10-21"}')
                FROM @targets t;

                INSERT INTO [audit].[ConfigurationAuditEvent]
                    ([Id], [AggregateType], [AggregateId], [EventType], [ActorId], [OccurredAt], [PayloadJson], [CorrelationId])
                SELECT e.[EventId], 'UserAccount', e.[AggregateId], 'user_signature_seeded', @actor, @now, e.[PayloadJson], @correlation
                FROM @signatureEvents e;

                INSERT INTO [intg].[OutboxMessage]
                    ([Id], [AggregateId], [EventType], [PayloadJson], [OccurredAt], [ProcessedAt],
                     [Attempts], [NextAttemptAt], [LastError])
                SELECT e.[EventId], e.[AggregateId], 'user_signature_seeded', e.[PayloadJson], @now, NULL, 0, NULL, NULL
                FROM @signatureEvents e;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Specimen versions and their audit rows are history; a rollback must not delete them.
        }
    }
}
