# NR PTW Online

NR PTW Online adalah aplikasi internal untuk mendigitalkan lifecycle _Permit to Work_ Nusantara Regas. Sistem ini melengkapi E-SIMI: E-SIMI mengendalikan izin masuk instalasi, sedangkan PTW mengendalikan izin melaksanakan pekerjaan tertentu pada lokasi, periode, dan kondisi lapangan tertentu.

> [!IMPORTANT]
> `APPROVED` bukan izin untuk mulai bekerja. Pekerjaan hanya boleh berjalan setelah PTW
> **Diterbitkan**, memiliki satu work period aktif, dan seluruh prasyarat lapangan aktual lulus
> validasi server. `OPEN` tetap dipakai sebagai status domain internal untuk kondisi tersebut.

## Status implementasi

Repository ini berisi increment fondasi dan vertical slice draft PTW—belum merupakan aplikasi produksi lengkap.

Sudah tersedia:

- modular monolith ASP.NET Core 10 dengan batas Domain, Application, Contracts, Infrastructure, API, dan Worker;
- explicit state machine PTW dari `DRAFT` sampai terminal state;
- guard validity maksimum tujuh hari, submit fail-closed, field readiness, single active work period,
  penangguhan fail-safe, konfirmasi penyelesaian tiga pihak, dan penutupan;
- API `/api/v1` untuk identitas development, lookup lokasi, create/list/get/update/submit PTW,
  validasi HSSE, request revision/reject, approval pemilik area, penerbitan, penangguhan,
  penyelesaian, audit timeline, dan immutable version history;
- optimistic concurrency memakai `ETag`/`If-Match` dan idempotency untuk transition command;
- SQL Server persistence, immutable permit-version snapshot, audit event, transactional outbox, dan additive EF migrations;
- Angular 22 SPA bertema logo resmi Pertamina Nusantara Regas dengan dashboard keselamatan,
  daftar, detail, create/edit draft ber-ETag, progres validasi HSSE, approval/penerbitan,
  penangguhan dan penyelesaian sesuai role, audit timeline, version history, responsive navigation,
  dan Bahasa Indonesia;
- menu **Tugas Saya** membaca task workflow persisten dari `/api/v1/tasks`; task disaring server-side
  menurut role dan cakupan lokasi akun;
- framework master lokasi effective-dated dengan maker-checker, immutable version snapshot, audit/outbox, dan halaman Administrasi fail-safe;
- lookup lokasi approved/effective dan scope-filtered; katalog MVP yang dikonfirmasi mencakup HO,
  ORF, Site Office, FSRU, dan Water-Based Activity, dan form PTW memakai dropdown lookup;
- flow MVP `submit → validasi HSSE → approval → penerbitan`; PIC pemilik area yang sama menjalankan
  approval dan penerbitan untuk kelompok areanya;
- permintaan penangguhan Sponsor langsung menghentikan active work period sebelum persetujuan PIC
  pemilik area; penyelesaian memerlukan konfirmasi Sponsor, HSSE, dan PIC pemilik area sebelum PIC
  pemilik area dapat menutup PTW;
- framework assignment otorisasi multi-role dengan periode efektif, maker-checker, delegasi
  non-broadening, resolver fail-safe, dan halaman Administrasi;
- activation gate OPN-001/002 yang memeriksa policy version, referensi keputusan, master efektif,
  action mapping, assignment, kompetensi, dan latest passing UAT untuk versi policy yang sama
  sebelum enforcement dapat digunakan;
- paket UAT policy immutable dan berversi dengan expected-vs-actual batch, coverage matrix,
  checksum SHA-256, idempotent run, serta audit/outbox atomik;
- worker outbox, health checks, rate limiting, `ProblemDetails`, correlation ID, development identity adapter, Docker Compose, Nginx, dan CI;
- unit tests untuk invariants/negative paths domain serta integration test API–SQL untuk scope, concurrency, dan idempotency.

Area yang menunggu keputusan OPN-001–009—termasuk matriks approval, checklist keselamatan, ambang gas, SSO, dan kontrak E-SIMI—tidak diberi default tersembunyi. Lihat [status implementasi](docs/implementation-status.md).

