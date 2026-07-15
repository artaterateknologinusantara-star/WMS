# DEVELOPMENT_ROADMAP.md — SynteraWMS

> **Terakhir diperbarui:** 2026-07-15
> Versi awal dokumen ini (belum ada file sebelumnya di `/docs`). Fase dan urutan di bawah disusun ulang berdasarkan kondisi kode aktual saat ini, bukan sekadar disalin dari rencana lama.

---

## 1. Fase Pengembangan

```
[ Fase R&D ] ──▶ [ Fase MVP ] ──▶ [ Fase Pilot (Unilever) ] ──▶ [ Fase Production Rollout ]
   (saat ini)      (target berikut)   (lapangan terbatas)         (live, multi-site)
```

### Fase R&D — **posisi saat ini**
Fokus: memastikan alur inti (receiving → dispatch) benar secara bisnis dan aman secara data, sebelum menambah scope. Belum ada deployment infra (Dockerfile/CI) — ini **disengaja**, karena arsitektur & aturan bisnis masih berpotensi berubah selama R&D.

### Fase MVP (berikutnya)
Syarat keluar dari R&D menuju MVP:
- Seluruh perubahan WIP yang belum di-commit di `WMS-Frontend` sudah direview & masuk git history.
- Gap struktural pada bagian 3 (di bawah) sudah dituntaskan atau didokumentasikan sebagai keputusan sadar.
- Ada minimal 1 lingkungan (Docker/CI) yang bisa dipakai demo ke stakeholder non-teknis tanpa setup manual di laptop developer.

### Fase Pilot
- Modul Produksi (Fase 3 lama) kemungkinan **belum wajib** di fase ini — pilot bisa berjalan dengan lingkup WH murni (receiving–dispatch) dulu, produksi menyusul.
- Integrasi SAP realistis dimulai di sini paling awal (butuh kepastian versi SAP dari tim IT Unilever/klien).

### Production Rollout
- SAP integration, SignalR realtime, multi-role UAT penuh (termasuk `InboundStaff`/`OutboundStaff` yang saat ini belum punya akun seed untuk diuji).

---

## 2. Milestone & Target Berikutnya (berdasarkan kondisi kode saat ini)

| # | Milestone | Kenapa ini berikutnya |
|---|---|---|
| M1 | **Bersihkan working tree** — review & commit seluruh perubahan pending di `WMS-Frontend` (dispatch, picking, putaway, receiving, inventory service) | Tidak aman menumpuk fitur baru di atas WIP yang belum ter-review; risiko konflik/hilang kerja meningkat tiap sesi. |
| M2 | **Perbaiki type error `PackingContent.tsx`** (status union tidak menyertakan `'cancelled'`) | Memblokir `tsc --noEmit` bersih — sinyal murah untuk dibereskan sebelum menambah kode baru di modul yang sama. |
| M3 | **Selesaikan sisa Master Data:** halaman Supplier List (frontend + backend) | Menyamakan modul Master Data (SKU ✅, Bin Location ✅, Supplier ⬜) supaya tidak setengah jalan. |
| M4 | **Putuskan pola MasterSKU/BinLocation:** tetap inline di controller (dokumentasikan sebagai pengecualian sadar) atau refactor ke service layer agar konsisten dengan 6 controller lain | Saat ini ambigu — bisa disalahartikan sebagai bug oleh developer baru. |
| M5 | **Tambahkan pagination** ke `GET /api/inventory`, `/api/mastersku`, `/api/binlocation` | Murah dikerjakan sekarang (dataset masih kecil), mahal jika ditunda sampai data produksi masuk. |
| M6 | **Halaman Users & Roles + Warehouse Config + Reports** | Melengkapi sisa nav sidebar yang masih dead-link. |
| M7 | **Dashboard realtime + 2 chart yang belum bersumber data** (Receiving Volume, Supplier-SKU) | Termasuk Phase 1 lama (auto-refresh dashboard) yang belum tuntas. |
| M8 | **Environment & deployment dasar** — minimal Dockerfile + docker-compose (API + SQL Server + frontend) | Prasyarat keluar dari R&D menuju MVP; saat ini nol infrastruktur deployment. |
| M9 | **Modul Produksi** (6 entity baru, 4 controller, 6 halaman) | Setelah fondasi WH stabil — sesuai prinsip "stabilize before expanding". |
| M10 | **Integrasi SAP + SignalR realtime** | Fase paling akhir, tergantung kepastian versi SAP dari pihak eksternal (Unilever IT). |

---

## 3. Dependency Antar Modul

