using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ptw.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeedUserAccountsAndRoleAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Snapshot of the user directory and its approved role assignments exported from the
            // Development database on 2026-09-25, so a new environment starts with the same people
            // and roles. Credentials and signature specimens are deliberately excluded: password
            // material must never enter Git and specimens are uploaded per environment. Every
            // insert is guarded, so the migration is a no-op on the database it was exported from
            // and never overwrites rows an operator created earlier.
            migrationBuilder.Sql(
                """
                DECLARE @now datetimeoffset = SYSUTCDATETIME();
                DECLARE @actor nvarchar(200) = N'system:migration';
                DECLARE @correlation nvarchar(200) = N'migration:20260925004624';
                -- Location id of ORF in the exported database; area-scoped rows are re-pointed to the
                -- ORF row of the target database because location ids differ between environments.
                DECLARE @sourceLocationId varchar(36) = '01a0a755-d839-7764-b2e5-8d0b7b9bece0';
                DECLARE @orfLocationId uniqueidentifier = (
                    SELECT TOP 1 [Id]
                    FROM [cfg].[LocationMaster]
                    WHERE [Code] = N'ORF'
                    ORDER BY CASE WHEN [Status] = 'Approved' THEN 0 ELSE 1 END, [EffectiveFrom] DESC);

                DECLARE @users TABLE
                (
                    [SubjectId] nvarchar(200) NOT NULL PRIMARY KEY,
                    [UserName] nvarchar(100) NOT NULL,
                    [DisplayName] nvarchar(200) NOT NULL,
                    [Position] nvarchar(200) NULL,
                    [Department] nvarchar(200) NULL,
                    [IsActive] bit NOT NULL,
                    [Version] int NOT NULL,
                    [CreatedAt] datetimeoffset NOT NULL,
                    [UpdatedAt] datetimeoffset NOT NULL
                );

                INSERT INTO @users
                    ([SubjectId], [UserName], [DisplayName], [Position], [Department], [IsActive], [Version], [CreatedAt], [UpdatedAt])
                VALUES
                    (N'superadmin.local', N'superadmin', N'Super Administrator', N'System Administrator', N'IT', 1, 3, '2026-09-21T13:58:45.7882111Z', '2026-09-23T10:28:42.7723494Z'),
                    (N'yosep.zulkarnain', N'yosep.zulkarnain', N'Yosep Ismail Zulkarnain', N'Manager Gas Distribution&ORF Management', N'Gas Distribution&ORF Management', 1, 2, '2026-09-21T14:32:12.5438620Z', '2026-09-21T15:46:51.7521636Z'),
                    (N'ade.ruhimat', N'ade.ruhimat', N'Ade Imat Ruhimat', N'Sr. Officer II Gas System Management', N'Gas Distribution&ORF Management', 1, 2, '2026-09-21T14:32:12.7531829Z', '2026-09-21T15:46:26.5664816Z'),
                    (N'muhamad.ibrahim', N'muhamad.ibrahim', N'Muhammad Tauvik Ibrahim', N'Lead II Operator ORF', N'Gas Distribution&ORF Management', 1, 2, '2026-09-21T14:32:12.8557625Z', '2026-09-21T16:06:22.3405941Z'),
                    (N'benny.sulistio', N'benny.sulistio', N'Benny Sulistio', N'Officer II Gas Delivery Operation', N'Gas Distribution&ORF Management', 1, 2, '2026-09-21T14:32:12.9497136Z', '2026-09-21T15:46:34.2087523Z'),
                    (N'erwin.jonathan', N'erwin.jonathan', N'Erwin Jonathan', N'Manager HSSE', N'HSSE', 1, 2, '2026-09-21T15:03:39.5852298Z', '2026-09-21T15:46:41.0126159Z'),
                    (N'darsono4', N'darsono4', N'Darsono', N'Sr. Officer II Health & Safety', N'HSSE', 1, 2, '2026-09-21T15:03:39.9794208Z', '2026-09-21T15:46:37.5720765Z'),
                    (N'aldi.setiawan', N'aldi.setiawan', N'Aldi Firdiansyah Setiawan', N'Officer II Security', N'HSSE', 1, 2, '2026-09-21T15:03:40.0656527Z', '2026-09-21T15:46:30.3189279Z'),
                    (N'mk.ferry.febrian', N'mk.ferry.febrian', N'Ferry Febrian', N'Admin HSSE', N'HSSE', 1, 2, '2026-09-21T15:03:40.1526290Z', '2026-09-21T15:46:44.3028650Z'),
                    (N'mk.revanza.raytama', N'mk.revanza.raytama', N'Revanza Raytama', N'Sistem Informasi', N'ISGA', 1, 3, '2026-09-21T16:07:57.0747593Z', '2026-09-23T00:53:37.1658069Z');

                DECLARE @insertedUsers TABLE ([SubjectId] nvarchar(200) NOT NULL PRIMARY KEY);

                INSERT INTO [sec].[UserAccount]
                    ([SubjectId], [UserName], [NormalizedUserName], [DisplayName], [Position], [Department],
                     [IsActive], [Version], [CreatedAt], [UpdatedAt])
                OUTPUT inserted.[SubjectId] INTO @insertedUsers
                SELECT u.[SubjectId], u.[UserName], UPPER(u.[UserName]), u.[DisplayName], u.[Position], u.[Department],
                       u.[IsActive], u.[Version], u.[CreatedAt], u.[UpdatedAt]
                FROM @users u
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM [sec].[UserAccount] a
                    WHERE a.[SubjectId] = u.[SubjectId]
                       OR a.[NormalizedUserName] = UPPER(u.[UserName]));

                DECLARE @userEvents TABLE
                (
                    [EventId] uniqueidentifier NOT NULL PRIMARY KEY,
                    [AggregateId] uniqueidentifier NOT NULL,
                    [PayloadJson] nvarchar(max) NOT NULL
                );

                -- AggregateId mirrors UserDirectoryStore.DeterministicGuid: the first 16 bytes of
                -- SHA-256 over the upper-cased subject id.
                INSERT INTO @userEvents ([EventId], [AggregateId], [PayloadJson])
                SELECT NEWID(),
                       CONVERT(uniqueidentifier, SUBSTRING(HASHBYTES('SHA2_256', CONVERT(varchar(200), UPPER(u.[SubjectId]))), 1, 16)),
                       CONCAT('{"subjectId":"', STRING_ESCAPE(u.[SubjectId], 'json'),
                              '","userName":"', STRING_ESCAPE(u.[UserName], 'json'),
                              '","displayName":"', STRING_ESCAPE(u.[DisplayName], 'json'),
                              '","position":', CASE WHEN u.[Position] IS NULL THEN 'null'
                                                    ELSE CONCAT('"', STRING_ESCAPE(u.[Position], 'json'), '"') END,
                              ',"department":', CASE WHEN u.[Department] IS NULL THEN 'null'
                                                     ELSE CONCAT('"', STRING_ESCAPE(u.[Department], 'json'), '"') END,
                              ',"version":', u.[Version],
                              ',"source":"development-export-2026-09-25"}')
                FROM @users u
                INNER JOIN @insertedUsers i ON i.[SubjectId] = u.[SubjectId];

                INSERT INTO [audit].[ConfigurationAuditEvent]
                    ([Id], [AggregateType], [AggregateId], [EventType], [ActorId], [OccurredAt], [PayloadJson], [CorrelationId])
                SELECT e.[EventId], 'UserAccount', e.[AggregateId], 'user_account_seeded', @actor, @now, e.[PayloadJson], @correlation
                FROM @userEvents e;

                INSERT INTO [intg].[OutboxMessage]
                    ([Id], [AggregateId], [EventType], [PayloadJson], [OccurredAt], [ProcessedAt],
                     [Attempts], [NextAttemptAt], [LastError])
                SELECT e.[EventId], e.[AggregateId], 'user_account_seeded', e.[PayloadJson], @now, NULL, 0, NULL, NULL
                FROM @userEvents e;

                DECLARE @assignments TABLE
                (
                    [Id] uniqueidentifier NOT NULL PRIMARY KEY,
                    [SubjectId] nvarchar(200) NOT NULL,
                    [RoleCode] nvarchar(100) NOT NULL,
                    [ActionCodesJson] nvarchar(max) NOT NULL,
                    [AreaScoped] bit NOT NULL,
                    [EffectiveFrom] datetimeoffset NOT NULL,
                    [EffectiveUntil] datetimeoffset NULL,
                    [MakerId] nvarchar(200) NOT NULL,
                    [CheckerId] nvarchar(200) NOT NULL,
                    [ApprovedAt] datetimeoffset NOT NULL,
                    [CreatedAt] datetimeoffset NOT NULL,
                    [UpdatedAt] datetimeoffset NOT NULL
                );

                INSERT INTO @assignments
                    ([Id], [SubjectId], [RoleCode], [ActionCodesJson], [AreaScoped], [EffectiveFrom], [EffectiveUntil],
                     [MakerId], [CheckerId], [ApprovedAt], [CreatedAt], [UpdatedAt])
                VALUES
                    ('01A0C444-3330-754D-9781-A2371A5F306F', N'superadmin.local', N'Administrator', N'["admin.manage"]', 0, '2026-09-21T13:58:58.4856667Z', '2031-09-21T13:59:58.4856667Z', N'admin.maker.bootstrap', N'admin.checker.bootstrap', '2026-09-21T14:00:15.5045120Z', '2026-09-21T13:59:59.0244507Z', '2026-09-21T14:00:15.5045120Z'),
                    ('01A0C485-D8DB-7394-A78B-54E4A08BB353', N'yosep.zulkarnain', N'AreaOwnerManager', N'["permit.approve-and-issue","permit.closure.review","permit.renewal.review","permit.suspend","permit.suspension.resolve"]', 1, '2026-09-21T15:10:41.0078422Z', NULL, N'assignment.maker.bootstrap', N'assignment.checker.bootstrap', '2026-09-21T15:11:41.5294942Z', '2026-09-21T15:11:41.2753515Z', '2026-09-21T15:11:41.5294942Z'),
                    ('01A0C485-DA11-7AF7-B8CF-BDF78EC6C54B', N'ade.ruhimat', N'AreaOwnerSeniorOfficer', N'["permit.area-operations.review"]', 1, '2026-09-21T15:10:41.0078422Z', NULL, N'assignment.maker.bootstrap', N'assignment.checker.bootstrap', '2026-09-21T15:11:41.6224414Z', '2026-09-21T15:11:41.5850742Z', '2026-09-21T15:11:41.6224414Z'),
                    ('01A0C485-DA4A-7AC1-B698-F97ACD14A57E', N'erwin.jonathan', N'HSEValidator', N'["permit.suspend","permit.validate","permit.validation.escalate","permit.validation.reject","permit.validation.revision"]', 0, '2026-09-21T15:10:41.0078422Z', NULL, N'assignment.maker.bootstrap', N'assignment.checker.bootstrap', '2026-09-21T15:11:41.6781972Z', '2026-09-21T15:11:41.6422655Z', '2026-09-21T15:11:41.6781972Z'),
                    ('01A0C485-DA7E-79BA-921B-2FC75E108B3F', N'darsono4', N'HSEValidator', N'["permit.suspend","permit.validate","permit.validation.escalate","permit.validation.reject","permit.validation.revision"]', 0, '2026-09-21T15:10:41.0078422Z', NULL, N'assignment.maker.bootstrap', N'assignment.checker.bootstrap', '2026-09-21T15:11:41.7299365Z', '2026-09-21T15:11:41.6948841Z', '2026-09-21T15:11:41.7299365Z'),
                    ('01A0C485-DAB1-7130-9C5E-B54D93C9135D', N'aldi.setiawan', N'HSEValidator', N'["permit.suspend","permit.validate","permit.validation.escalate","permit.validation.reject","permit.validation.revision"]', 0, '2026-09-21T15:10:41.0078422Z', NULL, N'assignment.maker.bootstrap', N'assignment.checker.bootstrap', '2026-09-21T15:11:41.7828631Z', '2026-09-21T15:11:41.7458824Z', '2026-09-21T15:11:41.7828631Z'),
                    ('01A0C485-DAE9-723D-8CAE-D0C3C37E74B9', N'mk.ferry.febrian', N'HSEValidator', N'["permit.suspend","permit.validate","permit.validation.escalate","permit.validation.reject","permit.validation.revision"]', 0, '2026-09-21T15:10:41.0078422Z', NULL, N'assignment.maker.bootstrap', N'assignment.checker.bootstrap', '2026-09-21T15:11:41.8401666Z', '2026-09-21T15:11:41.8011390Z', '2026-09-21T15:11:41.8401666Z'),
                    ('01A0C496-EC2A-7A98-BF99-68C557044C5E', N'benny.sulistio', N'AreaOwnerSeniorOfficer', N'["permit.area-operations.review"]', 1, '2026-09-21T15:29:20.1615206Z', NULL, N'assignment.maker.bootstrap', N'assignment.checker.bootstrap', '2026-09-21T15:30:21.0853361Z', '2026-09-21T15:30:20.3299700Z', '2026-09-21T15:30:21.0853361Z'),
                    ('01A0C496-EF54-7548-8A97-6A6FE6FF7B43', N'muhamad.ibrahim', N'AreaOwnerSeniorOfficer', N'["permit.area-operations.review"]', 1, '2026-09-21T15:29:21.1252942Z', NULL, N'assignment.maker.bootstrap', N'assignment.checker.bootstrap', '2026-09-21T15:30:21.1975785Z', '2026-09-21T15:30:21.1407834Z', '2026-09-21T15:30:21.1975785Z'),
                    ('01A0C4B9-C20B-7D59-BB16-C20CDD1B75D0', N'mk.revanza.raytama', N'Sponsor', N'["permit.cancel","permit.closure.request","permit.create","permit.renewal.request","permit.submit","permit.update"]', 0, '2026-09-20T16:08:00Z', NULL, N'superadmin.local', N'superadmin.local', '2026-09-21T16:17:42.5918903Z', '2026-09-21T16:08:23.3070185Z', '2026-09-21T16:17:42.5918903Z');

                DECLARE @versions TABLE
                (
                    [Id] uniqueidentifier NOT NULL PRIMARY KEY,
                    [UserAuthorizationId] uniqueidentifier NOT NULL,
                    [Version] int NOT NULL,
                    [ContentJson] nvarchar(max) NOT NULL,
                    [CreatedAt] datetimeoffset NOT NULL,
                    [CreatedBy] nvarchar(200) NOT NULL
                );

                INSERT INTO @versions ([Id], [UserAuthorizationId], [Version], [ContentJson], [CreatedAt], [CreatedBy])
                VALUES
                    ('01A0C444-339B-7C11-A509-B14625A10EC8', '01A0C444-3330-754D-9781-A2371A5F306F', 1, N'{"id":"01a0c444-3330-754d-9781-a2371a5f306f","subjectId":"superadmin.local","roleCode":"Administrator","actionCodes":["admin.manage"],"locationId":null,"includeDescendants":false,"requiredCompetencyCodes":[],"kind":0,"sourceAuthorizationId":null,"effectiveFrom":"2026-09-21T13:58:58.4856667+00:00","effectiveUntil":"2031-09-21T13:59:58.4856667+00:00","status":0,"version":1,"makerId":"admin.maker.bootstrap","checkerId":null,"approvedAt":null,"createdAt":"2026-09-21T13:59:59.0244507+00:00","updatedAt":"2026-09-21T13:59:59.0244507+00:00"}', '2026-09-21T13:59:59.0244507Z', N'admin.maker.bootstrap'),
                    ('01A0C444-72EB-7F02-92FB-9192385525DC', '01A0C444-3330-754D-9781-A2371A5F306F', 2, N'{"id":"01a0c444-3330-754d-9781-a2371a5f306f","subjectId":"superadmin.local","roleCode":"Administrator","actionCodes":["admin.manage"],"locationId":null,"includeDescendants":false,"requiredCompetencyCodes":[],"kind":0,"sourceAuthorizationId":null,"effectiveFrom":"2026-09-21T13:58:58.4856667+00:00","effectiveUntil":"2031-09-21T13:59:58.4856667+00:00","status":1,"version":2,"makerId":"admin.maker.bootstrap","checkerId":null,"approvedAt":null,"createdAt":"2026-09-21T13:59:59.0244507+00:00","updatedAt":"2026-09-21T14:00:15.2832481+00:00"}', '2026-09-21T14:00:15.2832481Z', N'admin.maker.bootstrap'),
                    ('01A0C444-7395-7A1F-9CE1-130A942654DF', '01A0C444-3330-754D-9781-A2371A5F306F', 3, N'{"id":"01a0c444-3330-754d-9781-a2371a5f306f","subjectId":"superadmin.local","roleCode":"Administrator","actionCodes":["admin.manage"],"locationId":null,"includeDescendants":false,"requiredCompetencyCodes":[],"kind":0,"sourceAuthorizationId":null,"effectiveFrom":"2026-09-21T13:58:58.4856667+00:00","effectiveUntil":"2031-09-21T13:59:58.4856667+00:00","status":2,"version":3,"makerId":"admin.maker.bootstrap","checkerId":"admin.checker.bootstrap","approvedAt":"2026-09-21T14:00:15.504512+00:00","createdAt":"2026-09-21T13:59:59.0244507+00:00","updatedAt":"2026-09-21T14:00:15.504512+00:00"}', '2026-09-21T14:00:15.5045120Z', N'admin.checker.bootstrap'),
                    ('01A0C485-D915-7DE6-B80F-3111289C49C5', '01A0C485-D8DB-7394-A78B-54E4A08BB353', 1, N'{"id":"01a0c485-d8db-7394-a78b-54e4a08bb353","subjectId":"yosep.zulkarnain","roleCode":"AreaOwnerManager","actionCodes":["permit.approve-and-issue","permit.closure.review","permit.renewal.review","permit.suspend","permit.suspension.resolve"],"locationId":"01a0a755-d839-7764-b2e5-8d0b7b9bece0","includeDescendants":false,"requiredCompetencyCodes":[],"kind":0,"sourceAuthorizationId":null,"effectiveFrom":"2026-09-21T15:10:41.0078422+00:00","effectiveUntil":null,"status":0,"version":1,"makerId":"assignment.maker.bootstrap","checkerId":null,"approvedAt":null,"createdAt":"2026-09-21T15:11:41.2753515+00:00","updatedAt":"2026-09-21T15:11:41.2753515+00:00"}', '2026-09-21T15:11:41.2753515Z', N'assignment.maker.bootstrap'),
                    ('01A0C485-D9A1-781A-A3C3-6534DBBC3F1E', '01A0C485-D8DB-7394-A78B-54E4A08BB353', 2, N'{"id":"01a0c485-d8db-7394-a78b-54e4a08bb353","subjectId":"yosep.zulkarnain","roleCode":"AreaOwnerManager","actionCodes":["permit.approve-and-issue","permit.closure.review","permit.renewal.review","permit.suspend","permit.suspension.resolve"],"locationId":"01a0a755-d839-7764-b2e5-8d0b7b9bece0","includeDescendants":false,"requiredCompetencyCodes":[],"kind":0,"sourceAuthorizationId":null,"effectiveFrom":"2026-09-21T15:10:41.0078422+00:00","effectiveUntil":null,"status":1,"version":2,"makerId":"assignment.maker.bootstrap","checkerId":null,"approvedAt":null,"createdAt":"2026-09-21T15:11:41.2753515+00:00","updatedAt":"2026-09-21T15:11:41.4545311+00:00"}', '2026-09-21T15:11:41.4545311Z', N'assignment.maker.bootstrap'),
                    ('01A0C485-D9DF-7407-9AE5-5726AE2284E8', '01A0C485-D8DB-7394-A78B-54E4A08BB353', 3, N'{"id":"01a0c485-d8db-7394-a78b-54e4a08bb353","subjectId":"yosep.zulkarnain","roleCode":"AreaOwnerManager","actionCodes":["permit.approve-and-issue","permit.closure.review","permit.renewal.review","permit.suspend","permit.suspension.resolve"],"locationId":"01a0a755-d839-7764-b2e5-8d0b7b9bece0","includeDescendants":false,"requiredCompetencyCodes":[],"kind":0,"sourceAuthorizationId":null,"effectiveFrom":"2026-09-21T15:10:41.0078422+00:00","effectiveUntil":null,"status":2,"version":3,"makerId":"assignment.maker.bootstrap","checkerId":"assignment.checker.bootstrap","approvedAt":"2026-09-21T15:11:41.5294942+00:00","createdAt":"2026-09-21T15:11:41.2753515+00:00","updatedAt":"2026-09-21T15:11:41.5294942+00:00"}', '2026-09-21T15:11:41.5294942Z', N'assignment.checker.bootstrap'),
                    ('01A0C485-DA11-7BFB-B8B2-1371815D5DC7', '01A0C485-DA11-7AF7-B8CF-BDF78EC6C54B', 1, N'{"id":"01a0c485-da11-7af7-b8cf-bdf78ec6c54b","subjectId":"ade.ruhimat","roleCode":"AreaOwnerSeniorOfficer","actionCodes":["permit.area-operations.review"],"locationId":"01a0a755-d839-7764-b2e5-8d0b7b9bece0","includeDescendants":false,"requiredCompetencyCodes":[],"kind":0,"sourceAuthorizationId":null,"effectiveFrom":"2026-09-21T15:10:41.0078422+00:00","effectiveUntil":null,"status":0,"version":1,"makerId":"assignment.maker.bootstrap","checkerId":null,"approvedAt":null,"createdAt":"2026-09-21T15:11:41.5850742+00:00","updatedAt":"2026-09-21T15:11:41.5850742+00:00"}', '2026-09-21T15:11:41.5850742Z', N'assignment.maker.bootstrap'),
                    ('01A0C485-DA25-7802-BBB8-BE74778ADFEA', '01A0C485-DA11-7AF7-B8CF-BDF78EC6C54B', 2, N'{"id":"01a0c485-da11-7af7-b8cf-bdf78ec6c54b","subjectId":"ade.ruhimat","roleCode":"AreaOwnerSeniorOfficer","actionCodes":["permit.area-operations.review"],"locationId":"01a0a755-d839-7764-b2e5-8d0b7b9bece0","includeDescendants":false,"requiredCompetencyCodes":[],"kind":0,"sourceAuthorizationId":null,"effectiveFrom":"2026-09-21T15:10:41.0078422+00:00","effectiveUntil":null,"status":1,"version":2,"makerId":"assignment.maker.bootstrap","checkerId":null,"approvedAt":null,"createdAt":"2026-09-21T15:11:41.5850742+00:00","updatedAt":"2026-09-21T15:11:41.6038159+00:00"}', '2026-09-21T15:11:41.6038159Z', N'assignment.maker.bootstrap'),
                    ('01A0C485-DA39-744B-A49E-7E18882D03F4', '01A0C485-DA11-7AF7-B8CF-BDF78EC6C54B', 3, N'{"id":"01a0c485-da11-7af7-b8cf-bdf78ec6c54b","subjectId":"ade.ruhimat","roleCode":"AreaOwnerSeniorOfficer","actionCodes":["permit.area-operations.review"],"locationId":"01a0a755-d839-7764-b2e5-8d0b7b9bece0","includeDescendants":false,"requiredCompetencyCodes":[],"kind":0,"sourceAuthorizationId":null,"effectiveFrom":"2026-09-21T15:10:41.0078422+00:00","effectiveUntil":null,"status":2,"version":3,"makerId":"assignment.maker.bootstrap","checkerId":"assignment.checker.bootstrap","approvedAt":"2026-09-21T15:11:41.6224414+00:00","createdAt":"2026-09-21T15:11:41.5850742+00:00","updatedAt":"2026-09-21T15:11:41.6224414+00:00"}', '2026-09-21T15:11:41.6224414Z', N'assignment.checker.bootstrap'),
                    ('01A0C485-DA4B-7DEF-93CA-6F11EC9112FA', '01A0C485-DA4A-7AC1-B698-F97ACD14A57E', 1, N'{"id":"01a0c485-da4a-7ac1-b698-f97acd14a57e","subjectId":"erwin.jonathan","roleCode":"HSEValidator","actionCodes":["permit.suspend","permit.validate","permit.validation.escalate","permit.validation.reject","permit.validation.revision"],"locationId":null,"includeDescendants":false,"requiredCompetencyCodes":[],"kind":0,"sourceAuthorizationId":null,"effectiveFrom":"2026-09-21T15:10:41.0078422+00:00","effectiveUntil":null,"status":0,"version":1,"makerId":"assignment.maker.bootstrap","checkerId":null,"approvedAt":null,"createdAt":"2026-09-21T15:11:41.6422655+00:00","updatedAt":"2026-09-21T15:11:41.6422655+00:00"}', '2026-09-21T15:11:41.6422655Z', N'assignment.maker.bootstrap'),
                    ('01A0C485-DA5D-75D1-9DFC-D28A37E2D93C', '01A0C485-DA4A-7AC1-B698-F97ACD14A57E', 2, N'{"id":"01a0c485-da4a-7ac1-b698-f97acd14a57e","subjectId":"erwin.jonathan","roleCode":"HSEValidator","actionCodes":["permit.suspend","permit.validate","permit.validation.escalate","permit.validation.reject","permit.validation.revision"],"locationId":null,"includeDescendants":false,"requiredCompetencyCodes":[],"kind":0,"sourceAuthorizationId":null,"effectiveFrom":"2026-09-21T15:10:41.0078422+00:00","effectiveUntil":null,"status":1,"version":2,"makerId":"assignment.maker.bootstrap","checkerId":null,"approvedAt":null,"createdAt":"2026-09-21T15:11:41.6422655+00:00","updatedAt":"2026-09-21T15:11:41.6600136+00:00"}', '2026-09-21T15:11:41.6600136Z', N'assignment.maker.bootstrap'),
                    ('01A0C485-DA70-734D-B106-4B70C371B115', '01A0C485-DA4A-7AC1-B698-F97ACD14A57E', 3, N'{"id":"01a0c485-da4a-7ac1-b698-f97acd14a57e","subjectId":"erwin.jonathan","roleCode":"HSEValidator","actionCodes":["permit.suspend","permit.validate","permit.validation.escalate","permit.validation.reject","permit.validation.revision"],"locationId":null,"includeDescendants":false,"requiredCompetencyCodes":[],"kind":0,"sourceAuthorizationId":null,"effectiveFrom":"2026-09-21T15:10:41.0078422+00:00","effectiveUntil":null,"status":2,"version":3,"makerId":"assignment.maker.bootstrap","checkerId":"assignment.checker.bootstrap","approvedAt":"2026-09-21T15:11:41.6781972+00:00","createdAt":"2026-09-21T15:11:41.6422655+00:00","updatedAt":"2026-09-21T15:11:41.6781972+00:00"}', '2026-09-21T15:11:41.6781972Z', N'assignment.checker.bootstrap'),
                    ('01A0C485-DA7F-7C4A-87F8-EDD084250847', '01A0C485-DA7E-79BA-921B-2FC75E108B3F', 1, N'{"id":"01a0c485-da7e-79ba-921b-2fc75e108b3f","subjectId":"darsono4","roleCode":"HSEValidator","actionCodes":["permit.suspend","permit.validate","permit.validation.escalate","permit.validation.reject","permit.validation.revision"],"locationId":null,"includeDescendants":false,"requiredCompetencyCodes":[],"kind":0,"sourceAuthorizationId":null,"effectiveFrom":"2026-09-21T15:10:41.0078422+00:00","effectiveUntil":null,"status":0,"version":1,"makerId":"assignment.maker.bootstrap","checkerId":null,"approvedAt":null,"createdAt":"2026-09-21T15:11:41.6948841+00:00","updatedAt":"2026-09-21T15:11:41.6948841+00:00"}', '2026-09-21T15:11:41.6948841Z', N'assignment.maker.bootstrap'),
                    ('01A0C485-DA91-7406-B840-B1E9DEE7EB87', '01A0C485-DA7E-79BA-921B-2FC75E108B3F', 2, N'{"id":"01a0c485-da7e-79ba-921b-2fc75e108b3f","subjectId":"darsono4","roleCode":"HSEValidator","actionCodes":["permit.suspend","permit.validate","permit.validation.escalate","permit.validation.reject","permit.validation.revision"],"locationId":null,"includeDescendants":false,"requiredCompetencyCodes":[],"kind":0,"sourceAuthorizationId":null,"effectiveFrom":"2026-09-21T15:10:41.0078422+00:00","effectiveUntil":null,"status":1,"version":2,"makerId":"assignment.maker.bootstrap","checkerId":null,"approvedAt":null,"createdAt":"2026-09-21T15:11:41.6948841+00:00","updatedAt":"2026-09-21T15:11:41.7121908+00:00"}', '2026-09-21T15:11:41.7121908Z', N'assignment.maker.bootstrap'),
                    ('01A0C485-DAA3-7F9A-A225-7EBA3737FB81', '01A0C485-DA7E-79BA-921B-2FC75E108B3F', 3, N'{"id":"01a0c485-da7e-79ba-921b-2fc75e108b3f","subjectId":"darsono4","roleCode":"HSEValidator","actionCodes":["permit.suspend","permit.validate","permit.validation.escalate","permit.validation.reject","permit.validation.revision"],"locationId":null,"includeDescendants":false,"requiredCompetencyCodes":[],"kind":0,"sourceAuthorizationId":null,"effectiveFrom":"2026-09-21T15:10:41.0078422+00:00","effectiveUntil":null,"status":2,"version":3,"makerId":"assignment.maker.bootstrap","checkerId":"assignment.checker.bootstrap","approvedAt":"2026-09-21T15:11:41.7299365+00:00","createdAt":"2026-09-21T15:11:41.6948841+00:00","updatedAt":"2026-09-21T15:11:41.7299365+00:00"}', '2026-09-21T15:11:41.7299365Z', N'assignment.checker.bootstrap'),
                    ('01A0C485-DAB2-7BD0-838F-A88643D7C952', '01A0C485-DAB1-7130-9C5E-B54D93C9135D', 1, N'{"id":"01a0c485-dab1-7130-9c5e-b54d93c9135d","subjectId":"aldi.setiawan","roleCode":"HSEValidator","actionCodes":["permit.suspend","permit.validate","permit.validation.escalate","permit.validation.reject","permit.validation.revision"],"locationId":null,"includeDescendants":false,"requiredCompetencyCodes":[],"kind":0,"sourceAuthorizationId":null,"effectiveFrom":"2026-09-21T15:10:41.0078422+00:00","effectiveUntil":null,"status":0,"version":1,"makerId":"assignment.maker.bootstrap","checkerId":null,"approvedAt":null,"createdAt":"2026-09-21T15:11:41.7458824+00:00","updatedAt":"2026-09-21T15:11:41.7458824+00:00"}', '2026-09-21T15:11:41.7458824Z', N'assignment.maker.bootstrap'),
                    ('01A0C485-DAC5-7B42-BE72-37875C5B51E1', '01A0C485-DAB1-7130-9C5E-B54D93C9135D', 2, N'{"id":"01a0c485-dab1-7130-9c5e-b54d93c9135d","subjectId":"aldi.setiawan","roleCode":"HSEValidator","actionCodes":["permit.suspend","permit.validate","permit.validation.escalate","permit.validation.reject","permit.validation.revision"],"locationId":null,"includeDescendants":false,"requiredCompetencyCodes":[],"kind":0,"sourceAuthorizationId":null,"effectiveFrom":"2026-09-21T15:10:41.0078422+00:00","effectiveUntil":null,"status":1,"version":2,"makerId":"assignment.maker.bootstrap","checkerId":null,"approvedAt":null,"createdAt":"2026-09-21T15:11:41.7458824+00:00","updatedAt":"2026-09-21T15:11:41.7642997+00:00"}', '2026-09-21T15:11:41.7642997Z', N'assignment.maker.bootstrap'),
                    ('01A0C485-DAD8-7A8E-AF6E-3A5B4F3131C9', '01A0C485-DAB1-7130-9C5E-B54D93C9135D', 3, N'{"id":"01a0c485-dab1-7130-9c5e-b54d93c9135d","subjectId":"aldi.setiawan","roleCode":"HSEValidator","actionCodes":["permit.suspend","permit.validate","permit.validation.escalate","permit.validation.reject","permit.validation.revision"],"locationId":null,"includeDescendants":false,"requiredCompetencyCodes":[],"kind":0,"sourceAuthorizationId":null,"effectiveFrom":"2026-09-21T15:10:41.0078422+00:00","effectiveUntil":null,"status":2,"version":3,"makerId":"assignment.maker.bootstrap","checkerId":"assignment.checker.bootstrap","approvedAt":"2026-09-21T15:11:41.7828631+00:00","createdAt":"2026-09-21T15:11:41.7458824+00:00","updatedAt":"2026-09-21T15:11:41.7828631+00:00"}', '2026-09-21T15:11:41.7828631Z', N'assignment.checker.bootstrap'),
                    ('01A0C485-DAE9-7D48-AB81-22AA39E43A25', '01A0C485-DAE9-723D-8CAE-D0C3C37E74B9', 1, N'{"id":"01a0c485-dae9-723d-8cae-d0c3c37e74b9","subjectId":"mk.ferry.febrian","roleCode":"HSEValidator","actionCodes":["permit.suspend","permit.validate","permit.validation.escalate","permit.validation.reject","permit.validation.revision"],"locationId":null,"includeDescendants":false,"requiredCompetencyCodes":[],"kind":0,"sourceAuthorizationId":null,"effectiveFrom":"2026-09-21T15:10:41.0078422+00:00","effectiveUntil":null,"status":0,"version":1,"makerId":"assignment.maker.bootstrap","checkerId":null,"approvedAt":null,"createdAt":"2026-09-21T15:11:41.801139+00:00","updatedAt":"2026-09-21T15:11:41.801139+00:00"}', '2026-09-21T15:11:41.8011390Z', N'assignment.maker.bootstrap'),
                    ('01A0C485-DAFE-7B62-AADD-E5100BEAE556', '01A0C485-DAE9-723D-8CAE-D0C3C37E74B9', 2, N'{"id":"01a0c485-dae9-723d-8cae-d0c3c37e74b9","subjectId":"mk.ferry.febrian","roleCode":"HSEValidator","actionCodes":["permit.suspend","permit.validate","permit.validation.escalate","permit.validation.reject","permit.validation.revision"],"locationId":null,"includeDescendants":false,"requiredCompetencyCodes":[],"kind":0,"sourceAuthorizationId":null,"effectiveFrom":"2026-09-21T15:10:41.0078422+00:00","effectiveUntil":null,"status":1,"version":2,"makerId":"assignment.maker.bootstrap","checkerId":null,"approvedAt":null,"createdAt":"2026-09-21T15:11:41.801139+00:00","updatedAt":"2026-09-21T15:11:41.8209195+00:00"}', '2026-09-21T15:11:41.8209195Z', N'assignment.maker.bootstrap'),
                    ('01A0C485-DB11-7016-ABDD-923B61A4257A', '01A0C485-DAE9-723D-8CAE-D0C3C37E74B9', 3, N'{"id":"01a0c485-dae9-723d-8cae-d0c3c37e74b9","subjectId":"mk.ferry.febrian","roleCode":"HSEValidator","actionCodes":["permit.suspend","permit.validate","permit.validation.escalate","permit.validation.reject","permit.validation.revision"],"locationId":null,"includeDescendants":false,"requiredCompetencyCodes":[],"kind":0,"sourceAuthorizationId":null,"effectiveFrom":"2026-09-21T15:10:41.0078422+00:00","effectiveUntil":null,"status":2,"version":3,"makerId":"assignment.maker.bootstrap","checkerId":"assignment.checker.bootstrap","approvedAt":"2026-09-21T15:11:41.8401666+00:00","createdAt":"2026-09-21T15:11:41.801139+00:00","updatedAt":"2026-09-21T15:11:41.8401666+00:00"}', '2026-09-21T15:11:41.8401666Z', N'assignment.checker.bootstrap'),
                    ('01A0C496-ECD6-7565-80F0-46599B5BB0D2', '01A0C496-EC2A-7A98-BF99-68C557044C5E', 1, N'{"id":"01a0c496-ec2a-7a98-bf99-68c557044c5e","subjectId":"benny.sulistio","roleCode":"AreaOwnerSeniorOfficer","actionCodes":["permit.area-operations.review"],"locationId":"01a0a755-d839-7764-b2e5-8d0b7b9bece0","includeDescendants":false,"requiredCompetencyCodes":[],"kind":0,"sourceAuthorizationId":null,"effectiveFrom":"2026-09-21T15:29:20.1615206+00:00","effectiveUntil":null,"status":0,"version":1,"makerId":"assignment.maker.bootstrap","checkerId":null,"approvedAt":null,"createdAt":"2026-09-21T15:30:20.32997+00:00","updatedAt":"2026-09-21T15:30:20.32997+00:00"}', '2026-09-21T15:30:20.3299700Z', N'assignment.maker.bootstrap'),
                    ('01A0C496-EEC5-7804-A38B-19A6DA0F7C55', '01A0C496-EC2A-7A98-BF99-68C557044C5E', 2, N'{"id":"01a0c496-ec2a-7a98-bf99-68c557044c5e","subjectId":"benny.sulistio","roleCode":"AreaOwnerSeniorOfficer","actionCodes":["permit.area-operations.review"],"locationId":"01a0a755-d839-7764-b2e5-8d0b7b9bece0","includeDescendants":false,"requiredCompetencyCodes":[],"kind":0,"sourceAuthorizationId":null,"effectiveFrom":"2026-09-21T15:29:20.1615206+00:00","effectiveUntil":null,"status":1,"version":2,"makerId":"assignment.maker.bootstrap","checkerId":null,"approvedAt":null,"createdAt":"2026-09-21T15:30:20.32997+00:00","updatedAt":"2026-09-21T15:30:20.9704944+00:00"}', '2026-09-21T15:30:20.9704944Z', N'assignment.maker.bootstrap'),
                    ('01A0C496-EF25-75B5-84D1-4C3D7D980377', '01A0C496-EC2A-7A98-BF99-68C557044C5E', 3, N'{"id":"01a0c496-ec2a-7a98-bf99-68c557044c5e","subjectId":"benny.sulistio","roleCode":"AreaOwnerSeniorOfficer","actionCodes":["permit.area-operations.review"],"locationId":"01a0a755-d839-7764-b2e5-8d0b7b9bece0","includeDescendants":false,"requiredCompetencyCodes":[],"kind":0,"sourceAuthorizationId":null,"effectiveFrom":"2026-09-21T15:29:20.1615206+00:00","effectiveUntil":null,"status":2,"version":3,"makerId":"assignment.maker.bootstrap","checkerId":"assignment.checker.bootstrap","approvedAt":"2026-09-21T15:30:21.0853361+00:00","createdAt":"2026-09-21T15:30:20.32997+00:00","updatedAt":"2026-09-21T15:30:21.0853361+00:00"}', '2026-09-21T15:30:21.0853361Z', N'assignment.checker.bootstrap'),
                    ('01A0C496-EF55-7461-A81D-C5C1D3C0D065', '01A0C496-EF54-7548-8A97-6A6FE6FF7B43', 1, N'{"id":"01a0c496-ef54-7548-8a97-6a6fe6ff7b43","subjectId":"muhamad.ibrahim","roleCode":"AreaOwnerSeniorOfficer","actionCodes":["permit.area-operations.review"],"locationId":"01a0a755-d839-7764-b2e5-8d0b7b9bece0","includeDescendants":false,"requiredCompetencyCodes":[],"kind":0,"sourceAuthorizationId":null,"effectiveFrom":"2026-09-21T15:29:21.1252942+00:00","effectiveUntil":null,"status":0,"version":1,"makerId":"assignment.maker.bootstrap","checkerId":null,"approvedAt":null,"createdAt":"2026-09-21T15:30:21.1407834+00:00","updatedAt":"2026-09-21T15:30:21.1407834+00:00"}', '2026-09-21T15:30:21.1407834Z', N'assignment.maker.bootstrap'),
                    ('01A0C496-EF71-78B7-B822-CB1DF7897145', '01A0C496-EF54-7548-8A97-6A6FE6FF7B43', 2, N'{"id":"01a0c496-ef54-7548-8a97-6a6fe6ff7b43","subjectId":"muhamad.ibrahim","roleCode":"AreaOwnerSeniorOfficer","actionCodes":["permit.area-operations.review"],"locationId":"01a0a755-d839-7764-b2e5-8d0b7b9bece0","includeDescendants":false,"requiredCompetencyCodes":[],"kind":0,"sourceAuthorizationId":null,"effectiveFrom":"2026-09-21T15:29:21.1252942+00:00","effectiveUntil":null,"status":1,"version":2,"makerId":"assignment.maker.bootstrap","checkerId":null,"approvedAt":null,"createdAt":"2026-09-21T15:30:21.1407834+00:00","updatedAt":"2026-09-21T15:30:21.1674824+00:00"}', '2026-09-21T15:30:21.1674824Z', N'assignment.maker.bootstrap'),
                    ('01A0C496-EF91-7F85-BC36-56EEAB2DC53B', '01A0C496-EF54-7548-8A97-6A6FE6FF7B43', 3, N'{"id":"01a0c496-ef54-7548-8a97-6a6fe6ff7b43","subjectId":"muhamad.ibrahim","roleCode":"AreaOwnerSeniorOfficer","actionCodes":["permit.area-operations.review"],"locationId":"01a0a755-d839-7764-b2e5-8d0b7b9bece0","includeDescendants":false,"requiredCompetencyCodes":[],"kind":0,"sourceAuthorizationId":null,"effectiveFrom":"2026-09-21T15:29:21.1252942+00:00","effectiveUntil":null,"status":2,"version":3,"makerId":"assignment.maker.bootstrap","checkerId":"assignment.checker.bootstrap","approvedAt":"2026-09-21T15:30:21.1975785+00:00","createdAt":"2026-09-21T15:30:21.1407834+00:00","updatedAt":"2026-09-21T15:30:21.1975785+00:00"}', '2026-09-21T15:30:21.1975785Z', N'assignment.checker.bootstrap'),
                    ('01A0C4B9-C214-79B7-A974-78E787335122', '01A0C4B9-C20B-7D59-BB16-C20CDD1B75D0', 1, N'{"id":"01a0c4b9-c20b-7d59-bb16-c20cdd1b75d0","subjectId":"mk.revanza.raytama","roleCode":"Sponsor","actionCodes":["permit.cancel","permit.closure.request","permit.create","permit.renewal.request","permit.submit","permit.update"],"locationId":null,"includeDescendants":false,"requiredCompetencyCodes":[],"kind":0,"sourceAuthorizationId":null,"effectiveFrom":"2026-09-20T16:08:00+00:00","effectiveUntil":null,"status":0,"version":1,"makerId":"superadmin.local","checkerId":null,"approvedAt":null,"createdAt":"2026-09-21T16:08:23.3070185+00:00","updatedAt":"2026-09-21T16:08:23.3070185+00:00"}', '2026-09-21T16:08:23.3070185Z', N'superadmin.local'),
                    ('01A0C4B9-DAC8-752D-BFF4-F2B6EA84FEAA', '01A0C4B9-C20B-7D59-BB16-C20CDD1B75D0', 2, N'{"id":"01a0c4b9-c20b-7d59-bb16-c20cdd1b75d0","subjectId":"mk.revanza.raytama","roleCode":"Sponsor","actionCodes":["permit.cancel","permit.closure.request","permit.create","permit.renewal.request","permit.submit","permit.update"],"locationId":null,"includeDescendants":false,"requiredCompetencyCodes":[],"kind":0,"sourceAuthorizationId":null,"effectiveFrom":"2026-09-20T16:08:00+00:00","effectiveUntil":null,"status":1,"version":2,"makerId":"superadmin.local","checkerId":null,"approvedAt":null,"createdAt":"2026-09-21T16:08:23.3070185+00:00","updatedAt":"2026-09-21T16:08:29.6364876+00:00"}', '2026-09-21T16:08:29.6364876Z', N'superadmin.local'),
                    ('01A0C4C2-4B2D-7BC8-A483-9277A7171FC9', '01A0C4B9-C20B-7D59-BB16-C20CDD1B75D0', 3, N'{"id":"01a0c4b9-c20b-7d59-bb16-c20cdd1b75d0","subjectId":"mk.revanza.raytama","roleCode":"Sponsor","actionCodes":["permit.cancel","permit.closure.request","permit.create","permit.renewal.request","permit.submit","permit.update"],"locationId":null,"includeDescendants":false,"requiredCompetencyCodes":[],"kind":0,"sourceAuthorizationId":null,"effectiveFrom":"2026-09-20T16:08:00+00:00","effectiveUntil":null,"status":2,"version":3,"makerId":"superadmin.local","checkerId":"superadmin.local","approvedAt":"2026-09-21T16:17:42.5918903+00:00","createdAt":"2026-09-21T16:08:23.3070185+00:00","updatedAt":"2026-09-21T16:17:42.5918903+00:00"}', '2026-09-21T16:17:42.5918903Z', N'superadmin.local');

                DECLARE @outcome TABLE
                (
                    [Id] uniqueidentifier NOT NULL PRIMARY KEY,
                    [SkipReason] varchar(40) NULL
                );

                -- Fail closed: an assignment is only seeded when its account exists, its area exists,
                -- and the subject does not already hold an approved assignment for the same role.
                INSERT INTO @outcome ([Id], [SkipReason])
                SELECT a.[Id],
                       CASE
                           WHEN EXISTS (SELECT 1 FROM [sec].[UserAuthorization] x WHERE x.[Id] = a.[Id])
                               THEN 'already_present'
                           WHEN NOT EXISTS (SELECT 1 FROM [sec].[UserAccount] u WHERE u.[SubjectId] = a.[SubjectId])
                               THEN 'user_account_missing'
                           WHEN a.[AreaScoped] = 1 AND @orfLocationId IS NULL
                               THEN 'location_not_found'
                           WHEN EXISTS (
                                    SELECT 1
                                    FROM [sec].[UserAuthorization] x
                                    WHERE x.[SubjectId] = a.[SubjectId]
                                      AND x.[RoleCode] = a.[RoleCode]
                                      AND x.[Status] = 'Approved')
                               THEN 'approved_role_exists'
                           ELSE NULL
                       END
                FROM @assignments a;

                INSERT INTO [sec].[UserAuthorization]
                    ([Id], [SubjectId], [RoleCode], [ActionCodesJson], [LocationId], [IncludeDescendants],
                     [RequiredCompetencyCodesJson], [Kind], [SourceAuthorizationId], [EffectiveFrom], [EffectiveUntil],
                     [Status], [Version], [MakerId], [CheckerId], [ApprovedAt], [CreatedAt], [UpdatedAt])
                SELECT a.[Id], a.[SubjectId], a.[RoleCode], a.[ActionCodesJson],
                       CASE WHEN a.[AreaScoped] = 1 THEN @orfLocationId ELSE NULL END, 0,
                       '[]', 'Direct', NULL, a.[EffectiveFrom], a.[EffectiveUntil],
                       'Approved', 3, a.[MakerId], a.[CheckerId], a.[ApprovedAt], a.[CreatedAt], a.[UpdatedAt]
                FROM @assignments a
                INNER JOIN @outcome o ON o.[Id] = a.[Id]
                WHERE o.[SkipReason] IS NULL;

                -- Snapshot JSON is ASCII, so hashing its varchar form equals the UTF-8 hash the store computes.
                INSERT INTO [sec].[UserAuthorizationVersion]
                    ([Id], [UserAuthorizationId], [Version], [ContentJson], [ContentHash], [CreatedAt], [CreatedBy])
                SELECT v.[Id], v.[UserAuthorizationId], v.[Version], j.[ContentJson],
                       CONVERT(varchar(64), HASHBYTES('SHA2_256', CONVERT(varchar(max), j.[ContentJson])), 2),
                       v.[CreatedAt], v.[CreatedBy]
                FROM @versions v
                INNER JOIN @assignments a ON a.[Id] = v.[UserAuthorizationId]
                INNER JOIN @outcome o ON o.[Id] = a.[Id]
                CROSS APPLY (
                    SELECT CASE WHEN a.[AreaScoped] = 1
                                THEN REPLACE(v.[ContentJson], @sourceLocationId, LOWER(CONVERT(varchar(36), @orfLocationId)))
                                ELSE v.[ContentJson]
                           END AS [ContentJson]) j
                WHERE o.[SkipReason] IS NULL;

                DECLARE @assignmentEvents TABLE
                (
                    [EventId] uniqueidentifier NOT NULL PRIMARY KEY,
                    [AggregateId] uniqueidentifier NOT NULL,
                    [EventType] nvarchar(100) NOT NULL,
                    [PayloadJson] nvarchar(max) NOT NULL
                );

                INSERT INTO @assignmentEvents ([EventId], [AggregateId], [EventType], [PayloadJson])
                SELECT NEWID(), a.[Id],
                       CASE WHEN o.[SkipReason] IS NULL THEN 'authorization_seeded' ELSE 'authorization_seed_skipped' END,
                       CONCAT('{"subjectId":"', STRING_ESCAPE(a.[SubjectId], 'json'),
                              '","roleCode":"', STRING_ESCAPE(a.[RoleCode], 'json'),
                              '","locationId":', CASE WHEN a.[AreaScoped] = 1 AND o.[SkipReason] IS NULL
                                                      THEN CONCAT('"', LOWER(CONVERT(varchar(36), @orfLocationId)), '"')
                                                      ELSE 'null' END,
                              ',"locationCode":', CASE WHEN a.[AreaScoped] = 1 THEN '"ORF"' ELSE 'null' END,
                              ',"version":3,"makerId":"', STRING_ESCAPE(a.[MakerId], 'json'),
                              '","checkerId":"', STRING_ESCAPE(a.[CheckerId], 'json'),
                              '","reason":', CASE WHEN o.[SkipReason] IS NULL THEN 'null' ELSE CONCAT('"', o.[SkipReason], '"') END,
                              ',"source":"development-export-2026-09-25"}')
                FROM @assignments a
                INNER JOIN @outcome o ON o.[Id] = a.[Id]
                WHERE o.[SkipReason] IS NULL OR o.[SkipReason] <> 'already_present';

                INSERT INTO [audit].[ConfigurationAuditEvent]
                    ([Id], [AggregateType], [AggregateId], [EventType], [ActorId], [OccurredAt], [PayloadJson], [CorrelationId])
                SELECT e.[EventId], 'UserAuthorization', e.[AggregateId], e.[EventType], @actor, @now, e.[PayloadJson], @correlation
                FROM @assignmentEvents e;

                INSERT INTO [intg].[OutboxMessage]
                    ([Id], [AggregateId], [EventType], [PayloadJson], [OccurredAt], [ProcessedAt],
                     [Attempts], [NextAttemptAt], [LastError])
                SELECT e.[EventId], e.[AggregateId], e.[EventType], e.[PayloadJson], @now, NULL, 0, NULL, NULL
                FROM @assignmentEvents e
                WHERE e.[EventType] = 'authorization_seeded';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Seeded accounts and assignments are business data with audit history; a rollback must
            // not delete them.
        }
    }
}