Draft decision record beserta owner dan pertanyaan yang wajib dijawab tersedia di [register keputusan OPN](docs/decisions/README.md). Status `DRAFT` belum boleh diperlakukan sebagai kebijakan yang disahkan.

## Stack

| Lapisan     | Teknologi                                                       |
| ----------- | --------------------------------------------------------------- |
| Frontend    | Angular 22, standalone components, Signals/RxJS, reactive forms |
| API         | ASP.NET Core 10 / .NET 10 LTS                                   |
| Persistence | EF Core 10, SQL Server 2025                                     |
| Background  | .NET Worker, transactional outbox                               |
| Runtime     | Docker Compose, Nginx unprivileged                              |
| Test        | xUnit, Vitest/Angular test runner                               |

## Quick start dengan Docker Compose

Prasyarat: Docker Desktop/Engine, Docker Compose V2, dan sekitar 4 GB RAM tersedia untuk containers.

```powershell
Copy-Item .env.example .env
docker compose --env-file .env -f deploy/compose/compose.dev.yaml up --build
```

Buka `http://localhost:8080`. Startup menunggu SQL Server sehat, menjalankan migrator one-shot, lalu memulai API, Worker, dan web.

Database baru tidak melakukan seed lokasi/assignment produksi. Gunakan Admin Maker dan Admin Checker
untuk membuat serta menyetujui lima lokasi MVP sebelum menguji form PTW pada database kosong.

Periksa status:

```powershell
docker compose --env-file .env -f deploy/compose/compose.dev.yaml ps
Invoke-RestMethod http://localhost:8080/health/live
Invoke-RestMethod http://localhost:8080/health/ready
```

Hentikan containers tanpa menghapus database:

```powershell
docker compose --env-file .env -f deploy/compose/compose.dev.yaml down
```

Menambahkan `--volumes` akan menghapus seluruh data development dan tidak dapat dipulihkan tanpa backup. Compose ini khusus development dan tidak boleh dipakai sebagai deployment produksi.

## Development lokal

### Backend

Gunakan SDK .NET 10 yang cocok dengan [global.json](global.json):

```powershell
dotnet tool restore
dotnet restore PtwOnline.sln
dotnet ef database update `
  --project src/Ptw.Infrastructure/Ptw.Infrastructure.csproj `
  --startup-project src/Ptw.Api/Ptw.Api.csproj
dotnet run --project src/Ptw.Api/Ptw.Api.csproj --urls http://localhost:5080
```

Pada terminal lain:

```powershell
dotnet run --project src/Ptw.Worker/Ptw.Worker.csproj
```

Connection string lokal memakai Windows Integrated Security. Gunakan environment variable `ConnectionStrings__PtwDb` untuk menggantinya tanpa mengedit atau meng-commit secret.

Jika SDK .NET 10 belum terpasang:

```powershell
docker run --rm -v "${PWD}:/workspace" -w /workspace `
  mcr.microsoft.com/dotnet/sdk:10.0 `
  dotnet test tests/Ptw.Domain.Tests/Ptw.Domain.Tests.csproj --configuration Release
```

Integration test API memakai SQL Server disposable melalui Testcontainers ketika dijalankan dengan SDK native; Docker harus aktif. Untuk fallback ketika SDK hanya tersedia dalam container, gunakan SQL Server Compose dan database test terpisah:

```powershell
$passwordLine = Get-Content -Encoding utf8 .env |
  Where-Object { $_ -like 'PTW_SQL_BOOTSTRAP_PASSWORD=*' } |
  Select-Object -First 1
$testPassword = $passwordLine.Substring($passwordLine.IndexOf('=') + 1)
$env:PTW_TEST_CONNECTION_STRING = "Server=tcp:db,1433;Database=PtwOnlineIntegration;User Id=sa;Password=$testPassword;Encrypt=True;TrustServerCertificate=True"

docker compose --env-file .env -f deploy/compose/compose.dev.yaml up -d db
docker run --rm --network nr-ptw-dev_backend `
  -e PTW_TEST_CONNECTION_STRING `
  -v "${PWD}:/workspace" -w /workspace `
  mcr.microsoft.com/dotnet/sdk:10.0 `
  dotnet test PtwOnline.sln --configuration Release

Remove-Item Env:PTW_TEST_CONNECTION_STRING
```

