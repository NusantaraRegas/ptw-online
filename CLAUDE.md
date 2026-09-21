# CLAUDE.md — panduan kerja dan autoreview

Panduan operasional Claude untuk repository **NR PTW Online**. Berisi orientasi cepat dan
**checklist autoreview** yang wajib dijalankan sebelum menyerahkan perubahan.

> `AGENTS.md` adalah aturan normatif repository. Dokumen ini tidak menggantikannya; ia
> memberi peta arsitektur, perintah, konvensi kode, dan menerjemahkan aturan tersebut
> menjadi checklist review yang dapat dieksekusi. Bila terjadi konflik, `AGENTS.md` menang.

Urutan rujukan ketika ambigu: (1) permintaan pengguna, (2) decision record yang disahkan di
`docs/decisions/`, (3) invariant dan kontrak yang sudah diuji, (4) BRD/PRD/FSD v1.7 di
`docs/`, (5) asumsi teknis yang dinyatakan eksplisit.

## 1. Orientasi

Modular monolith Permit to Work untuk Nusantara Regas, baseline requirement **v1.7**. MVP
bersifat hybrid digital-ke-kertas: sistem mengelola lifecycle dan menerbitkan paket cetak,
sedangkan gas test, readiness, revalidasi, completion, inspeksi/restorasi, handback, dan
tanda tangan lapangan tetap dikendalikan pada hardcopy.

| Lapisan | Teknologi |
| --- | --- |
| Frontend | Angular 22 standalone, Signals + RxJS, reactive forms, Vitest |
| API | ASP.NET Core 10 / .NET 10, controller tipis, ProblemDetails |
| Persistence | EF Core 10, SQL Server 2025, outbox transaksional |
| Background | .NET Worker (`OutboxWorker`, `PrintPackageRenderWorker`) |
| Cetak | PDFsharp, template terkontrol FM-001/002/003-B-002-NR-B220 |
| Test | xUnit, Testcontainers (MsSql), Vitest |

## 2. Peta repository

```
src/Ptw.Domain          aggregate, state machine, invariant, katalog checklist formulir, UserAccount/UserAuthorizationAssignment — tanpa EF/ASP.NET/IO
src/Ptw.Contracts       DTO netral — tanpa domain behavior
src/Ptw.Application     use case, authorization, ports (IPermitStore, IPrintPackage...), UserAuthorizationRoleProfiles (profil role assignment langsung)
src/Ptw.Infrastructure  EF Core, storage, audit, outbox, Printing/ (renderer + template)
src/Ptw.Api             mapping HTTP, Security/ (DevelopmentAuthenticationHandler, HttpActorContext), login cookie lokal, ApiExceptionHandler
src/Ptw.Worker          job idempotent dan bounded
src/web                 Angular: core/*-api.ts (HTTP) + features/* (komponen)
deploy/compose, deploy/nginx  compose.dev.yaml (production-like), compose.hotreload.yaml (bind mount + dotnet watch/ng serve), reverse proxy (cache index.html, 404 chunk hilang)
tests/Ptw.Domain.Tests           unit state machine
tests/Ptw.Api.IntegrationTests   end-to-end via PtwApiFactory + Testcontainers
tests/Ptw.Printing.Tests         regresi layout dokumen (akses internal via InternalsVisibleTo)
docs/                            BRD/PRD/FSD v1.7 (sumber requirement)
docs/decisions/                  OPN-001..009 (DRAFT), PTW-RENEWAL (baseline Development) — register kebijakan; DRAFT bukan keputusan
docs/implementation-status.md    matriks traceability requirement -> komponen -> test
.github/workflows/ci.yml         CI: build/test backend, build/test frontend, compose config; format/prettier/audit belum di CI
```

Arah dependency: `Api`/`Worker` → `Application` → `Domain`/`Contracts`; `Infrastructure`
mengimplementasikan port `Application`. Tidak ada arah balik.

## 3. Perintah

Backend (SDK sesuai `global.json`, .NET 10):

```powershell
dotnet tool restore
dotnet restore PtwOnline.sln
dotnet build PtwOnline.sln --configuration Release --no-restore
dotnet test PtwOnline.sln --configuration Release --no-build
dotnet format PtwOnline.sln --verify-no-changes --no-restore
dotnet list PtwOnline.sln package --vulnerable --include-transitive
```

