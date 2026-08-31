# Status implementasi terhadap baseline v1.0

Dokumen BRD/PRD/FSD adalah spesifikasi dan sumber kebutuhan, bukan instruksi untuk mengarang kebijakan keselamatan. FSD bagian 21 membolehkan pembangunan fondasi sambil melarang penguncian journey final sebelum OPN-001–009 diputuskan.

## Increment yang diimplementasikan

| Area | Status | Bukti utama |
| --- | --- | --- |
| Struktur modular .NET 10 | Selesai | `PtwOnline.sln`, batas Domain/Application/Infrastructure/API/Worker |
| Domain state machine | Fondasi selesai | `Ptw.Domain/Permit.cs`, unit tests |
| Draft PTW | Vertical slice diperkuat | create/list/get/update/submit, Angular create/list/detail/edit dengan optimistic concurrency |
| Concurrency dan idempotency | Fondasi selesai | ETag/If-Match, request hash, unique idempotency record |
| Data SQL Server | Fondasi selesai | schemas `ptw`, `cfg`, `audit`, `intg`, additive migrations |
| Master lokasi | Framework decision-neutral | effective dating, hierarchy, maker-checker, version, audit/outbox; belum menjadi authority PTW |
| Audit dan outbox | Read/write vertical slice | audit+outbox ditulis atomik; timeline scoped dan paginated tersedia pada API/UI |
| Identity/authorization | Adapter development | actor abstraction, role/ownership/location scope; OIDC menunggu OPN-007 |
| E-SIMI | Kontrak data pada draft saja | live adapter menunggu kontrak OPN-007 |
| Angular SPA | Vertical slices bertahap | dashboard, permit draft/detail/history, master lokasi admin, conflict/access handling, responsive layout |
| Compose | Development topology | web, API, worker, migrator, SQL Server 2025 |
| Integration test | API–SQL baseline | SQL Server Testcontainer, scope denial, stale ETag, idempotency, immutability, hierarchy, dan maker-checker |

## Increment 2

- Detail PTW dapat dibuka dari daftar/dashboard dan menampilkan status, validity, bahaya, kontrol, metadata versi, serta pengingat keselamatan.
- Draft dapat diedit hanya pada status yang diizinkan domain; update selalu membawa `If-Match` dan konflik menawarkan reload versi terbaru.
- Test integrasi API memakai SQL Server disposable dengan credential acak pada runtime, menerapkan migration yang sama dengan aplikasi.
- Register dan draft decision record OPN-001–009 tersedia di [`docs/decisions`](decisions/README.md); seluruh status masih `DRAFT` dan belum menjadi kebijakan.

## Increment 3

- Detail PTW menampilkan audit timeline terbaru dan snapshot seluruh versi tanpa mengubah historical record.
- Endpoint `activity` dan `versions` memvalidasi parent permit dan location scope di server, membatasi `limit` hingga 100, serta mengurutkan record terbaru lebih dahulu.
- Snapshot versi memuat hash SHA-256 untuk verifikasi konten; seluruh timestamp tetap disimpan UTC dan ditampilkan sebagai WIB.
- Test integrasi membuktikan pagination, urutan, persistensi event awal, snapshot versi 1–3, invalid page request, dan penolakan akses di luar location scope.

## Increment 4A

- Master lokasi menyediakan entry effective-dated dan parent hierarchy tanpa seed atau asumsi lokasi produksi.
- Lifecycle eksplisit `DRAFT → PENDING_APPROVAL → APPROVED`; pembuat perubahan tidak dapat menjadi checker untuk entry yang sama.
- Approved entry immutable in-place. Koreksi atau periode berikutnya dibuat sebagai entry baru sehingga interpretasi historis tetap tersedia.
- Setiap perubahan menyimpan snapshot versi, configuration audit, dan outbox secara atomik. Transition memakai `If-Match` dan `Idempotency-Key`.
- Endpoint dan halaman Administrasi hanya tersedia bagi role `Administrator`; akses role lain ditolak server.
- Framework belum digunakan untuk memutuskan scope atau authority PTW sampai OPN-001/002 disahkan.

## Belum diimplementasikan karena membutuhkan keputusan bisnis

- daftar resmi lokasi, owner/area authority, aturan overlap/delegasi, dan aktivasi master sebagai policy PTW (OPN-001);
- risk matrix, review route, final authority, delegation, dan SoD detail (OPN-002);
- checklist per permit class dan mapping final formulir (OPN-003);
- gas thresholds, units, test lifetime, retest, dan continuous monitoring (OPN-004);
- urutan serta SLA HSE–Operations review (OPN-005);
- contractor acknowledgement mechanism (OPN-006);
- production IdP/SSO dan kontrak API/webhook E-SIMI (OPN-007);
- retention, electronic signature, data classification, RPO/RTO final (OPN-008);
- production topology dan HA (OPN-009).

## Urutan implementasi berikutnya

1. Lengkapi dan sahkan draft decision records OPN-001–009 bersama owner pada BRD.
2. Muat daftar lokasi resmi dan implement authorization assignments memakai hasil OPN-001/002.
3. Tambahkan E-SIMI adapter dan contract tests memakai hasil OPN-007.
4. Implement declarative ruleset + simulation tanpa free-form scripting.
5. Lengkapi workflow review/approval dan field operations berdasarkan OPN-002–006.
6. Tambahkan attachment quarantine/malware integration, reports, print, notifications, integration/E2E/security tests.

Tidak ada default gas threshold, approval matrix, atau safety checklist yang ditanam diam-diam di source code.