Jangan menaruh connection string test atau password di source code maupun commit.

### Frontend

Prasyarat: Node.js 22 dan npm yang kompatibel dengan Angular 22.

```powershell
Set-Location src/web
npm ci
npm start
```

Frontend tersedia di `http://localhost:4200` dan meneruskan `/api` serta `/health` ke `http://localhost:5080` melalui [proxy.conf.json](src/web/proxy.conf.json).

Jika frontend dijalankan melalui Docker Compose di `http://localhost:8080`, refresh browser tidak
membangun ulang image. Setelah source frontend berubah, rebuild hanya service web tanpa menyentuh
database:

```powershell
docker compose --env-file .env -f deploy/compose/compose.dev.yaml up -d --build --no-deps web
```

## Development identity

Pada environment `Development`, API menyediakan identitas default:

| Nilai          | Default        |
| -------------- | -------------- |
| User ID        | `sponsor.demo` |
| Display name   | `Sponsor Demo` |
| Role           | `Sponsor`, `Administrator` |
| Location scope | `*`            |

Identitas dapat diganti per request melalui `X-Dev-User`, `X-Dev-Name`, `X-Dev-Roles`, dan
`X-Dev-Locations`. Nilai header menggantikan default sepenuhnya, sehingga negative test tetap dapat
memakai role `Sponsor` saja. Adapter hanya berhasil pada environment `Development`; OIDC/BFF
produksi menunggu OPN-007.

Frontend Development menyediakan pemilih **Akun demo** pada topbar:

| Akun | User ID | Role |
| --- | --- | --- |
| Sponsor Demo | `sponsor.demo` | `Sponsor`, `Administrator` |
| Admin Maker Demo | `admin.maker.demo` | `Administrator` |
| Admin Checker Demo | `admin.checker.demo` | `Administrator` |
| Sponsor Only Demo | `sponsor.only.demo` | `Sponsor` |
| Validator HSSE Demo | `hsse.validator.demo` | `HSSEValidator` |
| PIC Pemilik Area HO Demo | `area.owner.ho.demo` | `AreaOwnerApprover`, `IssuingAuthority` |
| PIC Pemilik Area ORF & Site Office Demo | `area.owner.orf.demo` | `AreaOwnerApprover`, `IssuingAuthority` |
| PIC Pemilik Area FSRU & Water-Based Demo | `area.owner.fsru.demo` | `AreaOwnerApprover`, `IssuingAuthority` |

Pilihan disimpan di `sessionStorage` dan diterapkan sebagai header hanya untuk request `/api/`.
Gunakan Admin Maker untuk mengajukan konfigurasi, lalu Admin Checker untuk menyetujuinya. Selector
tidak ditampilkan bila `/api/v1/me` tidak mengembalikan development identity, dan header tersebut
tetap diabaikan backend di luar environment `Development`.

Form pembuatan PTW mengambil Sponsor aktif dari `/api/v1/me`; jangan mengirim `sponsor.demo`
sebagai nilai tetap. **Sponsor Only Demo** karena itu membuat draft atas nama
`sponsor.only.demo`, sedangkan **Sponsor Demo** membuat draft atas nama `sponsor.demo` dan juga
memiliki role Administrator.

Untuk UAT flow PTW, gunakan Sponsor untuk submit, Validator HSSE untuk validasi, lalu PIC pemilik
area yang sesuai untuk approval dan penerbitan. Sponsor juga memulai penangguhan atau penyelesaian;
konfirmasi selesai dilakukan Validator HSSE dan PIC pemilik area sebelum PIC pemilik area menutup.
Profile PIC dibatasi menurut kelompok lokasi: HO; ORF/Site Office; serta FSRU/Water-Based Activity.
Penerbitan hanya berhasil di dalam masa berlaku PTW; kegagalan guard ditampilkan di dekat panel
aksi terkait dan server tetap menjadi authority untuk waktu serta transition.

## API yang tersedia

