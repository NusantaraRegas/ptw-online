using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ptw.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeedSponsorAssignmentsForAreaAndHsseStaff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Grants an approved Sponsor assignment to the non-manager area-owner (SO/Officer) and
            // HSSE staff seeded by 20260925004624 so they can raise their own PTW. Managers
            // (AreaOwnerManager and the HSSE manager) are deliberately excluded. Holding Sponsor
            // next to a validator/reviewer role is safe because separation of duty is enforced per
            // permit on the actor id, not on the role set. Every insert is guarded so the migration
            // is idempotent and never overrides an operator decision: an existing approved Sponsor
            // row, or a manager role granted after the snapshot, skips the grant with an audit trail.
            migrationBuilder.Sql(
                """
                DECLARE @now datetimeoffset = SYSUTCDATETIME();
                DECLARE @actor nvarchar(200) = N'system:migration';
                DECLARE @correlation nvarchar(200) = N'migration:20260925141908';
                DECLARE @maker nvarchar(200) = N'assignment.maker.bootstrap';
                DECLARE @checker nvarchar(200) = N'assignment.checker.bootstrap';
                DECLARE @effectiveFrom datetimeoffset = '2026-09-25T00:00:00Z';
                DECLARE @stamp datetimeoffset = '2026-09-25T14:19:08Z';
                DECLARE @actionCodes nvarchar(max) =
                    N'["permit.cancel","permit.closure.request","permit.create","permit.renewal.request","permit.submit","permit.update"]';

                DECLARE @assignments TABLE
                (
                    [Id] uniqueidentifier NOT NULL PRIMARY KEY,
                    [SubjectId] nvarchar(200) NOT NULL,
                    [Version1Id] uniqueidentifier NOT NULL,
                    [Version2Id] uniqueidentifier NOT NULL,
                    [Version3Id] uniqueidentifier NOT NULL
                );

                INSERT INTO @assignments ([Id], [SubjectId], [Version1Id], [Version2Id], [Version3Id])
                VALUES
                    ('189FD185-B8D4-47DA-899B-39B8880EFD17', N'ade.ruhimat', '2D645708-F69F-4D1E-A1CC-FA418085FA1B', '8C2F784C-FE6C-4B30-BA40-56354F69EA28', '9063BE49-5563-417D-B0CC-31A56FA19504'),
                    ('7466B5A8-171B-4AE7-B1DF-30E45FF5BFD5', N'benny.sulistio', '5F79C6F9-DBB9-4236-8027-85DD996C58E9', 'AEC4EF91-F7A3-4830-892E-15811D83A597', '85B02FE4-63B0-45AB-9ABF-6406B12BF1A0'),
                    ('2DC9F1F1-639E-4186-91E8-595FA208BCDB', N'muhamad.ibrahim', 'A00244B7-3751-4AF6-89EB-BE0833853CEB', '4FEC199B-BA59-451F-BF8A-E2A64E4DA656', '35DCF9B0-D31A-47B4-A93C-45B03D08E902'),
                    ('F02D051B-CE42-4B54-8222-93D81DAD87AB', N'darsono4', '8C134F60-DCD4-47DA-A883-9A58E2D0A343', 'ECDE03E9-70F4-43BC-862A-743ECF782FD7', '3C0EF6C9-C3B2-418C-B925-8DF80F950240'),
                    ('BD86B147-06AA-4F7E-ACAE-C44968D4E7E4', N'aldi.setiawan', '20705397-9096-4B98-ACB4-9D3202FD6EDC', '6E22DAE1-BB66-47FB-8D84-27C1A99E049D', '8EB59CBE-A824-4D2F-AECE-801A1A124C25'),
                    ('C8E2CCA5-B515-481C-8A51-1C60E40A77C0', N'mk.ferry.febrian', '00695FB0-F8CE-48E6-9230-371841E99379', 'DD60A378-D7AB-4DF5-BFB0-7D59340270F9', '1B302A92-23A3-4D6C-BFC6-BD9D6E6E8115');

                DECLARE @outcome TABLE
                (
                    [Id] uniqueidentifier NOT NULL PRIMARY KEY,
                    [SkipReason] varchar(50) NULL
                );

                -- Fail closed: the grant is only seeded when the account exists and is active, the
                -- subject does not already hold an approved Sponsor assignment, and the subject has
                -- not been given a manager role since the snapshot was taken.
                INSERT INTO @outcome ([Id], [SkipReason])
                SELECT a.[Id],
                       CASE
                           WHEN EXISTS (SELECT 1 FROM [sec].[UserAuthorization] x WHERE x.[Id] = a.[Id])
                               THEN 'already_present'
                           WHEN NOT EXISTS (SELECT 1 FROM [sec].[UserAccount] u WHERE u.[SubjectId] = a.[SubjectId])
                               THEN 'user_account_missing'
                           WHEN EXISTS (SELECT 1 FROM [sec].[UserAccount] u WHERE u.[SubjectId] = a.[SubjectId] AND u.[IsActive] = 0)
                               THEN 'user_account_inactive'
                           WHEN EXISTS (
                                    SELECT 1
                                    FROM [sec].[UserAuthorization] x
                                    WHERE x.[SubjectId] = a.[SubjectId]
                                      AND x.[RoleCode] = N'Sponsor'
                                      AND x.[Status] = 'Approved')
                               THEN 'approved_role_exists'
                           WHEN EXISTS (
                                    SELECT 1
                                    FROM [sec].[UserAuthorization] x
                                    WHERE x.[SubjectId] = a.[SubjectId]
                                      AND x.[RoleCode] = N'AreaOwnerManager'
                                      AND x.[Status] = 'Approved')
                               THEN 'manager_role_exists'
                           ELSE NULL
                       END
                FROM @assignments a;

                INSERT INTO [sec].[UserAuthorization]
                    ([Id], [SubjectId], [RoleCode], [ActionCodesJson], [LocationId], [IncludeDescendants],
                     [RequiredCompetencyCodesJson], [Kind], [SourceAuthorizationId], [EffectiveFrom], [EffectiveUntil],
                     [Status], [Version], [MakerId], [CheckerId], [ApprovedAt], [CreatedAt], [UpdatedAt])
                SELECT a.[Id], a.[SubjectId], N'Sponsor', @actionCodes, NULL, 0,
                       '[]', 'Direct', NULL, @effectiveFrom, NULL,
                       'Approved', 3, @maker, @checker, @stamp, @stamp, @stamp
                FROM @assignments a
                INNER JOIN @outcome o ON o.[Id] = a.[Id]
                WHERE o.[SkipReason] IS NULL;

                -- Three version snapshots per assignment (Draft, Submitted, Approved) mirror the
                -- maker/checker history the store writes. The JSON is ASCII, so hashing its varchar
                -- form equals the UTF-8 hash the store computes.
                DECLARE @versions TABLE
                (
                    [Id] uniqueidentifier NOT NULL PRIMARY KEY,
                    [UserAuthorizationId] uniqueidentifier NOT NULL,
                    [Version] int NOT NULL,
                    [ContentJson] nvarchar(max) NOT NULL
                );

                INSERT INTO @versions ([Id], [UserAuthorizationId], [Version], [ContentJson])
                SELECT v.[Id], a.[Id], v.[Version],
                       CONCAT('{"id":"', LOWER(CONVERT(varchar(36), a.[Id])),
                              '","subjectId":"', STRING_ESCAPE(a.[SubjectId], 'json'),
                              '","roleCode":"Sponsor","actionCodes":', @actionCodes,
                              ',"locationId":null,"includeDescendants":false,"requiredCompetencyCodes":[],"kind":0,"sourceAuthorizationId":null',
                              ',"effectiveFrom":"2026-09-25T00:00:00+00:00","effectiveUntil":null',
                              ',"status":', v.[Status],
                              ',"version":', v.[Version],
                              ',"makerId":"', @maker, '"',
                              ',"checkerId":', CASE WHEN v.[Version] = 3 THEN CONCAT('"', @checker, '"') ELSE 'null' END,
                              ',"approvedAt":', CASE WHEN v.[Version] = 3 THEN '"2026-09-25T14:19:08+00:00"' ELSE 'null' END,
                              ',"createdAt":"2026-09-25T14:19:08+00:00","updatedAt":"2026-09-25T14:19:08+00:00"}')
                FROM @assignments a
                INNER JOIN @outcome o ON o.[Id] = a.[Id]
                CROSS APPLY (VALUES
                    (a.[Version1Id], 1, 0),
                    (a.[Version2Id], 2, 1),
                    (a.[Version3Id], 3, 2)) v ([Id], [Version], [Status])
                WHERE o.[SkipReason] IS NULL;

                INSERT INTO [sec].[UserAuthorizationVersion]
                    ([Id], [UserAuthorizationId], [Version], [ContentJson], [ContentHash], [CreatedAt], [CreatedBy])
                SELECT v.[Id], v.[UserAuthorizationId], v.[Version], v.[ContentJson],
                       CONVERT(varchar(64), HASHBYTES('SHA2_256', CONVERT(varchar(max), v.[ContentJson])), 2),
                       @stamp, CASE WHEN v.[Version] = 3 THEN @checker ELSE @maker END
                FROM @versions v;

                DECLARE @events TABLE
                (
                    [EventId] uniqueidentifier NOT NULL PRIMARY KEY,
                    [AggregateId] uniqueidentifier NOT NULL,
                    [EventType] nvarchar(100) NOT NULL,
                    [PayloadJson] nvarchar(max) NOT NULL
                );

                INSERT INTO @events ([EventId], [AggregateId], [EventType], [PayloadJson])
                SELECT NEWID(), a.[Id],
                       CASE WHEN o.[SkipReason] IS NULL THEN 'authorization_seeded' ELSE 'authorization_seed_skipped' END,
                       CONCAT('{"subjectId":"', STRING_ESCAPE(a.[SubjectId], 'json'),
                              '","roleCode":"Sponsor","locationId":null,"locationCode":null,"version":3',
                              ',"makerId":"', @maker, '","checkerId":"', @checker, '"',
                              ',"reason":', CASE WHEN o.[SkipReason] IS NULL THEN 'null' ELSE CONCAT('"', o.[SkipReason], '"') END,
                              ',"source":"sponsor-grant-area-hsse-staff-2026-09-25"}')
                FROM @assignments a
                INNER JOIN @outcome o ON o.[Id] = a.[Id]
                WHERE o.[SkipReason] IS NULL OR o.[SkipReason] <> 'already_present';

                INSERT INTO [audit].[ConfigurationAuditEvent]
                    ([Id], [AggregateType], [AggregateId], [EventType], [ActorId], [OccurredAt], [PayloadJson], [CorrelationId])
                SELECT e.[EventId], 'UserAuthorization', e.[AggregateId], e.[EventType], @actor, @now, e.[PayloadJson], @correlation
                FROM @events e;

                INSERT INTO [intg].[OutboxMessage]
                    ([Id], [AggregateId], [EventType], [PayloadJson], [OccurredAt], [ProcessedAt],
                     [Attempts], [NextAttemptAt], [LastError])
                SELECT e.[EventId], e.[AggregateId], e.[EventType], e.[PayloadJson], @now, NULL, 0, NULL, NULL
                FROM @events e
                WHERE e.[EventType] = 'authorization_seeded';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Seeded assignments are business data with audit history; a rollback must not delete
            // them.
        }
    }
}