Bila SDK .NET 10 tidak terpasang lokal (workstation ini hanya memiliki SDK sampai 9.x), jalankan perintah yang sama melalui image `mcr.microsoft.com/dotnet/sdk:10.0` dengan repository di-mount sebagai `/workspace` (lihat `AGENTS.md`). Set `PTW_USE_ARTIFACTS_OUTPUT=true` dan shadow `artifacts/` dengan named volume agar output build Linux tidak bertabrakan dengan `bin/obj` host; mount Docker socket dan set `TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal` agar integration test dapat menjalankan SQL Server disposable. Jangan menjalankan gate ini di dalam container `api`/`worker` stack hot reload.

Frontend:

```powershell
Set-Location src/web
npm ci
npx prettier --check "src/**/*.{ts,html,scss}"
npm run build
npm test -- --watch=false
npm audit --audit-level=high
```

Stack lengkap dan smoke test:

```powershell
Copy-Item .env.example .env
docker compose --env-file .env -f deploy/compose/compose.dev.yaml up --build -d
Invoke-WebRequest -UseBasicParsing http://localhost:8080/health/ready
```

Migration baru: ubah model/mapping lalu
`dotnet ef migrations add <Nama> --project src/Ptw.Infrastructure --startup-project src/Ptw.Api`.
Jangan mengedit file migration hasil generate secara manual.

Jangan menambahkan `--volumes` pada `docker compose down`, jangan mengganti password
development saat volume SQL lama masih dipakai, dan jangan menghapus volume untuk mengatasi
mismatch kredensial tanpa permintaan eksplisit.

## 4. Konvensi kode

Backend:

- `TreatWarningsAsErrors=true` dan `AnalysisLevel=latest-recommended` — warning baru memblokir build.
- Nullable dan implicit usings aktif; indent 4 spasi untuk `.cs`, 2 spasi untuk `ts/html/scss/json/yaml`; akhiran baris LF.
- `sealed` sebagai default untuk class dan record; primary constructor untuk service dan controller.
- Logging terstruktur melalui `LoggerMessage.Define` dengan `EventId` bernama, bukan interpolasi string.
- Error domain memakai `DomainRuleViolationException` dengan `code` bertitik (`permit.submit.requirements_incomplete`) yang dipetakan `ApiExceptionHandler` ke ProblemDetails.
- Pesan yang terlihat pengguna ditulis Bahasa Indonesia; komentar kode ditulis Bahasa Inggris dan menjelaskan *mengapa*, bukan *apa*.
- Timestamp domain disimpan UTC (`DateTimeOffset`), gas reading memakai `decimal`.

Frontend:

- HTTP terisolasi di `src/app/core/*-api.ts`; komponen di `features/` memakai `input()`/`output()`, `signal`, `computed`, dan `takeUntilDestroyed(destroyRef)`.
- Signals untuk state UI lokal, RxJS untuk stream HTTP. Jangan menyimpan access token di `localStorage`.
- Istilah UI untuk status `ISSUED` adalah **Diterbitkan**; jangan menampilkan `OPEN`.
- Setiap `*-api.ts` memiliki spec `HttpTestingController` yang memverifikasi URL, method, header, dan `responseType`.

Printing:

- `PrintTemplateDescriptor` adalah transkripsi formulir terkontrol. Jangan merapikan teks, urutan item, atau kolomnya tanpa decision record yang disahkan.
- `PermitHeaderClassificationCatalog`, `PermitWorkTypeCatalog`, `PermitSupportingDocumentCatalog`, `PermitSafetyEquipmentCatalog`, dan `PermitOperationalConditionCatalog` di `Ptw.Domain` adalah transkripsi kotak klasifikasi header (HOT: `Api Terbuka`/`Percikan Api` multi-select; COLD: tepat satu `Low Risk`/`High Risk`; CSE tanpa kotak), Bagian 1, 4, 5, dan 7 dengan koordinat template. Bagian 7 memakai enam kotak induk (`Isolasi` dan `Bilas` mewajibkan minimal satu rincian; `Lainnya` mewajibkan detail maksimum 200 karakter). Perlakukan sama seperti descriptor: tanpa decision record, jangan mengubah label, urutan, mode pemilihan, kewajiban item, atau kelas izin yang memuatnya.
- `PermitMandatoryDocumentCatalog` (JSA, ID, BPJS TK, FTW, E-SIMI) **bukan** transkripsi formulir; ia adalah evidence pengajuan yang wajib berlampiran sebelum submit. Hanya JSA yang juga tercetak di Bagian 4 — jangan menambahkan empat dokumen lainnya ke checklist PDF.
- `PtwFormRenderer.RendererVersion` harus dinaikkan bila output dokumen berubah.
- Font dan logo di-embed sebagai `EmbeddedResource` agar render deterministik pada image runtime tanpa font sistem.