| Method  | Endpoint                                  | Fungsi                                         |
| ------- | ----------------------------------------- | ---------------------------------------------- |
| `GET`   | `/api/v1/me`                    | Identitas, role, dan scope efektif             |
| `GET`   | `/api/v1/locations`             | Lookup lokasi approved/effective dan scoped    |
| `GET`   | `/api/v1/permits`               | Daftar PTW scoped                              |
| `POST`  | `/api/v1/permits`               | Membuat draft                                  |
| `GET`   | `/api/v1/permits/{id}`          | Membaca detail scoped                          |
| `GET`   | `/api/v1/permits/{id}/activity` | Audit timeline scoped dan paginated            |
| `GET`   | `/api/v1/permits/{id}/versions` | Snapshot versi immutable, scoped dan paginated |
| `PATCH` | `/api/v1/permits/{id}/draft`    | Update draft dengan `If-Match`                 |
| `POST`  | `/api/v1/permits/{id}/submit`   | Submit dengan `If-Match` dan `Idempotency-Key` |
| `POST`  | `/api/v1/permits/{id}/validations/hsse/endorse` | Validasi HSSE |
| `POST`  | `/api/v1/permits/{id}/approve`  | Approval oleh PIC pemilik area                 |
| `POST`  | `/api/v1/permits/{id}/issue`    | Menerbitkan PTW setelah field guards lulus     |
| `POST`  | `/api/v1/permits/{id}/suspensions/request` | Sponsor meminta penangguhan; hak kerja langsung berhenti |
| `POST`  | `/api/v1/permits/{id}/suspensions/approve` | PIC pemilik area menyetujui penangguhan |
| `POST`  | `/api/v1/permits/{id}/completion/declare` | Sponsor menyatakan pekerjaan selesai |
| `POST`  | `/api/v1/permits/{id}/completion/confirm/hsse` | HSSE mengonfirmasi penyelesaian |
| `POST`  | `/api/v1/permits/{id}/completion/confirm/area-owner` | PIC pemilik area mengonfirmasi penyelesaian |
| `POST`  | `/api/v1/permits/{id}/close` | PIC pemilik area menutup PTW setelah seluruh konfirmasi |
| `GET`   | `/api/v1/admin/locations`        | Daftar seluruh konfigurasi lokasi (Admin)      |
| `POST`  | `/api/v1/admin/locations`        | Membuat draft lokasi (Admin)                   |
| `PATCH` | `/api/v1/admin/locations/{id}/draft`      | Memperbarui draft dengan `If-Match`            |
| `POST`  | `/api/v1/admin/locations/{id}/submit`     | Mengajukan pemeriksaan maker-checker           |
| `POST`  | `/api/v1/admin/locations/{id}/approve`    | Menyetujui dengan checker berbeda              |
| `POST`  | `/api/v1/admin/locations/{id}/return-for-changes` | Mengembalikan draft dengan alasan       |
| `GET`   | `/api/v1/admin/authorizations` | Daftar assignment otorisasi (Admin) |
| `POST`  | `/api/v1/admin/authorizations` | Membuat draft assignment multi-role (Admin) |
| `PATCH` | `/api/v1/admin/authorizations/{id}/draft` | Memperbarui draft dengan `If-Match` |
| `POST`  | `/api/v1/admin/authorizations/{id}/submit` | Mengajukan pemeriksaan maker-checker |
| `POST`  | `/api/v1/admin/authorizations/{id}/approve` | Menyetujui dengan checker berbeda |
| `POST`  | `/api/v1/admin/authorizations/{id}/return-for-changes` | Mengembalikan draft dengan alasan |
| `GET`   | `/api/v1/admin/policy-readiness` | Preflight aktivasi policy OPN-001/002 (Admin) |
| `POST`  | `/api/v1/admin/policy-simulations` | Simulasi actor/action/location/kompetensi tanpa mutasi (Admin) |
| `GET`   | `/api/v1/admin/policy-uat-suites` | Daftar paket UAT immutable dan berversi (Admin) |
| `POST`  | `/api/v1/admin/policy-uat-suites` | Membekukan versi paket UAT dengan `Idempotency-Key` (Admin) |
| `GET`   | `/api/v1/admin/policy-uat-suites/{id}` | Detail skenario dan checksum paket UAT (Admin) |
| `GET`   | `/api/v1/admin/policy-uat-suites/{id}/runs` | Riwayat report batch immutable (Admin) |
| `POST`  | `/api/v1/admin/policy-uat-suites/{id}/runs` | Menjalankan batch dengan `Idempotency-Key` (Admin) |
| `GET`   | `/health/live`                  | Process liveness                               |
| `GET`   | `/health/ready`                 | Readiness termasuk SQL Server                  |

