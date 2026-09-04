# Status implementasi terhadap baseline v1.0

Dokumen BRD/PRD/FSD adalah spesifikasi dan sumber kebutuhan, bukan instruksi untuk mengarang kebijakan keselamatan. FSD bagian 21 membolehkan pembangunan fondasi sambil melarang penguncian journey final sebelum OPN-001–009 diputuskan.

## Increment yang diimplementasikan

| Area | Status | Bukti utama |
| --- | --- | --- |
| Struktur modular .NET 10 | Selesai | `PtwOnline.sln`, batas Domain/Application/Infrastructure/API/Worker |
| Domain state machine | Fondasi selesai | `Ptw.Domain/Permit.cs`, unit tests |
| Draft PTW | Vertical slice diperkuat | create/list/get/update/submit, Angular create/list/detail/edit dengan optimistic concurrency |
| Workflow utama PTW | Fondasi MVP | submit, validasi HSSE, approval/penerbitan pemilik area, penangguhan fail-safe, dan penyelesaian tiga pihak |
| Concurrency dan idempotency | Fondasi selesai | ETag/If-Match, request hash, unique idempotency record |
| Data SQL Server | Fondasi selesai | schemas `ptw`, `cfg`, `audit`, `intg`, additive migrations |
| Master lokasi | Framework + activation gate | effective dating, hierarchy, maker-checker, version, audit/outbox; enforcement default nonaktif |
| Assignment otorisasi | Framework + activation gate | multi-role, effective dating, maker-checker, delegasi non-broadening, resolver dan competency check fail-safe |
| Audit dan outbox | Read/write vertical slice | audit+outbox ditulis atomik; timeline scoped dan paginated tersedia pada API/UI |
| Identity/authorization | Adapter development | actor abstraction, role/ownership/location/competency claims; OIDC menunggu OPN-007 |
| E-SIMI | Kontrak data pada draft saja | live adapter menunggu kontrak OPN-007 |
| Angular SPA | Vertical slices bertahap | tema logo resmi, dashboard, permit draft/detail/history, master lokasi, assignment otorisasi, readiness, simulator, dan paket UAT policy admin |
| Compose | Development topology | web, API, worker, migrator, SQL Server 2025 |
| Integration test | API–SQL baseline | SQL Server disposable, scope denial, stale ETag, idempotency, maker-checker, activation gate, simulation non-mutating, UAT evidence, competency denial, dan atomic authorization evidence |

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
- Endpoint lookup `/api/v1/locations` hanya mengembalikan kode/nama lokasi `APPROVED` yang efektif
  dan berada dalam scope actor. Form buat/edit PTW memakai lookup ini sebagai dropdown dan tidak
  membaca endpoint Administrasi atau menerima input lokasi bebas dari journey normal.
- Framework belum digunakan untuk memutuskan scope atau authority PTW sampai OPN-001/002 disahkan.

## Increment 4B

- Satu `SubjectId` dapat memiliki beberapa assignment role terpisah; tidak ada constraint satu user
  satu role.
- Setiap assignment memiliki action, location scope opsional, kompetensi wajib, dan periode efektif.
- Lifecycle eksplisit `DRAFT → PENDING_APPROVAL → APPROVED` memakai maker-checker, optimistic
  concurrency, idempotency receipt, immutable version snapshot, configuration audit, dan outbox
  dalam satu transaction.
- Delegasi wajib finite dan hanya dapat mempersempit assignment langsung yang telah disetujui;
  delegation chaining serta perluasan role/action/kompetensi/lokasi/periode ditolak.
- Resolver menolak assignment hilang, kedaluwarsa, dan authority ambigu untuk role/action/konteks
  yang sama. Beberapa role berbeda pada orang yang sama tetap valid.
- Endpoint dan halaman `/admin/authorizations` hanya tersedia bagi Administrator.
- Topbar Development menyediakan identity switcher untuk menguji maker-checker dengan actor berbeda;
  pilihan hanya disimpan pada session browser dan development headers diabaikan di environment lain.
- Resolver belum menjadi authority aktif: wiring create/update/submit hanya berjalan ketika activation
  gate dinyalakan setelah daftar role/action, kompetensi, dan OPN-001/002 dikonfigurasi.

## Increment 5A

