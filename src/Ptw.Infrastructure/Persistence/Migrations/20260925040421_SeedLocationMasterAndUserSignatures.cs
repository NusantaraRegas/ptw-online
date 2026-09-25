using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ptw.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeedLocationMasterAndUserSignatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Completes the reference-data seed started by SeedUserAccountsAndRoleAssignments with
            // the remaining non-permit data exported from the Development database on 2026-09-25:
            // the approved location master with its version history, the placeholder signature
            // specimen of every seeded account, and the four ORF-scoped role assignments that the
            // earlier seed had to skip on a database without an ORF location. Credentials stay
            // excluded because password material must never enter Git, and permit data is not
            // seeded because it is still dummy. Every insert is guarded, so the migration is a
            // no-op on the database it was exported from and never overwrites operator data.
            migrationBuilder.Sql(
                """
                DECLARE @now datetimeoffset = SYSUTCDATETIME();
                DECLARE @actor nvarchar(200) = N'system:migration';
                DECLARE @correlation nvarchar(200) = N'migration:20260925040421';
                DECLARE @sourceLocationId varchar(36) = '01a0a755-d839-7764-b2e5-8d0b7b9bece0';

                ------------------------------------------------------------------------------------------
                -- 1. Location master (ORF, SITE_OFFICE, WATER_BASED) with its version history.
                ------------------------------------------------------------------------------------------
                DECLARE @locations TABLE
                (
                    [Id] uniqueidentifier NOT NULL PRIMARY KEY,
                    [Code] nvarchar(100) NOT NULL,
                    [Name] nvarchar(200) NOT NULL,
                    [EffectiveFrom] datetimeoffset NOT NULL,
                    [MakerId] nvarchar(200) NOT NULL,
                    [CheckerId] nvarchar(200) NOT NULL,
                    [ApprovedAt] datetimeoffset NOT NULL,
                    [CreatedAt] datetimeoffset NOT NULL,
                    [UpdatedAt] datetimeoffset NOT NULL
                );

                INSERT INTO @locations
                    ([Id], [Code], [Name], [EffectiveFrom], [MakerId], [CheckerId], [ApprovedAt], [CreatedAt], [UpdatedAt])
                VALUES
                    ('01A0A755-D839-7764-B2E5-8D0B7B9BECE0', N'ORF', N'Onshore Receiving Facility', '2026-09-15T00:00:00Z', N'admin.location.maker', N'admin.location.checker', '2026-09-15T23:10:16.5843345Z', '2026-09-15T23:10:16.1215663Z', '2026-09-15T23:10:16.5843345Z'),
                    ('01A0A755-DA5E-754B-B877-2B9403BC3C6D', N'SITE_OFFICE', N'Site-Office', '2026-09-15T00:00:00Z', N'admin.location.maker', N'admin.location.checker', '2026-09-15T23:10:16.7092902Z', '2026-09-15T23:10:16.6706676Z', '2026-09-15T23:10:16.7092902Z'),
                    ('01A0A755-DABE-71C5-974A-75563EA5699A', N'WATER_BASED', N'Water-Based Activity', '2026-09-15T00:00:00Z', N'admin.location.maker', N'admin.location.checker', '2026-09-15T23:10:16.8079255Z', '2026-09-15T23:10:16.7663564Z', '2026-09-15T23:10:16.8079255Z');

                DECLARE @locationVersions TABLE
                (
                    [Id] uniqueidentifier NOT NULL PRIMARY KEY,
                    [LocationMasterId] uniqueidentifier NOT NULL,
                    [Version] int NOT NULL,
                    [ContentJson] nvarchar(max) NOT NULL,
                    [CreatedAt] datetimeoffset NOT NULL,
                    [CreatedBy] nvarchar(200) NOT NULL
                );

                INSERT INTO @locationVersions ([Id], [LocationMasterId], [Version], [ContentJson], [CreatedAt], [CreatedBy])
                VALUES
                    ('01A0A755-D88D-70A3-9247-E7C264F9D7FF', '01A0A755-D839-7764-B2E5-8D0B7B9BECE0', 1, N'{"id":"01a0a755-d839-7764-b2e5-8d0b7b9bece0","code":"ORF","name":"Onshore Receiving Facility","parentId":null,"effectiveFrom":"2026-09-15T00:00:00+00:00","effectiveUntil":null,"status":0,"version":1,"makerId":"admin.location.maker","checkerId":null,"approvedAt":null,"createdAt":"2026-09-15T23:10:16.1215663+00:00","updatedAt":"2026-09-15T23:10:16.1215663+00:00"}', '2026-09-15T23:10:16.1215663Z', N'admin.location.maker'),
                    ('01A0A755-D9CF-7716-9F1C-1F5A1C05BCF6', '01A0A755-D839-7764-B2E5-8D0B7B9BECE0', 2, N'{"id":"01a0a755-d839-7764-b2e5-8d0b7b9bece0","code":"ORF","name":"Onshore Receiving Facility","parentId":null,"effectiveFrom":"2026-09-15T00:00:00+00:00","effectiveUntil":null,"status":1,"version":2,"makerId":"admin.location.maker","checkerId":null,"approvedAt":null,"createdAt":"2026-09-15T23:10:16.1215663+00:00","updatedAt":"2026-09-15T23:10:16.5078081+00:00"}', '2026-09-15T23:10:16.5078081Z', N'admin.location.maker'),
                    ('01A0A755-DA0A-7BD8-B92C-973D659C18A9', '01A0A755-D839-7764-B2E5-8D0B7B9BECE0', 3, N'{"id":"01a0a755-d839-7764-b2e5-8d0b7b9bece0","code":"ORF","name":"Onshore Receiving Facility","parentId":null,"effectiveFrom":"2026-09-15T00:00:00+00:00","effectiveUntil":null,"status":2,"version":3,"makerId":"admin.location.maker","checkerId":"admin.location.checker","approvedAt":"2026-09-15T23:10:16.5843345+00:00","createdAt":"2026-09-15T23:10:16.1215663+00:00","updatedAt":"2026-09-15T23:10:16.5843345+00:00"}', '2026-09-15T23:10:16.5843345Z', N'admin.location.checker'),
                    ('01A0A755-DA5F-76BD-AD66-F36B266FD932', '01A0A755-DA5E-754B-B877-2B9403BC3C6D', 1, N'{"id":"01a0a755-da5e-754b-b877-2b9403bc3c6d","code":"SITE_OFFICE","name":"Site-Office","parentId":null,"effectiveFrom":"2026-09-15T00:00:00+00:00","effectiveUntil":null,"status":0,"version":1,"makerId":"admin.location.maker","checkerId":null,"approvedAt":null,"createdAt":"2026-09-15T23:10:16.6706676+00:00","updatedAt":"2026-09-15T23:10:16.6706676+00:00"}', '2026-09-15T23:10:16.6706676Z', N'admin.location.maker'),
                    ('01A0A755-DA73-75EC-AF3E-E13DC72AF24C', '01A0A755-DA5E-754B-B877-2B9403BC3C6D', 2, N'{"id":"01a0a755-da5e-754b-b877-2b9403bc3c6d","code":"SITE_OFFICE","name":"Site-Office","parentId":null,"effectiveFrom":"2026-09-15T00:00:00+00:00","effectiveUntil":null,"status":1,"version":2,"makerId":"admin.location.maker","checkerId":null,"approvedAt":null,"createdAt":"2026-09-15T23:10:16.6706676+00:00","updatedAt":"2026-09-15T23:10:16.6903794+00:00"}', '2026-09-15T23:10:16.6903794Z', N'admin.location.maker'),
                    ('01A0A755-DA86-7DC7-A311-D998F087104C', '01A0A755-DA5E-754B-B877-2B9403BC3C6D', 3, N'{"id":"01a0a755-da5e-754b-b877-2b9403bc3c6d","code":"SITE_OFFICE","name":"Site-Office","parentId":null,"effectiveFrom":"2026-09-15T00:00:00+00:00","effectiveUntil":null,"status":2,"version":3,"makerId":"admin.location.maker","checkerId":"admin.location.checker","approvedAt":"2026-09-15T23:10:16.7092902+00:00","createdAt":"2026-09-15T23:10:16.6706676+00:00","updatedAt":"2026-09-15T23:10:16.7092902+00:00"}', '2026-09-15T23:10:16.7092902Z', N'admin.location.checker'),
                    ('01A0A755-DABE-7E66-A45B-47D6D131583C', '01A0A755-DABE-71C5-974A-75563EA5699A', 1, N'{"id":"01a0a755-dabe-71c5-974a-75563ea5699a","code":"WATER_BASED","name":"Water-Based Activity","parentId":null,"effectiveFrom":"2026-09-15T00:00:00+00:00","effectiveUntil":null,"status":0,"version":1,"makerId":"admin.location.maker","checkerId":null,"approvedAt":null,"createdAt":"2026-09-15T23:10:16.7663564+00:00","updatedAt":"2026-09-15T23:10:16.7663564+00:00"}', '2026-09-15T23:10:16.7663564Z', N'admin.location.maker'),
                    ('01A0A755-DAD0-7582-A9F1-F3F96D45C893', '01A0A755-DABE-71C5-974A-75563EA5699A', 2, N'{"id":"01a0a755-dabe-71c5-974a-75563ea5699a","code":"WATER_BASED","name":"Water-Based Activity","parentId":null,"effectiveFrom":"2026-09-15T00:00:00+00:00","effectiveUntil":null,"status":1,"version":2,"makerId":"admin.location.maker","checkerId":null,"approvedAt":null,"createdAt":"2026-09-15T23:10:16.7663564+00:00","updatedAt":"2026-09-15T23:10:16.7833208+00:00"}', '2026-09-15T23:10:16.7833208Z', N'admin.location.maker'),
                    ('01A0A755-DAE9-72C3-B4B7-BCEBE417D1EB', '01A0A755-DABE-71C5-974A-75563EA5699A', 3, N'{"id":"01a0a755-dabe-71c5-974a-75563ea5699a","code":"WATER_BASED","name":"Water-Based Activity","parentId":null,"effectiveFrom":"2026-09-15T00:00:00+00:00","effectiveUntil":null,"status":2,"version":3,"makerId":"admin.location.maker","checkerId":"admin.location.checker","approvedAt":"2026-09-15T23:10:16.8079255+00:00","createdAt":"2026-09-15T23:10:16.7663564+00:00","updatedAt":"2026-09-15T23:10:16.8079255+00:00"}', '2026-09-15T23:10:16.8079255Z', N'admin.location.checker');

                DECLARE @insertedLocations TABLE ([Id] uniqueidentifier NOT NULL PRIMARY KEY);

                -- A location code that already exists in the target database (whatever its id or status)
                -- is left untouched so operator-maintained master data is never duplicated.
                INSERT INTO [cfg].[LocationMaster]
                    ([Id], [Code], [Name], [ParentId], [EffectiveFrom], [EffectiveUntil], [Status], [Version],
                     [MakerId], [CheckerId], [ApprovedAt], [CreatedAt], [UpdatedAt])
                OUTPUT inserted.[Id] INTO @insertedLocations
                SELECT l.[Id], l.[Code], l.[Name], NULL, l.[EffectiveFrom], NULL, 'Approved', 3,
                       l.[MakerId], l.[CheckerId], l.[ApprovedAt], l.[CreatedAt], l.[UpdatedAt]
                FROM @locations l
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM [cfg].[LocationMaster] x
                    WHERE x.[Id] = l.[Id]
                       OR x.[Code] = l.[Code]);

                -- Snapshot JSON is ASCII, so hashing its varchar form equals the UTF-8 hash the store computes.
                INSERT INTO [cfg].[LocationMasterVersion]
                    ([Id], [LocationMasterId], [Version], [ContentJson], [ContentHash], [CreatedAt], [CreatedBy])
                SELECT v.[Id], v.[LocationMasterId], v.[Version], v.[ContentJson],
                       CONVERT(varchar(64), HASHBYTES('SHA2_256', CONVERT(varchar(max), v.[ContentJson])), 2),
                       v.[CreatedAt], v.[CreatedBy]
                FROM @locationVersions v
                INNER JOIN @insertedLocations i ON i.[Id] = v.[LocationMasterId];

                DECLARE @locationEvents TABLE
                (
                    [EventId] uniqueidentifier NOT NULL PRIMARY KEY,
                    [AggregateId] uniqueidentifier NOT NULL,
                    [PayloadJson] nvarchar(max) NOT NULL
                );

                INSERT INTO @locationEvents ([EventId], [AggregateId], [PayloadJson])
                SELECT NEWID(), l.[Id],
                       CONCAT('{"id":"', LOWER(CONVERT(varchar(36), l.[Id])),
                              '","code":"', STRING_ESCAPE(l.[Code], 'json'),
                              '","name":"', STRING_ESCAPE(l.[Name], 'json'),
                              '","status":"Approved","version":3,"source":"development-export-2026-09-25"}')
                FROM @locations l
                INNER JOIN @insertedLocations i ON i.[Id] = l.[Id];

                INSERT INTO [audit].[ConfigurationAuditEvent]
                    ([Id], [AggregateType], [AggregateId], [EventType], [ActorId], [OccurredAt], [PayloadJson], [CorrelationId])
                SELECT e.[EventId], 'LocationMaster', e.[AggregateId], 'location_seeded', @actor, @now, e.[PayloadJson], @correlation
                FROM @locationEvents e;

                INSERT INTO [intg].[OutboxMessage]
                    ([Id], [AggregateId], [EventType], [PayloadJson], [OccurredAt], [ProcessedAt],
                     [Attempts], [NextAttemptAt], [LastError])
                SELECT e.[EventId], e.[AggregateId], 'location_seeded', e.[PayloadJson], @now, NULL, 0, NULL, NULL
                FROM @locationEvents e;

                ------------------------------------------------------------------------------------------
                -- 2. Placeholder signature specimen for the accounts created by the user directory seed.
                --    The Development specimen is one shared PNG. It is attached only to accounts that the
                --    seed migration created (evidenced by their user_account_seeded audit row) and that have
                --    no specimen yet, never to an account an operator registered.
                ------------------------------------------------------------------------------------------
                DECLARE @png varbinary(max) = 0x89504E470D0A1A0A0000000D494844520000003C0000003C0802000000B59E4E25000000017352474200AECE1CE90000000467414D410000B18F0BFC6105000000097048597300000EC300000EC301C76FA86400000819494441546843C599D94F144B14C6EF3FC59B097F00AFFA408204131249F04554D4885B8C2624DC188D1A161754E22EB820B8E0860BA8A0B881B8032E2022B2298A57EF8FFE9A9A66E8E99E1EBAF17BE8549DDABE3A75EA9C5333FFFC0909BF2DFC678182246A0A1DA19106B0FCF1E3C7D8D8185F55250F1DB3256D988D8F8F0F0F0F0F0E0EF6F6F68E8C8C187D4781D449C309408EF2CF9F3FFBFBFB614C414DDF2C50B0FA868C54485B6C2721757EFFFEFDCB972F588536A0EFD7AF5FFBFAFA540E1DA99386D0C4C404DA851C0AD606F405987557571776628F09152992FEF5EB17FC50674F4F0F65BB610A74E0FBF0E1C38F1F3F4A122E8291161BE802EC01AB107BB51AA85B7373F3DBB76F250917814943912F978C9BA732B09BA7A06DDCB973E7F5EBD792848B00A42127BDA2E0818101CA33E90A92B7B5B57577774B122E829106434343D8315E59CC5CA1A6969616EEA224E1220069748C772376F0A5EA4BFAF6EDDB7F9F34F6C0E54B3264D0B9B5B5156F68D743853F692802D44C94C63692610CF0DF2F5EBC6087763D54F890166300E377EFDE519067F00557F0D9B3674976F680560794998DBB44444B8AF4E8E82824E84D39491EA8F9F9F3E776255568750A181B1E16070A383D7FF38025CB63181434852F58A3A3A3032763D767070E99D9F045AF5EBD427DD048481A7E522A03086C4EC693DB9F8224027D3404ADDCBD7B37A8416B36BE661ECEF6C993274F9F3E8500D4AD5E931D12926618CD38384C531E03205741504FC116594296B978F1A2E481C07031A6C0419D3F7F1E7B40BB9AD6C09DF4E4E256A60663821F1267FC53AB0A9200CA5A0CB778EBD62D393B67075F98CECC70FFFEFDA6A6A6CF9F3F6B5DCDAC56E042DA747AFFFE3DDBA520D8CDD625BB79F3E6CCD3571FE89E3B772E6E9999B0A6FC8D672429C00CD49F023EEAF2E5CB2F5FBE4462BA091A085C48AB3773614F6C5A42271E3F7EBC7EFDFA4F9F3ED9750B4CCA4056EDECEC2429459288B7113E7AF468CD9A35274F9E5495154957AE5DBBC64EA88A862BDC35ADB515845D17BE74E912C9B45D99023D790D5CBD7A95DC9FB2D3A2E280E1DDB87163D5AA55DBB76FD70B0DCF8051B10D6D35D1868578D21A801DB369CA8946A26C127C67AB3A634E8D8D8D549924EE1AA800F09EEC79CB962DC78E1D833D0AC6D538AF81B3B32B62A4D51BB0755261E6B21B66803EA899738C3B41E4A74E9D92D9508ED396CA0CACAFAF2F2B2BABA9A9C12D709854B14325611E87E3C4344D6B19BC6C7B7B3B552DAC262724DCB76F9F7EDF30609F67CE9CE18BBF63DB48E218D074E1C285A3478F1E397204678A8B6868683027A6E5E286B822469A016C942FBEC6F5FE1968DEEAEA6A93A33290C2BD7BF7E0815D72D64CA226A04D72BD608C820F1D3A44B0A04C67B920FAF04D1ED34833186530176555EDB6E9A089EFF5EBD795F409F0E6D0B1750E1DF6E69AEAC4D1024A45C7555555E4D99804168CDC63150F4C23CD974BEDFD94026AE2DD6A16A67AE5CA150E9D2C8C733F7BF6AC0E01778BE9B3010CE6F0E1C3903E7DFA34E7A09F1674B01482C226CDDA4C818DBE79F3868B4815A86926D4847B312911D7A0A4A484904B99EC0AB7402B31081F8C47AFADADE51000723219F9388FF97D1123CD97F5509E14E04D1A9060A048AA1C3D01124D63A038F8BD7BF7C20FC7475C84FADAB56B71C6B8E48A8A8A0F1F3E68ACF749FA621A690E8E7955F59854AD9046979801B78AC04B96421307959797575A5ACA4544F16C20272727373717C3E002682CDFD4ACC22066D300F590A3D895C4D0C2DC3978632150C7BE3165843B76ECD8B061032E82A93887F4F4F48C8C0C622427600D0D0731D25C8E9D3B7726F34B85A5E8DFC4052ED983070FA00E69E46C78F1E2C5050505B8336C66E9D2A5696969CA43C2458C345704FBD365F2862C9E0B505E5ECE56614CE680BCAEAE2E3F3F1F351359286767673BBDB535341CC448E35F590C9BF6353875C0AF610398077A9584DBB662C58AFDFBF7133EB2B2B270E456F7F011232D65488B927880BDE17709439C3EAF37245CC4C2C2421CDCA2458B962D5B861DAB67149846DAC016B9815636468A03634E8660A1504C6459B264C9C2850BE12D138F0E31D2BE30FBC124785F70070816C4642404D13D7BF6A0E3E2E262E5C4D688A8108C345FF48A49109971793838856B72090C7AF7EEDDADADAD0AA8D688A81058D3BC6888205409CEDC451C302169E5CA95C4118C7BF6213A1904200DC831387D856B2C180BC145AC5BB76EF5EAD58A88738360A461895E29F040DAB46913EF00BCF2B66DDB0E1E3CA80E738300A471E4040BFDF7435ABC71E3C603070EE034F074E68965758C1C0148E33448DC28100B897CBC4C8F1F3F4E50249A204CD2C187027FD293B7CF4A26F10C8A239B376F46C7843D4893E271354D376B44E4F0210D0FA99004085F313C3CBC6BD7AEA2A2220238D915CF2AFACC32394E01FEA44508C78C764933323333F16E681D2321283AFBCC1992350F5E340B162CC01F6FDDBA95772431851BC91E4C07759E1B24AB6952E779F3E62D5FBEBCBABA5A3F7710BA7902461DB15DE1AF69C01BE45F0B5C3EA54700659F3871822833676A361AF422AD1E18067479F661C1CE280D5D327DB3873980C5393169DA74EE388D929212829F616C401F3C20CF2D95258C1493947D358D2FABACAC24A3C75750B6A50E8A841B5C8ACA51C32C4AC185B469E69ECD9F3FDFBCF32414541D1A1A321612D72152C493666D19066FECDCDC5C1265C99D30FC30EBB6B6369227CA7F398CB336B91129115AB44589D1D5D545628DB219F53749839191116514BE40D9A4D4AE26141DDC351D6879A20C690976E2BCAC91C2EB22260375C69CC8A5E27C6254F8F3E77FAC7EAB6CD69B89F90000000049454E44AE426082;
                DECLARE @pngSha256 nchar(64) = CONVERT(nchar(64), HASHBYTES('SHA2_256', @png), 2);

                DECLARE @signatures TABLE
                (
                    [Id] uniqueidentifier NOT NULL PRIMARY KEY,
                    [SubjectId] nvarchar(200) NOT NULL,
                    [UploadedBy] nvarchar(200) NOT NULL,
                    [UploadedAt] datetimeoffset NOT NULL
                );

                INSERT INTO @signatures ([Id], [SubjectId], [UploadedBy], [UploadedAt])
                VALUES
                    ('01A0C4A5-AA79-7357-B99F-EAEA76BCBA47', N'ade.ruhimat', N'superadmin.local', '2026-09-21T15:46:26.5537150Z'),
                    ('01A0C4A5-B92E-74F5-83DB-E796C85C6169', N'aldi.setiawan', N'superadmin.local', '2026-09-21T15:46:30.3183716Z'),
                    ('01A0C4A5-C860-7492-9607-7695F1AA999D', N'benny.sulistio', N'superadmin.local', '2026-09-21T15:46:34.2085164Z'),
                    ('01A0C4A5-D583-7ADF-93EF-0F63617BEC17', N'darsono4', N'superadmin.local', '2026-09-21T15:46:37.5716772Z'),
                    ('01A0C4A5-E2F4-7895-832F-409FEE46C1FE', N'erwin.jonathan', N'superadmin.local', '2026-09-21T15:46:41.0123356Z'),
                    ('01A0C4A5-EFCE-7606-BEC9-F10755A15F34', N'mk.ferry.febrian', N'superadmin.local', '2026-09-21T15:46:44.3025294Z'),
                    ('01A0C4A5-FEE5-7B7A-BDF2-D067213D74B6', N'superadmin.local', N'superadmin.local', '2026-09-21T15:46:48.1656285Z'),
                    ('01A0C4A6-0CE7-7D74-B011-340AD558A427', N'yosep.zulkarnain', N'superadmin.local', '2026-09-21T15:46:51.7519257Z'),
                    ('01A0C4B7-E984-7A99-AEDE-6E2F80048481', N'muhamad.ibrahim', N'superadmin.local', '2026-09-21T16:06:22.3403628Z'),
                    ('01A0C4B9-6ED3-729B-A60C-80D16475E2C3', N'mk.revanza.raytama', N'superadmin.local', '2026-09-21T16:08:02.0036517Z');

                DECLARE @insertedSignatures TABLE ([Id] uniqueidentifier NOT NULL PRIMARY KEY);

                INSERT INTO [sec].[UserSignatureVersion]
                    ([Id], [SubjectId], [Version], [MediaType], [Content], [Sha256], [UploadedBy], [UploadedAt],
                     [IsActive], [SupersededAt])
                OUTPUT inserted.[Id] INTO @insertedSignatures
                SELECT s.[Id], s.[SubjectId], 1, 'image/png', @png, @pngSha256, s.[UploadedBy], s.[UploadedAt], 1, NULL
                FROM @signatures s
                WHERE EXISTS (SELECT 1 FROM [sec].[UserAccount] a WHERE a.[SubjectId] = s.[SubjectId])
                  AND EXISTS (
                      SELECT 1
                      FROM [audit].[ConfigurationAuditEvent] e
                      WHERE e.[AggregateType] = 'UserAccount'
                        AND e.[EventType] = 'user_account_seeded'
                        AND e.[AggregateId] = CONVERT(uniqueidentifier, SUBSTRING(HASHBYTES('SHA2_256', CONVERT(varchar(200), UPPER(s.[SubjectId]))), 1, 16)))
                  AND NOT EXISTS (SELECT 1 FROM [sec].[UserSignatureVersion] x WHERE x.[SubjectId] = s.[SubjectId]);

                DECLARE @signatureEvents TABLE
                (
                    [EventId] uniqueidentifier NOT NULL PRIMARY KEY,
                    [AggregateId] uniqueidentifier NOT NULL,
                    [PayloadJson] nvarchar(max) NOT NULL
                );

                -- AggregateId mirrors UserDirectoryStore.DeterministicGuid: the first 16 bytes of
                -- SHA-256 over the upper-cased subject id.
                INSERT INTO @signatureEvents ([EventId], [AggregateId], [PayloadJson])
                SELECT NEWID(),
                       CONVERT(uniqueidentifier, SUBSTRING(HASHBYTES('SHA2_256', CONVERT(varchar(200), UPPER(s.[SubjectId]))), 1, 16)),
                       CONCAT('{"subjectId":"', STRING_ESCAPE(s.[SubjectId], 'json'),
                              '","signatureId":"', LOWER(CONVERT(varchar(36), s.[Id])),
                              '","signatureVersion":1,"sha256":"', RTRIM(@pngSha256),
                              '","source":"development-export-2026-09-25"}')
                FROM @signatures s
                INNER JOIN @insertedSignatures i ON i.[Id] = s.[Id];

                INSERT INTO [audit].[ConfigurationAuditEvent]
                    ([Id], [AggregateType], [AggregateId], [EventType], [ActorId], [OccurredAt], [PayloadJson], [CorrelationId])
                SELECT e.[EventId], 'UserAccount', e.[AggregateId], 'user_signature_seeded', @actor, @now, e.[PayloadJson], @correlation
                FROM @signatureEvents e;

                INSERT INTO [intg].[OutboxMessage]
                    ([Id], [AggregateId], [EventType], [PayloadJson], [OccurredAt], [ProcessedAt],
                     [Attempts], [NextAttemptAt], [LastError])
                SELECT e.[EventId], e.[AggregateId], 'user_signature_seeded', e.[PayloadJson], @now, NULL, 0, NULL, NULL
                FROM @signatureEvents e;

                ------------------------------------------------------------------------------------------
                -- 3. Area-scoped role assignments that the user directory seed skipped because no ORF
                --    location existed yet. Same guards as that migration; ORF is resolved by code.
                ------------------------------------------------------------------------------------------
                DECLARE @orfLocationId uniqueidentifier = (
                    SELECT TOP 1 [Id]
                    FROM [cfg].[LocationMaster]
                    WHERE [Code] = N'ORF'
                    ORDER BY CASE WHEN [Status] = 'Approved' THEN 0 ELSE 1 END, [EffectiveFrom] DESC);

                DECLARE @assignments TABLE
                (
                    [Id] uniqueidentifier NOT NULL PRIMARY KEY,
                    [SubjectId] nvarchar(200) NOT NULL,
                    [RoleCode] nvarchar(100) NOT NULL,
                    [ActionCodesJson] nvarchar(max) NOT NULL,
                    [EffectiveFrom] datetimeoffset NOT NULL,
                    [EffectiveUntil] datetimeoffset NULL,
                    [MakerId] nvarchar(200) NOT NULL,
                    [CheckerId] nvarchar(200) NOT NULL,
                    [ApprovedAt] datetimeoffset NOT NULL,
                    [CreatedAt] datetimeoffset NOT NULL,
                    [UpdatedAt] datetimeoffset NOT NULL
                );

                INSERT INTO @assignments
                    ([Id], [SubjectId], [RoleCode], [ActionCodesJson], [EffectiveFrom], [EffectiveUntil],
                     [MakerId], [CheckerId], [ApprovedAt], [CreatedAt], [UpdatedAt])
                VALUES
                    ('01A0C485-D8DB-7394-A78B-54E4A08BB353', N'yosep.zulkarnain', N'AreaOwnerManager', N'["permit.approve-and-issue","permit.closure.review","permit.renewal.review","permit.suspend","permit.suspension.resolve"]', '2026-09-21T15:10:41.0078422Z', NULL, N'assignment.maker.bootstrap', N'assignment.checker.bootstrap', '2026-09-21T15:11:41.5294942Z', '2026-09-21T15:11:41.2753515Z', '2026-09-21T15:11:41.5294942Z'),
                    ('01A0C485-DA11-7AF7-B8CF-BDF78EC6C54B', N'ade.ruhimat', N'AreaOwnerSeniorOfficer', N'["permit.area-operations.review"]', '2026-09-21T15:10:41.0078422Z', NULL, N'assignment.maker.bootstrap', N'assignment.checker.bootstrap', '2026-09-21T15:11:41.6224414Z', '2026-09-21T15:11:41.5850742Z', '2026-09-21T15:11:41.6224414Z'),
                    ('01A0C496-EC2A-7A98-BF99-68C557044C5E', N'benny.sulistio', N'AreaOwnerSeniorOfficer', N'["permit.area-operations.review"]', '2026-09-21T15:29:20.1615206Z', NULL, N'assignment.maker.bootstrap', N'assignment.checker.bootstrap', '2026-09-21T15:30:21.0853361Z', '2026-09-21T15:30:20.3299700Z', '2026-09-21T15:30:21.0853361Z'),
                    ('01A0C496-EF54-7548-8A97-6A6FE6FF7B43', N'muhamad.ibrahim', N'AreaOwnerSeniorOfficer', N'["permit.area-operations.review"]', '2026-09-21T15:29:21.1252942Z', NULL, N'assignment.maker.bootstrap', N'assignment.checker.bootstrap', '2026-09-21T15:30:21.1975785Z', '2026-09-21T15:30:21.1407834Z', '2026-09-21T15:30:21.1975785Z');

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
                    ('01A0C485-D915-7DE6-B80F-3111289C49C5', '01A0C485-D8DB-7394-A78B-54E4A08BB353', 1, N'{"id":"01a0c485-d8db-7394-a78b-54e4a08bb353","subjectId":"yosep.zulkarnain","roleCode":"AreaOwnerManager","actionCodes":["permit.approve-and-issue","permit.closure.review","permit.renewal.review","permit.suspend","permit.suspension.resolve"],"locationId":"01a0a755-d839-7764-b2e5-8d0b7b9bece0","includeDescendants":false,"requiredCompetencyCodes":[],"kind":0,"sourceAuthorizationId":null,"effectiveFrom":"2026-09-21T15:10:41.0078422+00:00","effectiveUntil":null,"status":0,"version":1,"makerId":"assignment.maker.bootstrap","checkerId":null,"approvedAt":null,"createdAt":"2026-09-21T15:11:41.2753515+00:00","updatedAt":"2026-09-21T15:11:41.2753515+00:00"}', '2026-09-21T15:11:41.2753515Z', N'assignment.maker.bootstrap'),
                    ('01A0C485-D9A1-781A-A3C3-6534DBBC3F1E', '01A0C485-D8DB-7394-A78B-54E4A08BB353', 2, N'{"id":"01a0c485-d8db-7394-a78b-54e4a08bb353","subjectId":"yosep.zulkarnain","roleCode":"AreaOwnerManager","actionCodes":["permit.approve-and-issue","permit.closure.review","permit.renewal.review","permit.suspend","permit.suspension.resolve"],"locationId":"01a0a755-d839-7764-b2e5-8d0b7b9bece0","includeDescendants":false,"requiredCompetencyCodes":[],"kind":0,"sourceAuthorizationId":null,"effectiveFrom":"2026-09-21T15:10:41.0078422+00:00","effectiveUntil":null,"status":1,"version":2,"makerId":"assignment.maker.bootstrap","checkerId":null,"approvedAt":null,"createdAt":"2026-09-21T15:11:41.2753515+00:00","updatedAt":"2026-09-21T15:11:41.4545311+00:00"}', '2026-09-21T15:11:41.4545311Z', N'assignment.maker.bootstrap'),
                    ('01A0C485-D9DF-7407-9AE5-5726AE2284E8', '01A0C485-D8DB-7394-A78B-54E4A08BB353', 3, N'{"id":"01a0c485-d8db-7394-a78b-54e4a08bb353","subjectId":"yosep.zulkarnain","roleCode":"AreaOwnerManager","actionCodes":["permit.approve-and-issue","permit.closure.review","permit.renewal.review","permit.suspend","permit.suspension.resolve"],"locationId":"01a0a755-d839-7764-b2e5-8d0b7b9bece0","includeDescendants":false,"requiredCompetencyCodes":[],"kind":0,"sourceAuthorizationId":null,"effectiveFrom":"2026-09-21T15:10:41.0078422+00:00","effectiveUntil":null,"status":2,"version":3,"makerId":"assignment.maker.bootstrap","checkerId":"assignment.checker.bootstrap","approvedAt":"2026-09-21T15:11:41.5294942+00:00","createdAt":"2026-09-21T15:11:41.2753515+00:00","updatedAt":"2026-09-21T15:11:41.5294942+00:00"}', '2026-09-21T15:11:41.5294942Z', N'assignment.checker.bootstrap'),
                    ('01A0C485-DA11-7BFB-B8B2-1371815D5DC7', '01A0C485-DA11-7AF7-B8CF-BDF78EC6C54B', 1, N'{"id":"01a0c485-da11-7af7-b8cf-bdf78ec6c54b","subjectId":"ade.ruhimat","roleCode":"AreaOwnerSeniorOfficer","actionCodes":["permit.area-operations.review"],"locationId":"01a0a755-d839-7764-b2e5-8d0b7b9bece0","includeDescendants":false,"requiredCompetencyCodes":[],"kind":0,"sourceAuthorizationId":null,"effectiveFrom":"2026-09-21T15:10:41.0078422+00:00","effectiveUntil":null,"status":0,"version":1,"makerId":"assignment.maker.bootstrap","checkerId":null,"approvedAt":null,"createdAt":"2026-09-21T15:11:41.5850742+00:00","updatedAt":"2026-09-21T15:11:41.5850742+00:00"}', '2026-09-21T15:11:41.5850742Z', N'assignment.maker.bootstrap'),
                    ('01A0C485-DA25-7802-BBB8-BE74778ADFEA', '01A0C485-DA11-7AF7-B8CF-BDF78EC6C54B', 2, N'{"id":"01a0c485-da11-7af7-b8cf-bdf78ec6c54b","subjectId":"ade.ruhimat","roleCode":"AreaOwnerSeniorOfficer","actionCodes":["permit.area-operations.review"],"locationId":"01a0a755-d839-7764-b2e5-8d0b7b9bece0","includeDescendants":false,"requiredCompetencyCodes":[],"kind":0,"sourceAuthorizationId":null,"effectiveFrom":"2026-09-21T15:10:41.0078422+00:00","effectiveUntil":null,"status":1,"version":2,"makerId":"assignment.maker.bootstrap","checkerId":null,"approvedAt":null,"createdAt":"2026-09-21T15:11:41.5850742+00:00","updatedAt":"2026-09-21T15:11:41.6038159+00:00"}', '2026-09-21T15:11:41.6038159Z', N'assignment.maker.bootstrap'),
                    ('01A0C485-DA39-744B-A49E-7E18882D03F4', '01A0C485-DA11-7AF7-B8CF-BDF78EC6C54B', 3, N'{"id":"01a0c485-da11-7af7-b8cf-bdf78ec6c54b","subjectId":"ade.ruhimat","roleCode":"AreaOwnerSeniorOfficer","actionCodes":["permit.area-operations.review"],"locationId":"01a0a755-d839-7764-b2e5-8d0b7b9bece0","includeDescendants":false,"requiredCompetencyCodes":[],"kind":0,"sourceAuthorizationId":null,"effectiveFrom":"2026-09-21T15:10:41.0078422+00:00","effectiveUntil":null,"status":2,"version":3,"makerId":"assignment.maker.bootstrap","checkerId":"assignment.checker.bootstrap","approvedAt":"2026-09-21T15:11:41.6224414+00:00","createdAt":"2026-09-21T15:11:41.5850742+00:00","updatedAt":"2026-09-21T15:11:41.6224414+00:00"}', '2026-09-21T15:11:41.6224414Z', N'assignment.checker.bootstrap'),
                    ('01A0C496-ECD6-7565-80F0-46599B5BB0D2', '01A0C496-EC2A-7A98-BF99-68C557044C5E', 1, N'{"id":"01a0c496-ec2a-7a98-bf99-68c557044c5e","subjectId":"benny.sulistio","roleCode":"AreaOwnerSeniorOfficer","actionCodes":["permit.area-operations.review"],"locationId":"01a0a755-d839-7764-b2e5-8d0b7b9bece0","includeDescendants":false,"requiredCompetencyCodes":[],"kind":0,"sourceAuthorizationId":null,"effectiveFrom":"2026-09-21T15:29:20.1615206+00:00","effectiveUntil":null,"status":0,"version":1,"makerId":"assignment.maker.bootstrap","checkerId":null,"approvedAt":null,"createdAt":"2026-09-21T15:30:20.32997+00:00","updatedAt":"2026-09-21T15:30:20.32997+00:00"}', '2026-09-21T15:30:20.3299700Z', N'assignment.maker.bootstrap'),
                    ('01A0C496-EEC5-7804-A38B-19A6DA0F7C55', '01A0C496-EC2A-7A98-BF99-68C557044C5E', 2, N'{"id":"01a0c496-ec2a-7a98-bf99-68c557044c5e","subjectId":"benny.sulistio","roleCode":"AreaOwnerSeniorOfficer","actionCodes":["permit.area-operations.review"],"locationId":"01a0a755-d839-7764-b2e5-8d0b7b9bece0","includeDescendants":false,"requiredCompetencyCodes":[],"kind":0,"sourceAuthorizationId":null,"effectiveFrom":"2026-09-21T15:29:20.1615206+00:00","effectiveUntil":null,"status":1,"version":2,"makerId":"assignment.maker.bootstrap","checkerId":null,"approvedAt":null,"createdAt":"2026-09-21T15:30:20.32997+00:00","updatedAt":"2026-09-21T15:30:20.9704944+00:00"}', '2026-09-21T15:30:20.9704944Z', N'assignment.maker.bootstrap'),
                    ('01A0C496-EF25-75B5-84D1-4C3D7D980377', '01A0C496-EC2A-7A98-BF99-68C557044C5E', 3, N'{"id":"01a0c496-ec2a-7a98-bf99-68c557044c5e","subjectId":"benny.sulistio","roleCode":"AreaOwnerSeniorOfficer","actionCodes":["permit.area-operations.review"],"locationId":"01a0a755-d839-7764-b2e5-8d0b7b9bece0","includeDescendants":false,"requiredCompetencyCodes":[],"kind":0,"sourceAuthorizationId":null,"effectiveFrom":"2026-09-21T15:29:20.1615206+00:00","effectiveUntil":null,"status":2,"version":3,"makerId":"assignment.maker.bootstrap","checkerId":"assignment.checker.bootstrap","approvedAt":"2026-09-21T15:30:21.0853361+00:00","createdAt":"2026-09-21T15:30:20.32997+00:00","updatedAt":"2026-09-21T15:30:21.0853361+00:00"}', '2026-09-21T15:30:21.0853361Z', N'assignment.checker.bootstrap'),
                    ('01A0C496-EF55-7461-A81D-C5C1D3C0D065', '01A0C496-EF54-7548-8A97-6A6FE6FF7B43', 1, N'{"id":"01a0c496-ef54-7548-8a97-6a6fe6ff7b43","subjectId":"muhamad.ibrahim","roleCode":"AreaOwnerSeniorOfficer","actionCodes":["permit.area-operations.review"],"locationId":"01a0a755-d839-7764-b2e5-8d0b7b9bece0","includeDescendants":false,"requiredCompetencyCodes":[],"kind":0,"sourceAuthorizationId":null,"effectiveFrom":"2026-09-21T15:29:21.1252942+00:00","effectiveUntil":null,"status":0,"version":1,"makerId":"assignment.maker.bootstrap","checkerId":null,"approvedAt":null,"createdAt":"2026-09-21T15:30:21.1407834+00:00","updatedAt":"2026-09-21T15:30:21.1407834+00:00"}', '2026-09-21T15:30:21.1407834Z', N'assignment.maker.bootstrap'),
                    ('01A0C496-EF71-78B7-B822-CB1DF7897145', '01A0C496-EF54-7548-8A97-6A6FE6FF7B43', 2, N'{"id":"01a0c496-ef54-7548-8a97-6a6fe6ff7b43","subjectId":"muhamad.ibrahim","roleCode":"AreaOwnerSeniorOfficer","actionCodes":["permit.area-operations.review"],"locationId":"01a0a755-d839-7764-b2e5-8d0b7b9bece0","includeDescendants":false,"requiredCompetencyCodes":[],"kind":0,"sourceAuthorizationId":null,"effectiveFrom":"2026-09-21T15:29:21.1252942+00:00","effectiveUntil":null,"status":1,"version":2,"makerId":"assignment.maker.bootstrap","checkerId":null,"approvedAt":null,"createdAt":"2026-09-21T15:30:21.1407834+00:00","updatedAt":"2026-09-21T15:30:21.1674824+00:00"}', '2026-09-21T15:30:21.1674824Z', N'assignment.maker.bootstrap'),
                    ('01A0C496-EF91-7F85-BC36-56EEAB2DC53B', '01A0C496-EF54-7548-8A97-6A6FE6FF7B43', 3, N'{"id":"01a0c496-ef54-7548-8a97-6a6fe6ff7b43","subjectId":"muhamad.ibrahim","roleCode":"AreaOwnerSeniorOfficer","actionCodes":["permit.area-operations.review"],"locationId":"01a0a755-d839-7764-b2e5-8d0b7b9bece0","includeDescendants":false,"requiredCompetencyCodes":[],"kind":0,"sourceAuthorizationId":null,"effectiveFrom":"2026-09-21T15:29:21.1252942+00:00","effectiveUntil":null,"status":2,"version":3,"makerId":"assignment.maker.bootstrap","checkerId":"assignment.checker.bootstrap","approvedAt":"2026-09-21T15:30:21.1975785+00:00","createdAt":"2026-09-21T15:30:21.1407834+00:00","updatedAt":"2026-09-21T15:30:21.1975785+00:00"}', '2026-09-21T15:30:21.1975785Z', N'assignment.checker.bootstrap');

                DECLARE @outcome TABLE
                (
                    [Id] uniqueidentifier NOT NULL PRIMARY KEY,
                    [SkipReason] varchar(40) NULL
                );

                INSERT INTO @outcome ([Id], [SkipReason])
                SELECT a.[Id],
                       CASE
                           WHEN EXISTS (SELECT 1 FROM [sec].[UserAuthorization] x WHERE x.[Id] = a.[Id])
                               THEN 'already_present'
                           WHEN NOT EXISTS (SELECT 1 FROM [sec].[UserAccount] u WHERE u.[SubjectId] = a.[SubjectId])
                               THEN 'user_account_missing'
                           WHEN @orfLocationId IS NULL
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
                SELECT a.[Id], a.[SubjectId], a.[RoleCode], a.[ActionCodesJson], @orfLocationId, 0,
                       '[]', 'Direct', NULL, a.[EffectiveFrom], a.[EffectiveUntil],
                       'Approved', 3, a.[MakerId], a.[CheckerId], a.[ApprovedAt], a.[CreatedAt], a.[UpdatedAt]
                FROM @assignments a
                INNER JOIN @outcome o ON o.[Id] = a.[Id]
                WHERE o.[SkipReason] IS NULL;

                INSERT INTO [sec].[UserAuthorizationVersion]
                    ([Id], [UserAuthorizationId], [Version], [ContentJson], [ContentHash], [CreatedAt], [CreatedBy])
                SELECT v.[Id], v.[UserAuthorizationId], v.[Version], j.[ContentJson],
                       CONVERT(varchar(64), HASHBYTES('SHA2_256', CONVERT(varchar(max), j.[ContentJson])), 2),
                       v.[CreatedAt], v.[CreatedBy]
                FROM @versions v
                INNER JOIN @outcome o ON o.[Id] = v.[UserAuthorizationId]
                CROSS APPLY (
                    SELECT REPLACE(v.[ContentJson], @sourceLocationId, LOWER(CONVERT(varchar(36), @orfLocationId))) AS [ContentJson]) j
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
                              '","locationId":', CASE WHEN o.[SkipReason] IS NULL
                                                      THEN CONCAT('"', LOWER(CONVERT(varchar(36), @orfLocationId)), '"')
                                                      ELSE 'null' END,
                              ',"locationCode":"ORF","version":3,"makerId":"', STRING_ESCAPE(a.[MakerId], 'json'),
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
            // Seeded master data, specimens, and assignments carry audit history; a rollback must
            // not delete them.
        }
    }
}