## 5. Autoreview

Jalankan checklist ini pada setiap perubahan sebelum menyerahkan hasil. Tandai setiap gate
sebagai lulus, tidak berlaku, atau gagal — dan laporkan kegagalan apa adanya, jangan
disamarkan sebagai selesai.

### Gate A — Invariant keselamatan (blocking)

- [ ] Perubahan tidak membuat `ISSUED`/**Diterbitkan** tampak sebagai izin otomatis memulai pekerjaan; peringatan hardcopy tetap ada.
- [ ] Tidak ada status di luar sebelas status aktif (`DRAFT`, `UNDER_VALIDATION`, `REVISION_REQUIRED`, `AWAITING_AREA_APPROVAL`, `ISSUED`, `SUSPENDED`, `CLOSURE_REQUESTED`, `CLOSED`, `REJECTED`, `CANCELLED`, `EXPIRED`).
- [ ] `CLOSED`, `REJECTED`, `CANCELLED`, `EXPIRED` tetap terminal; validity maksimum tujuh hari; renewal membuat permit dan nomor baru.
- [ ] Renewal tidak mengubah status PTW asal dan tidak membuat permit saat request: Sponsor mengajukan signed field copy `CLEAN` yang cocok dengan exact PrintPackage/PermitVersion, task `AREA_RENEWAL_REVIEW` dibuat, dan draft penerus hanya lahir atomik saat Manager pemilik area menyetujui. Draft penerus tetap melewati submit, validasi HSE, review Bagian 7 oleh SO/Officer pemilik wilayah, dan approve-and-issue normal.
- [ ] Suspend tetap menghentikan hak kerja seketika; resolve hanya kembali ke `ISSUED`.
- [ ] Tidak ada endpoint atau helper generik bergaya `setStatus`.
- [ ] Tidak ada kebijakan OPN-001–012 yang dikarang: location authority, risk/approval matrix, checklist final, ambang/umur gas test, urutan review, contractor acknowledgement, kontrak SSO/E-SIMI produksi, retention, RPO/RTO, topologi HA. Tanpa decision record, jalur tersebut fail-closed. Klasifikasi header HOT/COLD hanya merepresentasikan checklist formulir (dan memetakan `RiskLevel` legacy pada COLD), bukan matriks routing risiko OPN-002.

Jika salah satu gate ini berpotensi melemah, hentikan pekerjaan dan minta keputusan eksplisit.

### Gate B — Batas arsitektur

- [ ] `Ptw.Domain` tidak mengimpor EF Core, ASP.NET Core, filesystem, HTTP, atau project lain.
- [ ] `Ptw.Contracts` hanya berisi DTO, tanpa behavior domain.
- [ ] `Ptw.Application` tidak bergantung pada `Ptw.Infrastructure` atau detail HTTP; ketergantungan baru masuk sebagai port.
- [ ] Controller tetap tipis: parsing, pemanggilan service, header respons. Tidak ada business rule di controller atau di UI.
- [ ] Tidak ada query lintas modul yang membaca tabel modul lain secara langsung.

### Gate C — Command, data, dan konsistensi

- [ ] Transition command membawa `Idempotency-Key`; key sama dengan payload sama mengembalikan hasil pertama, payload berbeda menghasilkan `409`.
- [ ] Update aggregate membawa `If-Match`; versi basi menghasilkan `409` dan respons mengembalikan `ETag` baru.
- [ ] Aggregate, task/decision, audit event, outbox message, dan idempotency result commit atomik dalam satu transaction.
- [ ] Audit tetap append-only; tidak ada jalur aplikasi untuk mengubah atau menghapus audit historis.
- [ ] Snapshot yang menjadi dasar keputusan (`PrintPackageSnapshot`, evidence) tidak berubah setelah dibuat.
- [ ] Submit ulang setelah `REVISION_REQUIRED` menaikkan `PermitVersion` meskipun draft tidak berubah; task lama tetap `CANCELLED` sebagai riwayat dan task baru terikat pada versi baru.
- [ ] Migration bersifat additive/expand-contract; tanpa `EnsureCreated` dan tanpa destructive migration satu langkah; `PtwDbContextModelSnapshot` ikut ter-update.
- [ ] Tipe kolom sesuai: `datetimeoffset`, `decimal` untuk gas reading, foreign key dan check constraint, index yang ter-scope.
- [ ] Tidak ada panggilan HTTP eksternal di dalam database transaction; job Worker idempotent dan bounded.

### Gate D — Authorization dan keamanan

- [ ] Scope filter diterapkan pada query, termasuk pengecekan parent permit untuk resource turunan dan attachment. Menyembunyikan tombol di UI tidak dihitung.
- [ ] Identitas actor dan Sponsor aktif berasal dari `/api/v1/me`, bukan profil demo yang di-hard-code ke payload domain.
- [ ] Bagian 5 hanya dapat ditetapkan PIC HSE saat validasi; payload draft Sponsor yang membawa Bagian 5 ditolak, dan approval tanpa evidence Bagian 5 diarahkan ke revisi.
- [ ] Bagian 7 hanya dapat ditetapkan SO/Officer pemilik wilayah (pool role `AreaOwnerSeniorOfficer`; kode dipertahankan untuk kompatibilitas data) pada tepat satu task `AREA_OPERATION_REVIEW` setelah validasi HSE, dengan assignment yang terverifikasi server. Reviewer pertama yang menyelesaikan task menang; reviewer berikutnya tidak lagi menemukan task (`404`). Sponsor dan validator HSE tidak boleh menjadi reviewer; Manager yang menerbitkan harus berbeda dari Sponsor, validator HSE, dan reviewer; approve-and-issue tanpa evidence Bagian 7 ditolak. Nama dan jabatan aktor pada evidence berasal dari profil akun aktif di server, bukan dari klien. Field bebas CLSR dan isolation/precaution tidak lagi diterima dari Sponsor.
- [ ] Bagian 4: JSA wajib, setiap dokumen terpilih memerlukan lampiran bertaut, dan metadata lampiran JSA harus cocok dengan draft sebelum submit. Validasi ini di server, bukan di UI.
- [ ] Dokumen dasar JSA, ID, BPJS TK, FTW, dan E-SIMI (`PermitMandatoryDocumentCatalog`) masing-masing memiliki lampiran bertaut sebelum submit; JSA harus diunggah dengan kategori `JSA`.
- [ ] Development identity header hanya aktif pada environment `Development`.
- [ ] Akun lokal, login cookie HTTP-only, dan `UserAuthorizationApproval:AllowAdministratorSelfApproval=true` hanya untuk `Development`; default kode dan konfigurasi non-Development tetap mewajibkan maker dan checker berbeda. Role dan scope dihitung ulang dari assignment approved/effective pada setiap request, bukan dari cookie.
- [ ] Assignment langsung (`POST /api/v1/admin/authorizations/direct`) hanya menerima user, role, area bila role area-scoped, dan periode; action code dan kompetensi diturunkan server dari `UserAuthorizationRoleProfiles`, input action code dari klien diabaikan, dan tanpa profil terkonfigurasi jalur ini fail-closed. Profil di `appsettings.Development.json` adalah konfigurasi UX/UAT, bukan matriks OPN-002; jangan menyalinnya ke production.
- [ ] `DevelopmentUploadTrustScanner` hanya terdaftar saat environment `Development` **dan** `Attachments:RequireMalwareScan=false`; di luar itu `UnavailableMalwareScanner` tetap fail-closed. Jangan melonggarkan kondisi ini atau membawanya ke konfigurasi production.
- [ ] Tidak ada secret, `.env`, token, connection string ber-secret, PII nyata, isi attachment, atau build output yang masuk Git.
- [ ] Log tidak memuat token, secret, isi dokumen, atau PII berlebih; correlation ID (`X-Correlation-ID`) tetap dipertahankan.
- [ ] File attachment berstatus selain `CLEAN` tidak dapat diunduh; hanya paket cetak `READY` yang merupakan dokumen resmi.
- [ ] OpenAPI, CORS, TLS, CSP, upload limit, dan rate limit tetap fail-safe; OpenAPI tidak terekspos di luar Development.
- [ ] Tidak ada dependency vulnerability high/critical yang dibiarkan, dan tidak ada audit yang dinonaktifkan agar build hijau.

### Gate E — Paket cetak

- [ ] Output tetap setia pada formulir terkontrol FM-001/002/003-B-002-NR-B220 sebagai dua halaman A3: halaman 1 landscape untuk Bagian 1–7 dan halaman 2 portrait yang dimulai pada Bagian 8. Jangan mengganti dengan layout digital yang lebih rapi, template placeholder, atau menunda layout.
- [ ] Sistem mengisi Bagian 1–5 dan evidence Bagian 7 (checklist kondisi operasi reviewer SO/Officer serta baris keputusan SO/Officer dan Manager, dengan nama/jabatan aktor dari profil akun dan spesimen tanda tangan berversi bila ada) dari snapshot immutable; Bagian 6 dan Bagian 8–10 dicetak kosong dengan ruang tulis yang memadai.
- [ ] Perubahan output menaikkan `RendererVersion` dan disertai test regresi di `tests/Ptw.Printing.Tests`.
- [ ] Kegagalan render tidak membatalkan keputusan penerbitan: `RETRYING` dengan backoff, lalu `FAILED`, dengan retry administrator yang idempotent.
- [ ] Pratinjau draft selalu ber-watermark `DRAFT / TIDAK BERLAKU` dan tidak pernah disimpan; setiap unduhan dokumen resmi menghasilkan audit event.

### Gate F — Frontend dan aksesibilitas

- [ ] Journey pengguna berbahasa Indonesia dan konsisten dengan istilah SOP; **Diterbitkan** dipakai untuk `ISSUED`.
- [ ] Status disampaikan lewat teks, bukan hanya warna.
- [ ] Error command terlihat di dekat aksi pemicunya pada halaman panjang; summary global tetap tersedia untuk aksesibilitas.
- [ ] Form panjang memakai reactive forms dengan asosiasi error, summary, dan remediation yang jelas.
- [ ] Memenuhi minimal WCAG 2.2 AA: keyboard, focus terlihat, heading semantik, label, kontras, error terasosiasi.
- [ ] Konfigurasi Nginx menjaga `index.html` selalu direvalidasi, aset JS/CSS ber-hash immutable, dan chunk hilang tetap `404`; pemulihan chunk basi di klien tetap dibatasi satu reload per menit.

### Gate G — Test dan quality gate

- [ ] Setiap transition baru memiliki test positif **dan** negatif; perubahan safety-critical menargetkan branch coverage tinggi.
- [ ] Perubahan identity selector, authorization, atau scoping disertai negative/regression test.
- [ ] `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes`, dan `dotnet list package --vulnerable` lulus tanpa warning baru.
- [ ] `prettier --check`, `npm run build`, `npm test -- --watch=false`, dan `npm audit --audit-level=high` lulus.
- [ ] Smoke test end-to-end dijalankan untuk perubahan persistence, migration, proxy, startup ordering, authentication, atau kontrak API.

### Gate H — Dokumentasi dan kebersihan perubahan

- [ ] `README.md` dan `docs/implementation-status.md` diperbarui bila cara menjalankan, scope, atau status berubah — termasuk baris traceability requirement → komponen → endpoint → migration → test.
- [ ] Keputusan yang masih terbuka dinyatakan terbuka, bukan disamarkan sebagai implementasi selesai.
- [ ] Perubahan pengguna yang tidak terkait tetap dipertahankan; tanpa reset/checkout destruktif dan tanpa mengedit generated output tanpa alasan.
- [ ] Patch kecil dan dapat direview; commit message menjelaskan outcome.

## 6. Kapan harus berhenti dan bertanya

Hentikan pekerjaan dan minta keputusan eksplisit bila perubahan menyentuh salah satu dari:
invariant Gate A; kebijakan OPN yang belum disahkan; layout formulir terkontrol; retensi,
RPO/RTO, atau topologi HA; kontrak SSO/E-SIMI produksi; penghapusan volume atau database;
atau pelonggaran default produksi yang saat ini fail-closed.