- `OperationalPolicy` menyediakan activation gate opt-in; enforcement tetap `false` secara default
  dan tidak menganggap decision record `DRAFT` sebagai kebijakan produksi.
- Preflight memerlukan versi policy, referensi pengesahan OPN-001/002, mapping action eksplisit untuk
  create/update/submit, serta minimal satu lokasi dan assignment disetujui yang sedang efektif.
- Endpoint dan halaman `/admin/policy` hanya dapat dibaca Administrator serta menjelaskan setiap
  blocker tanpa mengubah atau mengesahkan decision record.
- Saat enforcement diaktifkan, command perubahan PTW memetakan kode lokasi ke tepat satu master
  efektif, menyelesaikan assignment actor/action/location, dan memverifikasi competency claims.
- Konfigurasi aktif yang belum siap ditolak HTTP `503`; lokasi, assignment, ambiguity, atau
  kompetensi yang tidak valid ditolak HTTP `403` tanpa fallback ke development claims.
- Bukti policy version, decision references, action, location master, assignment, dan kompetensi
  dicatat sebagai audit `PermitAuthorizationEvaluated` dalam transaksi permit yang sama.
- Integration test membuktikan jalur sukses, akses admin, kompetensi hilang, audit atomik, dan
  fail-closed ketika keputusan belum dikonfigurasi.

## Increment 5B

- Endpoint `POST /api/v1/admin/policy-simulations` dan form UAT pada `/admin/policy` mengevaluasi
  subject, action, kode lokasi, waktu efektif, dan competency codes tanpa mengaktifkan enforcement.
- Outcome `ALLOW` atau `DENY` menjelaskan master lokasi, assignment/role yang cocok, kompetensi
  wajib dan hilang, serta hasil setiap check dengan penanda `isAuthoritative: false`.
- Lokasi hilang/overlap, assignment hilang/ambigu, dan kompetensi tidak lengkap menghasilkan
  outcome `DENY` fail-safe; request tetap sukses HTTP `200` agar skenario negatif dapat dicatat UAT.
- Simulator hanya dapat dijalankan Administrator dan tidak menulis permit, audit, outbox, snapshot,
  idempotency receipt, maupun configuration history.
- Integration test membandingkan seluruh jumlah record sebelum dan setelah simulasi untuk
  membuktikan jalur tersebut non-mutating.

## Increment 5C

- Endpoint dan halaman `/admin/policy-uat` menyediakan paket skenario immutable dan berversi tanpa
  menanam role, action, lokasi, kompetensi, atau expected outcome produksi ke source code.
- Setiap skenario merekam subject, action, lokasi, kompetensi, waktu opsional, expected outcome, dan
  expected code; batch menyimpan actual response simulator lengkap dan penanda match/mismatch.
- Coverage report menghitung expected/actual ALLOW-DENY, subject, action, lokasi, role, kompetensi,
  temporal cases, dan matched cases. Content pack serta report memiliki checksum SHA-256.
- Create suite dan run batch memerlukan `Idempotency-Key`; replay payload sama mengembalikan evidence
  pertama, sedangkan penggunaan key dengan payload atau suite berbeda ditolak HTTP `409`.
- Suite, run, configuration audit, outbox, dan receipt ditulis atomik. Tidak tersedia application
  path untuk mengubah atau menghapus paket/report historis.
- Activation readiness kini memerlukan latest passing run untuk policy version yang sama persis.
  Syarat ini fail-closed, tetapi tidak mengesahkan OPN-001/002 dan enforcement tetap nonaktif default.
- Integration test membuktikan version allocation, expected-vs-actual ALLOW/DENY, checksum, replay,
  payload mismatch, role Administrator, evidence atomik, dan passing-run activation prerequisite.

## Increment 6A

- Submit PTW langsung membentuk tahap `UNDER_REVIEW` dan satu evidence/task validasi HSSE untuk
  exact permit version.
- Task validasi persisten dibuat atomik saat submit. Task berikutnya untuk approval dan
  penerbitan dibuat setelah prerequisite sebelumnya selesai; `/api/v1/tasks` dan halaman **Tugas
  Saya** hanya mengembalikan task aktif sesuai role dan location scope actor.
