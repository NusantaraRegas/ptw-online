# NR PTW Online

NR PTW Online adalah aplikasi internal untuk mendigitalkan lifecycle _Permit to Work_ Nusantara Regas. Sistem ini melengkapi E-SIMI: E-SIMI mengendalikan izin masuk instalasi, sedangkan PTW mengendalikan izin melaksanakan pekerjaan tertentu pada lokasi, periode, dan kondisi lapangan tertentu.

> [!IMPORTANT]
> `APPROVED` bukan izin untuk mulai bekerja. Pekerjaan hanya boleh berjalan ketika PTW berstatus `OPEN`, memiliki satu work period aktif, dan seluruh prasyarat lapangan aktual lulus validasi server.

## Status implementasi

Repository ini berisi increment fondasi dan vertical slice draft PTW—belum merupakan aplikasi produksi lengkap.

Sudah tersedia:

- modular monolith ASP.NET Core 10 dengan batas Domain, Application, Contracts, Infrastructure, API, dan Worker;
- explicit state machine PTW dari `DRAFT` sampai terminal state;
- guard validity maksimum tujuh hari, submit fail-closed, field readiness, single active work period, suspend/resolution, dan handback;
- API `/api/v1` untuk identitas development serta create, list, get, update, dan submit draft;
- optimistic concurrency memakai `ETag`/`If-Match` dan idempotency untuk transition command;
- SQL Server persistence, immutable permit-version snapshot, audit event, transactional outbox, dan initial EF migration;
- Angular 22 SPA dengan dashboard keselamatan, daftar PTW, form draft, responsive navigation, dan Bahasa Indonesia;
- worker outbox, health checks, rate limiting, `ProblemDetails`, correlation ID, development identity adapter, Docker Compose, Nginx, dan CI;
- unit tests untuk invariants dan negative paths domain.

Area yang menunggu keputusan OPN-001–009—termasuk matriks approval, checklist keselamatan, ambang gas, SSO, dan kontrak E-SIMI—tidak diberi default tersembunyi. Lihat [status implementasi](docs/implementation-status.md).

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
  dotnet test PtwOnline.sln --configuration Release
```

### Frontend

Prasyarat: Node.js 22 dan npm yang kompatibel dengan Angular 22.

```powershell
Set-Location src/web
npm ci
npm start
```

Frontend tersedia di `http://localhost:4200` dan meneruskan `/api` serta `/health` ke `http://localhost:5080` melalui [proxy.conf.json](src/web/proxy.conf.json).

## Development identity

Pada environment `Development`, API menyediakan identitas default:

| Nilai          | Default        |
| -------------- | -------------- |
| User ID        | `sponsor.demo` |
| Display name   | `Sponsor Demo` |
| Role           | `Sponsor`      |
| Location scope | `*`            |

Identitas dapat diganti per request melalui `X-Dev-User`, `X-Dev-Name`, `X-Dev-Roles`, dan `X-Dev-Locations`. Adapter hanya berhasil pada environment `Development`; OIDC/BFF produksi menunggu OPN-007.

## API yang tersedia

| Method  | Endpoint                      | Fungsi                                         |
| ------- | ----------------------------- | ---------------------------------------------- |
| `GET`   | `/api/v1/me`                  | Identitas, role, dan scope efektif             |
| `GET`   | `/api/v1/permits`             | Daftar PTW scoped                              |
| `POST`  | `/api/v1/permits`             | Membuat draft                                  |
| `GET`   | `/api/v1/permits/{id}`        | Membaca detail scoped                          |
| `PATCH` | `/api/v1/permits/{id}/draft`  | Update draft dengan `If-Match`                 |
| `POST`  | `/api/v1/permits/{id}/submit` | Submit dengan `If-Match` dan `Idempotency-Key` |
| `GET`   | `/health/live`                | Process liveness                               |
| `GET`   | `/health/ready`               | Readiness termasuk SQL Server                  |

OpenAPI tersedia di development melalui `/openapi/v1.json`. Stale write dikembalikan sebagai HTTP `409`; idempotency key yang sama dengan payload berbeda juga ditolak.

## Lifecycle dan invariants

```text
DRAFT → SUBMITTED → UNDER_REVIEW → AWAITING_APPROVAL → APPROVED
   ↘ CANCELLED       ↘ REVISION_REQUIRED / REJECTED

APPROVED → READY_FOR_ISSUE → OPEN → APPROVED (pekerjaan berlanjut)
                            ↘ SUSPENDED → READY_FOR_ISSUE
                            ↘ WORK_COMPLETED → CLOSED
```

Invariant utama:

- official permit number baru dialokasikan saat submit;
- validity lebih dari tujuh hari ditolak;
- terminal state tidak dapat dibuka kembali;
- maksimum satu active work period per permit;
- suspend menghentikan active work period;
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