OpenAPI tersedia di development melalui `/openapi/v1.json`. Stale write dikembalikan sebagai HTTP `409`; idempotency key yang sama dengan payload berbeda juga ditolak.

Master lokasi dan assignment otorisasi belum diaktifkan sebagai sumber authority produksi PTW.
Satu user dapat memiliki beberapa role; PIC pemilik area pada flow MVP memegang approval dan
penerbitan, sedangkan SoD lain tetap dievaluasi per actor/action/konteks. Delegasi tidak boleh
memperluas authority sumber. Hanya identitas dengan role `Administrator` yang
dapat mengakses endpoint administrasi. Daftar lokasi, role/action, hierarchy ownership, kompetensi,
approval route, dan detail SoD produksi tetap menunggu OPN-001/002 berstatus `ACCEPTED`.

### Activation gate master authorization

Enforcement master authorization nonaktif secara default. Halaman **Administrasi → Kesiapan
policy** dan endpoint `/api/v1/admin/policy-readiness` menampilkan prasyarat yang belum terpenuhi.
Aktivasi hanya boleh dilakukan oleh deployment setelah OPN-001/002 disahkan, data efektif telah
disetujui, dan tersedia passing UAT run untuk policy version yang sama persis. Contoh bentuk
konfigurasi—nilai di bawah wajib diganti dengan keputusan resmi:

```json
{
  "OperationalPolicy": {
    "EnforceMasterAuthorization": true,
    "PolicyVersion": "<versi-policy-yang-disahkan>",
    "AcceptedDecisionReferences": {
      "OPN-001": "<referensi-pengesahan-lokasi>",
      "OPN-002": "<referensi-pengesahan-otorisasi>"
    },
    "PermitActionCodes": {
      "CreateDraft": "<action-code-resmi>",
      "UpdateDraft": "<action-code-resmi>",
      "Submit": "<action-code-resmi>",
      "ValidateHsse": "<action-code-resmi>",
      "Approve": "<action-code-resmi>",
      "Issue": "<action-code-resmi>",
      "RequestRevision": "<action-code-resmi>",
      "Reject": "<action-code-resmi>",
      "RequestSuspension": "<action-code-resmi>",
      "ApproveSuspension": "<action-code-resmi>",
      "DeclareCompletion": "<action-code-resmi>",
      "ConfirmCompletionHsse": "<action-code-resmi>",
      "ConfirmCompletionAreaOwner": "<action-code-resmi>",
      "Close": "<action-code-resmi>"
    }
  }
}
```

Ketika aktif, seluruh command PTW yang dipetakan memerlukan tepat satu master lokasi efektif,
assignment actor yang tidak ambigu untuk action tersebut, dan seluruh kompetensi wajib. Keputusan yang lolos
dicatat sebagai `PermitAuthorizationEvaluated` bersama permit dalam transaksi yang sama. Konfigurasi
aktif tetapi belum siap menghasilkan HTTP `503`; assignment, lokasi, atau kompetensi yang tidak
memenuhi syarat menghasilkan HTTP `403`. Tidak ada fallback ke claim development ketika enforcement
aktif.

Halaman **Kesiapan policy** juga menyediakan simulator UAT. Simulator memakai lokasi dan assignment
yang telah disetujui, tetapi tidak memerlukan enforcement aktif dan tidak menulis permit, audit,
outbox, version, atau receipt. Outcome `ALLOW`/`DENY` selalu ditandai `isAuthoritative: false` dan
tidak dapat digunakan sebagai izin untuk mulai bekerja.

Halaman **UAT policy** (`/admin/policy-uat`) membekukan sekumpulan skenario expected outcome sebagai
versi baru. Setiap batch menyimpan actual outcome, coverage subject/action/location/role/kompetensi,
checksum report, actor, dan waktu eksekusi. Create dan run bersifat idempotent; paket serta report
tidak memiliki endpoint update/delete. Activation readiness hanya menerima latest passing report
untuk `PolicyVersion` yang sama persis. Passing report tetap bukan pengesahan decision record `DRAFT`.