- Approval pemilik area hanya dapat dijalankan setelah validasi HSSE selesai. Penerbitan merupakan
  command eksplisit terpisah yang mengevaluasi seluruh field guards dan baru kemudian membuat satu
  active work period.
- Semua transition baru memakai role dan location scope server-side, `If-Match`, `Idempotency-Key`,
  audit, outbox, dan transaction persistence yang sama dengan aggregate permit.
- UI Development menyediakan identity validator dan PIC area untuk menguji scope HO, ORF/Site
  Office, serta FSRU/Water-Based Activity. Istilah pengguna adalah **Diterbitkan**; `OPEN` hanya tetap
  sebagai status domain internal yang menandai hak kerja aktif.
- Satu identity PIC pemilik area Development menjalankan approval dan penerbitan untuk kelompok
  areanya. Server menolak penerbitan oleh actor lain meskipun actor tersebut memiliki role pemilik
  area. Assignment PIC konkret dan kompetensi production tetap menunggu pengesahan policy.
- Role/action konkret, kompetensi, SoD, assignment PIC, SLA, serta checklist/ambang lapangan final
  masih bergantung pada pengesahan OPN-002 sampai OPN-005. Activation gate tetap fail-closed dan
  nonaktif secara default.

## Increment 6B

- Endpoint eksplisit `request-revision` dan `reject` memakai alasan wajib, `If-Match`,
  `Idempotency-Key`, authorization server-side, audit, outbox, dan transaksi permit yang sama.
- PIC HSSE dapat mengambil keputusan tersebut saat `UNDER_REVIEW`; setelah validasi selesai,
  authority beralih ke PIC pemilik area pada `AWAITING_APPROVAL`.
- Request revision membatalkan task aktif dan menghapus evidence aktif. Sponsor harus memperbarui
  draft, menghasilkan version baru, lalu submit ulang untuk membentuk task validasi HSSE baru.
- Reject membatalkan seluruh task aktif dan menghasilkan terminal state `REJECTED`. UI meminta
  konfirmasi tambahan sebelum aksi irreversible tersebut dikirim.
- Activation readiness kini juga mensyaratkan mapping action untuk kedua command baru sebelum
  master authorization dapat diaktifkan.

## Increment 6C

- Route validasi operasional Distribusi Gas dipensiunkan. PTW in-flight `UNDER_REVIEW` yang sudah
  memiliki evidence HSSE dimajukan ke `AWAITING_APPROVAL`; task gas pending dibatalkan dan task HSSE
  atau approval yang hilang direkonsiliasi melalui additive data migration. Rekonsiliasi tersebut
  juga menulis audit event dan outbox message dalam migration transaction yang sama.
- Sponsor pemilik PTW dapat mengirim command permintaan penangguhan. Server langsung menghapus
  active work period dan memindahkan permit ke `SUSPENSION_REQUESTED`; approval PIC pemilik area
  hanya mengesahkan kondisi yang sudah fail-safe tersebut.
- Sponsor menyatakan pekerjaan selesai dan langsung menghentikan active work period. Konfirmasi
  HSSE dan PIC pemilik area dibuat sebagai dua task paralel; `WORK_COMPLETED` baru tercapai setelah
  keduanya lengkap.
- Hanya PIC pemilik area yang dapat menutup PTW dari `WORK_COMPLETED` ke terminal `CLOSED`.
- Seluruh command memakai role/ownership/location scope server-side, `If-Match`, `Idempotency-Key`,
  audit, outbox, dan task persistence dalam transaksi permit yang sama.
- Panel tindakan dashboard dan halaman Tugas Saya memakai task `PENDING` aktual yang telah difilter
  server berdasarkan role, actor assignment, dan cakupan lokasi; setiap task membuka detail PTW
  terkait tanpa menjalankan transition secara langsung. Navigasi utama menampilkan badge jumlah
  task aktif sebagai indikator atensi untuk akun saat ini; lonceng membuka ringkasan maksimal lima
  tugas beserta tautan ke permit dan halaman Tugas Saya.

## Increment 6D

- Sponsor pemilik PTW dapat menambah beberapa lampiran PDF secara dinamis serta melakukan logical
  removal selama permit berada pada `DRAFT` atau `REVISION_REQUIRED`.