```
Master Data (SKU, Bin Location, Supplier)
        │  (data referensi wajib ada duluan)
        ▼
Receiving ──▶ Putaway ──▶ Inventory (view/adjustment)
                                │
                                ▼
                        Picking (planning + eksekusi)
                                │
                                ▼
                            Dispatch
                                │
                                ▼
                    Dashboard / Reporting
                                │
                                ▼
                    Modul Produksi (butuh Picking
                    matang — PPIC trigger auto-create
                    picking task dari Production Order)
                                │
                                ▼
                    Integrasi SAP (butuh model data
                    final dari WH + Produksi sebelum
                    mapping BAPI/IDoc bisa disepakati)
```

Implikasi urutan kerja:
- **Supplier List** (Master Data) sebaiknya selesai sebelum ada laporan/Reports yang butuh grouping by supplier.
- **Modul Produksi tidak bisa mulai** sebelum Picking benar-benar stabil (planning + eksekusi + cancel + force-complete) — komponen ini **sudah selesai**, jadi secara teknis Produksi *bisa* mulai kapan saja, tapi prinsip kerja proyek ("stabilize before expanding") mengharuskan M1–M8 selesai dulu.
- **SAP Integration** butuh skema data yang stabil dari WH *dan* Produksi — mengubah skema setelah integrasi SAP berjalan jauh lebih mahal daripada sebelum itu.
- **SignalR/realtime** paling efisien dikerjakan sekali untuk dashboard *dan* notifikasi *dan* SAP callback — jangan diimplementasikan terpisah 3x di 3 fase berbeda.

---

## 4. Estimasi Kompleksitas & Risiko Teknis

| Item | Kompleksitas | Risiko | Alasan (berdasarkan kode) |
|---|:---:|:---:|---|
| Bersihkan working tree WIP (M1) | Low | Medium | Bukan kerja teknis berat, tapi risiko kehilangan konteks perubahan jika ditunda terlalu lama. |
| Fix type error PackingContent (M2) | Low | Low | Satu baris perbandingan union type, kemungkinan cukup memperluas type atau memperbaiki logic filter. |
| Halaman Supplier List (M3) | Low | Low | Pola sudah ada 2x (SKU, Bin Location) — tinggal replikasi, backend perlu 1 entity+controller baru. |
| Refactor/dokumentasi MasterSKU/BinLocation (M4) | Medium | Low | Bukan bug fungsional, tapi menyentuh 2 controller yang sudah dipakai frontend — perlu regresi test manual. |
| Pagination 3 endpoint (M5) | Medium | Medium | Mengubah shape response (`data` jadi list vs. `data`+`meta`) berarti breaking change ke service frontend yang sudah ada — perlu koordinasi FE+BE serentak. |
| Users & Roles + Warehouse Config + Reports (M6) | High | Medium | 3 halaman + kemungkinan beberapa entity/endpoint baru (laporan agregasi belum ada polanya sama sekali di backend). |
| Dashboard realtime + 2 chart (M7) | Medium | Medium | Butuh keputusan arsitektur (polling vs SignalR) — kalau pilih SignalR di sini, sebagian pekerjaan Fase 4 (M10) jadi lebih murah nantinya. |
| Environment/deployment dasar (M8) | Medium | High | Belum ada preseden sama sekali di repo (nol Dockerfile/CI) — risiko tinggi karena scope "belum pernah dicoba", bukan karena sulit secara teknis. |
| Modul Produksi (M9) | High | High | 6 entity baru + trigger otomatis dari PPIC ke Picking — titik integrasi paling kompleks di seluruh sistem saat ini karena menyambung ke modul yang sudah live (Picking) tanpa boleh merusak alur outbound yang sudah berjalan. |
| Integrasi SAP (M10) | High | High | Bergantung pada keputusan eksternal (versi SAP dari Unilever IT) yang di luar kendali tim — risiko *jadwal*, bukan cuma risiko teknis. Mapping BAPI/IDoc adalah pekerjaan integrasi enterprise klasik yang rawan meleset dari estimasi awal. |

---

## 5. Prinsip yang Tetap Berlaku

- **Stabilize before expanding** — jangan mulai M9 (Produksi) sebelum M1–M8 tuntas, walau secara teknis Picking sudah cukup matang untuk jadi fondasi.
- **Konvensi `FIX-[number]-[short-name]`** tetap dipakai untuk setiap perbaikan, termasuk item-item di milestone M1–M8 di atas.
- Keputusan desain yang sudah disengaja (dispatch cancel tanpa rollback stok, putaway guard hanya pakai `PalletId`) **tidak berubah** oleh roadmap ini.