## Lifecycle dan invariants

```text
DRAFT ─submit→ UNDER_REVIEW → AWAITING_APPROVAL → APPROVED
   ↘ CANCELLED        ↘ REVISION_REQUIRED / REJECTED

APPROVED → READY_FOR_ISSUE → DITERBITKAN (internal: OPEN)
                                      ↘ Sponsor meminta penangguhan → SUSPENSION_REQUESTED → SUSPENDED
                                      ↘ Sponsor menyatakan selesai → COMPLETION_CONFIRMATION_PENDING
                                                                      ↓ HSSE + pemilik area konfirmasi
                                                               WORK_COMPLETED → CLOSED (pemilik area)
```

Invariant utama:

- official permit number baru dialokasikan saat submit;
- validity lebih dari tujuh hari ditolak;
- terminal state tidak dapat dibuka kembali;
- maksimum satu active work period per permit;
- permintaan penangguhan Sponsor langsung menghentikan active work period sebelum approval;
- deklarasi selesai Sponsor langsung menghentikan active work period;
- HSSE dan PIC pemilik area wajib mengonfirmasi selesai sebelum PIC pemilik area menutup;
- resolve suspension kembali ke `READY_FOR_ISSUE`, bukan langsung `OPEN`;
- perubahan aggregate, audit event, outbox, dan idempotency record disimpan atomik;
- waktu domain disimpan UTC dan UI menampilkan WIB/Asia Jakarta.

## Struktur repository

```text
src/
  Ptw.Api/              HTTP, development auth, health, OpenAPI
  Ptw.Application/      use cases, authorization/scoping, ports
  Ptw.Contracts/        request/response contracts
  Ptw.Domain/           aggregate, invariants, state machine
  Ptw.Infrastructure/   EF Core, SQL Server, audit/outbox, migrations
  Ptw.Worker/           background outbox processor
  web/                  Angular SPA
tests/Ptw.Domain.Tests/ critical domain tests
tests/Ptw.Api.IntegrationTests/ API + disposable SQL Server tests
deploy/                 Compose and Nginx configuration
docs/                   implementation status
```

Aturan kontribusi otomatis dan coding agents ada di [AGENTS.md](AGENTS.md).

## Quality gate

Backend:

```powershell
dotnet restore PtwOnline.sln
dotnet build PtwOnline.sln --configuration Release --no-restore
dotnet test PtwOnline.sln --configuration Release --no-build
dotnet format PtwOnline.sln --verify-no-changes --no-restore
dotnet list PtwOnline.sln package --vulnerable --include-transitive
```

Frontend:

```powershell
Set-Location src/web
npm ci
npx prettier --check "src/**/*.{ts,html,scss}"
npm run build
npm test -- --watch=false
npm audit --audit-level=high
```

Compose:

```powershell
$env:PTW_SQL_BOOTSTRAP_PASSWORD = 'development-password-only'
docker compose -f deploy/compose/compose.dev.yaml config --quiet
```

## Database migrations

```powershell
dotnet tool restore
dotnet ef migrations add NamaMigration `
  --project src/Ptw.Infrastructure/Ptw.Infrastructure.csproj `
  --startup-project src/Ptw.Api/Ptw.Api.csproj `
  --context PtwDbContext `
  --output-dir Persistence/Migrations
```

Gunakan pola expand/contract dan forward-fix. Jangan memakai `EnsureCreated`, destructive migration satu langkah, atau mengedit migration yang sudah diterapkan pada shared environment.

## Keamanan dan batas produksi

- `.env`, token, certificate, PII fixture nyata, dan secret tidak boleh di-commit;
- development headers tidak boleh diterima di luar `Development`;
- production database harus memakai migration/application user least-privilege, bukan `sa`;
- production image harus dipin ke patch/CU/digest yang disetujui;
- attachment quarantine, malware scanning, live E-SIMI, complete authorization matrix, dan production observability masih harus dibangun;
- Compose single-host tidak memberikan high availability.

Sampai item tersebut selesai dan OPN-001–009 disahkan, aplikasi adalah development baseline, bukan sistem PTW operasional.