- Setiap upload/remove memperbarui version dan ETag permit. Metadata attachment, immutable version
  snapshot, audit event, outbox, dan idempotency receipt disimpan dalam transaksi SQL yang sama.
- Binary disimpan melalui application storage port. Adapter Development memakai private filesystem,
  nama storage UUID, validasi ekstensi/signature PDF, streaming, checksum SHA-256, dan limit yang
  dikonfigurasi.
- Semua role yang lolos authorization parent permit dan location scope dapat melihat serta mengunduh
  lampiran aktif. File tidak diletakkan di web root dan response download memakai `nosniff`.
- Multiple selection Angular diproses sequential agar setiap response ETag menjadi input upload
  berikutnya; status dan kegagalan ditampilkan per file.
- Malware scan, object storage, klasifikasi, dan retention production masih terbuka. Konfigurasi
  non-Development menonaktifkan fitur dan mewajibkan scanner secara default. UI Development tidak
  menampilkan warning scan per lampiran karena kapabilitas tersebut berada di luar scope saat ini.

## Increment 6E

- Sponsor pemilik dapat menjalankan command eksplisit `RequestRenewal` hanya ketika PTW asal
  berstatus Diterbitkan dengan active work period dan masih berada dalam masa berlaku.
- Command membuat draft permit baru dan memperbarui version PTW asal secara atomik. Keduanya
  dihubungkan oleh `RenewedFromPermitId`, memiliki version snapshot sendiri, audit/outbox, concurrency
  `If-Match`, dan idempotency receipt.
- Data pekerjaan disalin dari snapshot PTW asal. Sponsor menentukan periode renewal yang tidak
  overlap dan tetap maksimum tujuh hari; attachment tidak disalin otomatis.
- Renewal memperoleh nomor baru ketika disubmit dan mengikuti validasi HSSE serta approval PIC
  pemilik area yang sama seperti permit biasa.
- Guard server menolak penerbitan renewal selama PTW asal masih aktif atau belum ditutup. UI
  menampilkan hubungan PTW asal dan successor serta form periode renewal khusus Sponsor.
- Mapping action production untuk `RequestRenewal`, perilaku pengajuan ulang setelah successor
  dibatalkan/ditolak, dan otomasi expiry/handover tetap membutuhkan pengesahan lanjutan.

## Belum diimplementasikan karena membutuhkan keputusan bisnis

- daftar resmi lokasi, owner/area authority, aturan overlap/delegasi, dan aktivasi master sebagai policy PTW (OPN-001);
- risk matrix, review route, final authority, daftar role/action, kompetensi, dan SoD detail untuk
  aktivasi framework assignment (OPN-002);
- checklist per permit class dan mapping final formulir (OPN-003);
- gas thresholds, units, test lifetime, retest, dan continuous monitoring (OPN-004);
- SLA, eskalasi, klasifikasi perubahan non-material, dan detail remediasi setelah revisi/reject
  (OPN-005);
- contractor acknowledgement mechanism (OPN-006);
- production IdP/SSO dan kontrak API/webhook E-SIMI (OPN-007);
- retention, electronic signature, data classification, RPO/RTO final (OPN-008);
- production topology dan HA (OPN-009).

## Urutan implementasi berikutnya

1. Lengkapi dan sahkan draft decision records OPN-001–009 bersama owner pada BRD.
2. Muat daftar lokasi resmi serta sahkan daftar role/action, kompetensi, dan matriks SoD; kemudian
   isi konfigurasi activation gate dan lakukan simulation/UAT sebelum enforcement dinyalakan.
3. Tambahkan E-SIMI adapter dan contract tests memakai hasil OPN-007.
4. Perluas paket UAT dengan declarative permit ruleset setelah OPN-003/004 disahkan, tanpa free-form
   scripting atau threshold/checklist default yang belum disetujui.
5. Sahkan assignment/SoD flow validasi HSSE, penangguhan, penyelesaian, dan penutupan; lalu lengkapi
   field operations berdasarkan OPN-002–006.
6. Tambahkan attachment quarantine/malware scanner dan object storage production, lalu reports,
   print, notifications, integration/E2E/security tests lanjutan.

Tidak ada default gas threshold, approval matrix, atau safety checklist yang ditanam diam-diam di source code.
