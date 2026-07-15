# PROJECT_STATUS.md — SynteraWMS

> **Terakhir diperbarui:** 2026-07-15
> Dokumen ini adalah **versi awal** (belum ada file sebelumnya di `/docs`). Sumber kebenaran diambil langsung dari kondisi kode di kedua repo (`WMS` backend dan `WMS-Frontend`), bukan asumsi dari dokumen historis.

---

## 1. Ringkasan Proyek

**SynteraWMS** adalah *Warehouse Management System* (WMS) enterprise yang sedang dikembangkan untuk kebutuhan **manufacture/logistik** (konteks awal: digitalisasi logistik Unilever). Proyek ini **masih dalam tahap R&D (Research & Development)** — belum ada infrastruktur deployment (tidak ada Dockerfile, tidak ada CI/CD pipeline, hanya satu environment config), dan seluruh pengujian masih dilakukan secara lokal (`localhost\SQLEXPRESS`, backend port 5000, frontend port 4028).

Cakupan sistem saat ini: **Receiving → Putaway → Inventory → Adjustment → Picking → Dispatch**, ditambah Dashboard ringkasan. Modul produksi dan integrasi SAP direncanakan namun **belum dimulai sama sekali** (tidak ada satu pun file kode untuk itu).

| Item | Detail |
|---|---|
| Backend | ASP.NET Core 8.0 — `Syntera.WMS.API.csproj` |
| Frontend | Next.js 15.1.11 (App Router), React 19 |
| Database | SQL Server Express, `SynteraWMS_Enterprise` |
| Auth | JWT Bearer + BCrypt |
| Migration terakhir | `20260525124542_AddDispatchTables` |

---

## 2. Fitur yang Sudah Selesai ✅

### Backend (10 controller, 8 service)
- [x] **Auth** — login JWT (`/api/auth/login`), 8 jam expiry, role claim
- [x] **Receiving** — submit penerimaan barang, auto-palletize (maks 50 qty/pallet), status Draft → Putaway
- [x] **Putaway** — konfirmasi pallet ke bin, satu-satunya pembuat `InventoryStock`, guard 1 pallet/bin
- [x] **Inventory (view)** — agregasi stok, lookup by SKU code, daftar pallet per SKU, daftar UOM
- [x] **Inventory Adjustment** — submit (Pending), approve/reject (ManagerOnly), audit `StockMovement`
- [x] **Picking (planning)** — FIFO reservation, split otomatis lintas pallet, nomor `PCK-YYYY-NNN`
- [x] **Picking (eksekusi fisik)** — scan validasi rak+pallet, staging otomatis, partial pick, idempotent recovery
- [x] **Picking cancel & force-complete** — release reservasi + audit, penutupan header terminal
- [x] **Dispatch** — create dari staging, confirm (finalisasi stok, BAST data), cancel (khusus status pending)
- [x] **Dashboard** — `/summary` (KPI inbound/inventory/outbound/adjustment/stock-by-zone) dan `/activity` (15 pergerakan stok terakhir)
- [x] **RBAC** — 5 policy bernama (`ManagerOnly`, `InboundAccess`, `OutboundAccess`, `InventoryAccess`, `AnyStaff`)
- [x] **Master Data (backend)** — `MasterSKUController` (CRUD + deactivate) dan `BinLocationController` (create + toggle-active) sudah lengkap

### Frontend (11 halaman)
- [x] Login, Dashboard, Inbound Receiving (`/`), Putaway, Stock On Hand, Inventory Adjustment, Adjustment Approval
- [x] Picking List (planning), Picking Process (eksekusi — scan rak/pallet), Dispatch (+ modal BAST)
- [x] **Master SKU** (`/master/sku`) — list, tambah, edit, deactivate — **selesai sesi ini**
- [x] **Bin Location** (`/master/bin-location`) — list, tambah, aktifkan/nonaktifkan (dengan guard bin terisi) — **baru dibuat sesi ini**
- [x] Pesan operator pada kedua halaman Master Data di atas sudah dilokalisasi ke Bahasa Indonesia (termasuk pesan error dari backend `MasterSKUController`/`BinLocationController`)

> Ini melengkapi **FIX-H10 (master-sku-binlocation-frontend)** yang sebelumnya berstatus in-progress — sekarang selesai.

---

## 3. Sedang Dikerjakan 🔄

- **Perubahan belum di-commit** di kedua repo (working tree kotor):
  - Backend: bersih, tidak ada perubahan pending selain `MasterSKUController.cs`/`BinLocationController.cs` yang baru dilokalisasi sesi ini.
  - Frontend: banyak file termodifikasi belum commit — `DispatchContent.tsx`, `PackingContent.tsx`, `PickingListContent.tsx`, `PickingProcessModal.tsx`, `PutawayContent.tsx`, `PutawayAssignModal.tsx`, `InboundReceivingContent.tsx`, beberapa service (`picking.service.ts`, `putaway.service.ts`, `receiving.service.ts`, `inventory.service.ts`, `adjustment.service.ts`), plus folder `src/app/master/` (baru, belum pernah di-commit).
  - **Perlu direview dan di-commit** sebelum lanjut ke fitur baru — ini adalah pekerjaan yang belum "selesai" secara administratif meskipun secara fungsional sudah berjalan.
