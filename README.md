# NR PTW Online

NR PTW Online adalah modular monolith untuk pengelolaan Permit to Work Nusantara Regas. Baseline implementasi saat ini mengacu pada BRD, PRD, dan FSD **v1.6**.

> [!IMPORTANT]
> Status **Diterbitkan** (`ISSUED`) belum otomatis mengizinkan pekerjaan dimulai. Gas test, toolbox/readiness, revalidasi harian/shift, completion, inspeksi/restorasi, handback, dan tanda tangan lapangan tetap dikendalikan pada hardcopy untuk MVP.

## Status saat ini

Increment P0 lifecycle v1.6 telah tersedia:

- lifecycle aktif: `DRAFT`, `UNDER_VALIDATION`, `REVISION_REQUIRED`, `AWAITING_AREA_APPROVAL`, `ISSUED`, `SUSPENDED`, `CLOSURE_REQUESTED`, `CLOSED`, `REJECTED`, `CANCELLED`, `EXPIRED`;
- submit membuat tepat satu task `HSE_VALIDATION`; tidak ada validator Distribusi Gas;
- Sponsor yang juga PIC HSE tidak dapat memvalidasi PTW miliknya sendiri;
- satu task `AREA_APPROVE_AND_ISSUE` dan satu command atomik menyimpan decision, status `ISSUED`, audit, outbox, `PrintPackageSnapshot`, serta placeholder `GeneratedDocument` dalam satu `SaveChanges` transaction;
- pilot submit dibatasi ke ORF dan lokasi lain ditolak fail-closed;
- suspend berlaku langsung, resolve kembali ke `ISSUED`, dan renewal membuat permit baru tanpa overlap;
- closure memakai task pemilik area dan memerlukan signed field copy yang `CLEAN`, bermetadata lengkap, tidak superseded, serta cocok dengan exact PermitVersion dan PrintPackage;
- lampiran privat mengenali signature PDF/JPEG/PNG, menyimpan SHA-256, kategori, metadata dokumen, target version, PrintPackage, replacement lineage, serta evidence malware scan; file selain `CLEAN` tidak dapat diunduh;
- form draft memuat tipe pengaju, work type, equipment/tag, plant/area, CLSR, SIMOPS, safety equipment, isolation/precaution, serta nomor/revisi/tanggal JSA; input hazards/controls bebas telah dihapus dari UI;
- seluruh transition memakai command eksplisit, `If-Match`, `Idempotency-Key`, scope/role server-side, audit, dan outbox.

Ruleset resmi, acting assignment v1.6 lengkap, renderer PDF, malware scanner produksi, E-SIMI adapter, external contractor scoping, serta master LocationRelease/ConfigurationBundle masih fail-closed atau partial. Detail dan traceability ada di [status implementasi](docs/implementation-status.md).

## Stack

| Lapisan | Teknologi |
| --- | --- |
| Frontend | Angular 22, standalone components, Signals/RxJS, reactive forms |
| API | ASP.NET Core 10 / .NET 10 |
| Persistence | EF Core 10, SQL Server 2025 |
| Background | .NET Worker, transactional outbox |
| Runtime | Docker Compose, Nginx unprivileged |
| Test | xUnit, Testcontainers, Vitest/Angular test runner |

## Menjalankan dengan Docker Compose

```powershell
Copy-Item .env.example .env
docker compose --env-file .env -f deploy/compose/compose.dev.yaml up --build -d
```

Buka `http://localhost:8080`. Verifikasi stack dan health endpoint:

```powershell
docker compose --env-file .env -f deploy/compose/compose.dev.yaml ps
Invoke-WebRequest -UseBasicParsing http://localhost:8080/health/ready
```

Gunakan password development yang sama ketika me-rebuild stack dengan volume SQL yang sudah ada; mengganti environment variable tidak mengubah password yang tersimpan di volume. Menghentikan stack tanpa menghapus database:

```powershell
docker compose --env-file .env -f deploy/compose/compose.dev.yaml down
```

Jangan menambahkan `--volumes` kecuali memang bermaksud menghapus seluruh data development.

## Development lokal

Backend memerlukan SDK sesuai [global.json](global.json):

