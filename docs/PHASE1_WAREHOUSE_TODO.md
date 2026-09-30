# PHASE1_WAREHOUSE_TODO.md — SynteraWMS

> Dibuat: 2026-07-15
> Sumber: gap analysis dari PPT "WMS Improvement" dibandingkan kondisi kode saat ini (lihat `CLAUDE.md`, `PROJECT_STATUS.md`, `ARCHITECTURE.md`).
> Status checklist mencerminkan kondisi implementasi, bukan urutan pengerjaan wajib — Bagian A dikerjakan lebih dulu sesuai arahan.

---

## Bagian A — Selaraskan dengan PPT

- [x] Tambah Batch/Lot dan Expired Date ke InventoryStock (+ ReceivingDetail sebagai sumber data) — **selesai penuh end-to-end**: migrasi `20260715112309_AddBatchExpiredDate` (nullable), input per baris SKU di form Receiving, disalin `ReceivingDetail` → `InventoryStock` di `PutawayService`, ditampilkan di `GET /putaway/stock/{palletId}`, `GET /api/inventory`, dan tabel Stock on Hand (frontend). Diuji manual: submit receiving dengan Batch/Expired → confirm Putaway → muncul benar di Stock on Hand.
- [x] Hybrid FEFO/FIFO pada Picking (`PickingService.CreatePickingAsync`) — kolom `Category.RequiresFEFO` (migrasi `20260715114537_AddRequiresFEFOToCategory`, default `false`, nullable-safe). **FEFO** (urut `ExpiredDate` terdekat, null di akhir): kategori **FMCG**, **Raw Material**. **FIFO** (urut `CreatedAt`, tidak berubah): kategori **Electronics**, **Packaging** — tidak ada kategori "cleaning/kimia" di seed data saat ini. Info FEFO/FIFO ditampilkan read-only di halaman Master SKU (badge per baris + label di dropdown kategori). Diuji manual: SKU FMCG dengan 2 pallet expired berbeda → sistem menyarankan pallet expired terdekat meski bukan yang paling lama di-putaway (FEFO override FIFO, terverifikasi); SKU Electronics tetap FIFO seperti semula (regresi terverifikasi).
- [x] Tambah gerbang QC (pass/fail) antara Receiving dan Putaway — level per `ReceivingDetail` (per pallet), aktor sama dengan staff Putaway (`InboundAccess`, tanpa role QC Inspector terpisah). Field: `QCStatus` (default `"Pending"`, migrasi `20260715144806_AddQCGateToReceivingDetail`), `QCCheckedBy`, `QCCheckedAt`, `QCRemarks` (semua nullable kecuali QCStatus). **Enforcement ganda**: hard block di `PutawayService.ConfirmPutawayAsync` (throw `InvalidOperationException` kalau `QCStatus != "Passed"`) + kolom QCStatus ditampilkan di `GET /putaway/pending` untuk UI (baris belum lolos QC tetap tampil, tombolnya beda). Endpoint baru: `POST /api/putaway/qc-check`. UI di halaman Putaway (bukan Receiving) — kolom "QC Status" + tombol kondisional "QC Check" (Pending/Failed) vs "Assign Bin" (Passed) + modal `QCCheckModal.tsx`. Data lama (pre-migrasi) sengaja **tidak** di-backfill — semua `ReceivingDetail` existing yang belum di-putaway otomatis `"Pending"` dan wajib di-QC ulang. Diuji manual: (a) putaway pallet `QCStatus=Pending` langsung ditolak (400) tanpa QC dulu; (b) submit QC Passed → putaway sukses (200); (c) submit QC Failed → putaway tetap ditolak (400), `QCRemarks` tersimpan & terbaca balik. Regresi guard "1 pallet per bin" lama terverifikasi masih jalan (tidak terpengaruh perubahan QC).
- [ ] Tambah proses RTV (Return to Vendor) untuk material gagal QC
- [ ] Tambah cetak label setelah GR (material number, deskripsi, qty, tanggal masuk, expired, lokasi bin)
- [ ] Tambah auto-suggest bin kosong saat Putaway
- [ ] Modul cycle count / stock take dasar
- [ ] Review konvensi penamaan racking vs struktur BinLocation saat ini

## Bagian B — Beres-beres teknis

- [ ] Bersihkan & commit WIP frontend
- [ ] Fix type error PackingContent.tsx
- [ ] Halaman Supplier List
- [ ] Putuskan pola MasterSKU/BinLocation (refactor atau dokumentasikan sebagai exception)
- [ ] Pagination /inventory, /mastersku, /binlocation
- [ ] Halaman Users & Roles, Warehouse Config, Reports
- [ ] Dashboard realtime + 2 chart
- [ ] Dockerfile + docker-compose minimal

## Bagian C — Backlog kecil

- [ ] Frontend cancel picking
- [ ] Frontend cancel dispatch
- [ ] Role-based UI filtering di Sidebar.tsx