- Tidak ditemukan penanda `TODO` / `FIXME` / `HACK` eksplisit di source code (baik `.cs` maupun `.tsx`) — backlog saat ini bersumber dari gap struktural yang ditemukan lewat inspeksi kode (lihat bagian 5).

---

## 4. Belum Dikerjakan / Backlog 📋

| Item | Keterangan |
|---|---|
| Modul Produksi | Belum ada satu pun entity/controller/halaman. Direncanakan Fase 3. |
| Integrasi SAP | Belum ada kode integrasi sama sekali. Direncanakan Fase 4. |
| Halaman Supplier List | Nav sidebar sudah ada (`href="#"`), halaman belum dibuat, endpoint backend belum ada. |
| Halaman Users & Roles | Nav sidebar ada (`href="#"`), belum ada halaman maupun endpoint CRUD user. |
| Halaman Warehouse Config | Nav sidebar ada (`href="#"`), belum ada implementasi. |
| Halaman Reports (3 jenis) | Receiving Report, Putaway Report, Stock Movement — semua `href="#"`, tidak ada endpoint agregasi laporan. |
| Export BAST ke PDF | Saat ini BAST hanya `window.print()` dari browser, tidak ada generate PDF di server. |
| Dashboard realtime | Belum ada SignalR/websocket; dashboard perlu manual refresh. 2 chart (Receiving Volume, Supplier-SKU) belum punya data source API. |
| Sistem notifikasi | Belum ada infrastruktur notifikasi sama sekali (in-app maupun push). |
| Endpoint GET StockMovement | Belum ada endpoint untuk query histori pergerakan stok secara umum (hanya lewat Dashboard Activity yang dibatasi 15 baris). |
| Pagination | `GET /api/inventory`, `/api/mastersku`, `/api/binlocation` semua mengembalikan seluruh dataset tanpa paging — berisiko saat data bertambah besar. |
| Soft-delete MasterSKU | Sudah ada "deactivate" (status Inactive), belum ada mekanisme hapus permanen/arsip. |
| Health-check endpoint | Belum ada `/health` atau endpoint monitoring uptime. |

---

## 5. Isu / Blocker Teridentifikasi dari Kode ⚠️

1. **`InventoryController.Get()` mengembalikan `category` kosong secara hardcode** — ada komentar eksplisit `// Will be populated if needed` (`Controllers/InventoryController.cs:38`). Ini adalah fitur yang sengaja belum diselesaikan.
2. **JWT secret key ter-hardcode di `appsettings.json`** (bukan environment variable/secrets manager) — sudah diketahui sebagai gap keamanan sebelum production.
3. **CORS sepenuhnya terbuka** (`AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()` di `Program.cs`) — perlu dibatasi ke origin frontend sebelum rollout.
4. **Inkonsistensi arsitektur:** `MasterSKUController` dan `BinLocationController` **tidak punya service layer terpisah** — seluruh logika (termasuk transaksi implisit via `SaveChangesAsync` tanpa `BeginTransactionAsync` eksplisit) ditulis langsung di controller. Ini menyimpang dari pola "thin controller → service layer" yang dipakai 6 controller lainnya.
5. **Role `InboundStaff` dan `OutboundStaff` tidak memiliki akun seed** — role-nya terdaftar di `DbSeeder` dan dipakai di authorization policy, tapi tidak ada user dengan role tersebut untuk pengujian end-to-end alur inbound/outbound dari sudut pandang staff (hanya bisa diuji lewat `admin`/`manager`/`staff`).
6. **Tidak ada `Dockerfile`, `docker-compose`, maupun workflow CI/CD** di kedua repo — konsisten dengan status "R&D", tapi berarti belum ada jalur otomatis menuju staging/production.
7. **Beberapa nav sidebar adalah dead link (`href="#"`)**: Supplier List, 3 item Reports, Users & Roles, Warehouse Config — berpotensi membingungkan operator jika UI ini sudah mulai di-demo-kan.
8. **Halaman `Adjustment Approval` tidak punya entri di sidebar** — hanya bisa diakses lewat URL langsung (`/inventory/adjustment-approval`), padahal halamannya sudah live.
9. **Type error pre-existing** di `src/app/outbound/packing/components/PackingContent.tsx:49` — perbandingan status `'cancelled'` terhadap union type yang tidak menyertakan `'cancelled'`. Muncul dari perubahan WIP yang belum di-commit (bukan dari pekerjaan sesi ini), tapi memblokir `tsc --noEmit` bersih.
10. **`package.json` frontend masih menyisakan metadata dari template generator** (key `rocketCritical`, dependency `@dhiwise/component-tagger`) — indikasi proyek di-bootstrap dari scaffold/tool eksternal, belum "dibersihkan".

---

## 6. Ringkasan Angka

- **Entities:** 15 (backend)
- **Controllers:** 10
- **Services:** 8 (2 controller — MasterSKU, BinLocation — tidak punya service terpisah, lihat isu #4)
- **Halaman frontend live:** 11
- **Migrasi database:** 4 (`InitialCreate`, `AddPickingModule`, `AddPickingDetailSuggestions`, `AddDispatchTables`)