```powershell
dotnet tool restore
dotnet restore PtwOnline.sln
dotnet ef database update --project src/Ptw.Infrastructure/Ptw.Infrastructure.csproj --startup-project src/Ptw.Api/Ptw.Api.csproj
dotnet run --project src/Ptw.Api/Ptw.Api.csproj --urls http://localhost:5080
```

Frontend:

```powershell
Set-Location src/web
npm ci
npm start
```

Development identity hanya aktif pada environment `Development`. Profil yang relevan untuk flow v1.6 adalah Sponsor, PIC HSE (`HSEValidator`), dan Manager pemilik area (`AreaOwnerManager`). Identitas dan Sponsor aktif berasal dari `/api/v1/me`; header development diabaikan di luar Development.

## Endpoint workflow v1.6

| Method | Endpoint | Command |
| --- | --- | --- |
| `POST` | `/api/v1/permits/{id}/submit` | `SubmitPermit` |
| `POST` | `/api/v1/tasks/{taskId}/validate` | `ValidateSubmission` |
| `POST` | `/api/v1/tasks/{taskId}/escalate` | eskalasi HSE dengan catatan |
| `POST` | `/api/v1/tasks/{taskId}/revision` | `RequestRevision` |
| `POST` | `/api/v1/tasks/{taskId}/reject` | `RejectPermit` |
| `POST` | `/api/v1/tasks/{taskId}/approve-and-issue` | `ApproveAndIssuePermit` |
| `POST` | `/api/v1/permits/{id}/suspensions` | `SuspendPermit` |
| `POST` | `/api/v1/permits/{id}/suspensions/resolve` | `ResolveSuspension` |
| `POST` | `/api/v1/permits/{id}/renew` | `CreateRenewal` |
| `POST` | `/api/v1/permits/{id}/closure-requests` | `RequestClosure` |
| `POST` | `/api/v1/closure-tasks/{taskId}/request-evidence` | `RequestClosureEvidenceReplacement` |
| `POST` | `/api/v1/closure-tasks/{taskId}/close` | `ClosePermit` |
| `POST` | `/api/v1/permits/{id}/cancel` | `CancelPermit` |
| `POST` | `/api/v1/permits/{id}/expire` | `ExpirePermit` |

Task command memakai `taskId`, bukan permit ID. Semua transition memerlukan `If-Match` dan `Idempotency-Key`. Tidak ada endpoint generik `setStatus`.

Endpoint baca/create draft, attachment, history, master lokasi, authorization, policy readiness/simulation/UAT, dan health tetap tersedia. OpenAPI hanya diekspos pada Development melalui `/openapi/v1.json`.

## Lifecycle

```text
DRAFT / REVISION_REQUIRED
        | submit
        v
UNDER_VALIDATION --revision--> REVISION_REQUIRED
        | validate                 | reject
        v                          v
AWAITING_AREA_APPROVAL --------> REJECTED
        | approve-and-issue
        v
ISSUED <----resolve---- SUSPENDED
  |  \                   |
  |   \ request closure /
  |    v
  | CLOSURE_REQUESTED --close--> CLOSED
  |
  +--expire--> EXPIRED

DRAFT / UNDER_VALIDATION / REVISION_REQUIRED --cancel--> CANCELLED
```

`CLOSED`, `REJECTED`, `CANCELLED`, dan `EXPIRED` terminal. Renewal membuat aggregate dan nomor baru.

## Quality gate

```powershell
dotnet restore PtwOnline.sln
dotnet build PtwOnline.sln --configuration Release --no-restore
dotnet test PtwOnline.sln --configuration Release --no-build
dotnet format PtwOnline.sln --verify-no-changes --no-restore
dotnet list PtwOnline.sln package --vulnerable --include-transitive

Set-Location src/web
npm ci
npx prettier --check "src/**/*.{ts,html,scss}"
npm run build
npm test -- --watch=false
npm audit --audit-level=high
```

Jika .NET 10 hanya tersedia melalui Docker, mount Docker socket dan set `TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal` agar integration test dapat menjalankan SQL Server disposable.

## Batas produksi

Konfigurasi produksi default fail-closed: master authorization wajib siap, issuance policy tidak approved, dan attachment memerlukan scanner. Jangan mengaktifkan issuance sampai exact ruleset, print template, campaign assets, owner mapping, authority, dan keputusan OPN terkait telah disahkan. Compose development bukan topologi HA produksi.
