# ARCHITECTURE.md — SynteraWMS

> **Terakhir diperbarui:** 2026-07-15
> Versi awal dokumen ini (belum ada file sebelumnya di `/docs`).

---

## 1. Gambaran Arsitektur Sistem

SynteraWMS adalah **monolith 2-repo**: satu backend API monolitik (bukan microservices) dan satu frontend Next.js terpisah yang berkomunikasi murni lewat REST/JSON. Tidak ada message queue, tidak ada event bus, tidak ada service mesh — semua komunikasi bersifat **synchronous request/response**.

```
┌─────────────────────────┐          ┌──────────────────────────────┐          ┌───────────────────────┐
│   WMS-Frontend           │  HTTPS   │   WMS (Backend API)           │  EF Core │   SQL Server Express   │
│   Next.js 15 (App Router)│ ───────▶ │   ASP.NET Core 8.0 Monolith   │ ───────▶ │   SynteraWMS_Enterprise │
│   Port 4028              │  JWT     │   Port 5000                   │          │   localhost\SQLEXPRESS  │
│                          │ ◀─────── │                                │ ◀─────── │                         │
└─────────────────────────┘  JSON    └──────────────────────────────┘  SQL     └───────────────────────┘
                                              │
                                              │ (belum diimplementasikan)
                                              ▼
                                   ┌───────────────────────────┐
                                   │  Integration Layer (SAP)   │  ← Fase 4, belum ada kode
                                   │  RFC/BAPI atau IDoc        │
                                   └───────────────────────────┘
```

Tidak ada reverse proxy, API gateway, atau load balancer di setup saat ini — backend diakses langsung di `http://localhost:5000`, frontend langsung memanggil `NEXT_PUBLIC_API_URL` (default sama). Ini konsisten dengan status "R&D": arsitektur deployment belum jadi concern.

---

## 2. Tech Stack

### Backend (`c:\Users\Administrator\WMS`)

| Layer | Teknologi | Versi |
|---|---|---|
| Framework | ASP.NET Core | 8.0 |
| ORM | Entity Framework Core (SqlServer) | 8.0.5 |
| EF Tools | Microsoft.EntityFrameworkCore.Tools | 10.0.8 |
| Database | SQL Server Express | lokal, `localhost\SQLEXPRESS` |
| Auth | Microsoft.AspNetCore.Authentication.JwtBearer | 8.0.5 |
| Password hashing | BCrypt.Net-Next | 4.0.3 |
| API docs | Swashbuckle (Swagger) | 6.5.0 |

Tidak ada library tambahan untuk logging terstruktur, validasi (FluentValidation, dll), caching, atau background job — semuanya masih native ASP.NET Core + EF Core.

### Frontend (`c:\Users\Administrator\WMS-Frontend`)

| Layer | Teknologi | Versi |
|---|---|---|
| Framework | Next.js (App Router) | 15.1.11 |
| UI Library | React | 19.0.3 |
| Bahasa | TypeScript (strict mode) | ^5 |
| Styling | Tailwind CSS | 3.4.6 |
| Ikon | Lucide React, Heroicons | 1.7.0 / 2.2.0 |
| Chart | Recharts | 2.15.2 |
| Form | React Hook Form | 7.75.0 |
| Barcode | react-barcode | 1.6.1 |

**Catatan:** `package.json` masih menyertakan `@dhiwise/component-tagger` dan blok metadata `rocketCritical` — sisa dari template/scaffold generator saat proyek pertama kali dibuat. Belum ada dampak fungsional, tapi ini sinyal bahwa proyek belum melalui proses "cleanup pasca-scaffold".

---

## 3. Struktur Folder

### Backend

```
WMS/
├── Controllers/         → 10 controller — HTTP layer, tipis, delegasi ke Services
│   ├── AuthController.cs
│   ├── InventoryController.cs
│   ├── InventoryAdjustmentController.cs
│   ├── PutawayController.cs
│   ├── ReceivingController.cs
│   ├── BinLocationController.cs      ← tidak punya service terpisah (lihat §5)
│   ├── MasterSKUController.cs        ← tidak punya service terpisah (lihat §5)
│   ├── PickingController.cs
│   ├── DispatchController.cs
│   └── DashboardController.cs
├── Services/            → 8 service — business logic, transaksi, audit trail, DTO inline
│   ├── AuthService.cs
│   ├── ReceivingService.cs
│   ├── PutawayService.cs
│   ├── InventoryService.cs
│   ├── AdjustmentApprovalService.cs
│   ├── PickingService.cs
│   ├── DispatchService.cs
│   └── DbSeeder.cs       ← idempotent, dipanggil di startup (Program.cs)
├── Models/               → 15 entity class EF Core
├── Data/                 → ApplicationDbContext
├── Migrations/           → 4 migrasi EF Core
├── Program.cs            → DI, CORS, JWT, 5 authorization policy, startup seeding
└── appsettings.json       → satu-satunya file config (belum ada Development/Production terpisah)
```

### Frontend

```
WMS-Frontend/
├── src/app/                          → App Router (Next.js 15)
│   ├── layout.tsx, page.tsx (= Inbound Receiving)
│   ├── login/
│   ├── dashboard/
│   ├── putaway/
│   ├── inventory/{stock-on-hand, adjustment, adjustment-approval}/
│   ├── outbound/{picking, packing, dispatch}/
│   ├── master/{sku, bin-location}/    ← baru (FIX-H10)
│   └── components/                    → komponen level-halaman untuk Inbound
├── src/components/                    → AppLayout, Sidebar, komponen UI reusable
├── src/lib/
│   ├── context/AuthContext.tsx        → state auth global (useAuth)
│   └── services/                      → API client per domain (fetch-based, 1 file per modul)
└── src/middleware.ts                  → proteksi route berbasis cookie
```

Setiap halaman fitur mengikuti pola: `page.tsx` (server wrapper, `<AppLayout>`) + `components/<Nama>Content.tsx` (client component, `'use client'`, berisi seluruh state & logic).

---

## 4. Pola Desain / Arsitektur

- **Monolith berlapis (layered monolith)** pada backend: `Controller → Service → EF Core DbContext → SQL Server`. Bukan microservices, bukan CQRS, bukan event-driven — pilihan yang wajar untuk tahap R&D dengan 1 tim kecil.
- **Inline DTO pattern** — DTO request/response didefinisikan di bagian bawah file service yang memilikinya, **bukan** di folder `DTOs/` terpisah. Ini konsisten di 6 dari 8 service.
- **Transactional consistency** — setiap mutasi multi-langkah wajib `BeginTransactionAsync()` eksplisit + `StockMovement` sebagai audit trail. **Pengecualian:** `MasterSKUController` dan `BinLocationController` melakukan mutasi langsung lewat `SaveChangesAsync()` tanpa transaksi eksplisit (karena mutasinya single-statement, risikonya rendah, tapi tetap menyimpang dari pola yang didokumentasikan).
- **State machine eksplisit** untuk entity kunci:
  - `InventoryStock.Status`: `Active → Outbound Staging → Dispatched | Empty`
  - `PickingDetail.Status`: `pending → in-progress → picked | cancelled | error`
  - `DispatchHeader.Status`: `pending → dispatched | cancelled`
- **RBAC berbasis policy bernama** (bukan role-check manual di tiap endpoint) — 5 policy didefinisikan sekali di `Program.cs`, dipakai via atribut `[Authorize(Policy = "...")]` di controller/action level.
- **Frontend: Service layer per domain** — tidak ada `fetch()` langsung di komponen; semua lewat `src/lib/services/*.service.ts`. Pola ini konsisten di seluruh frontend termasuk 2 halaman Master Data yang baru.
- **Client-side role gating** di frontend (`user?.role === 'SuperAdmin' || user?.role === 'WarehouseManager'`) untuk menyembunyikan tombol aksi yang di backend memang dibatasi `ManagerOnly` — ini murni UX (backend tetap jadi source of truth otorisasi via JWT role claim).

---

## 5. Catatan Teknis Penting

### Environment & Konfigurasi
- **Satu environment** — hanya `appsettings.json`, tidak ada `appsettings.Development.json` / `appsettings.Production.json`. Semua secret (termasuk JWT key) ada di file yang sama dan ter-commit ke source control.
- **Frontend** pakai `NEXT_PUBLIC_API_URL` (default `http://localhost:5000/api`), bisa di-override lewat `.env.local`.
- **CORS terbuka penuh** (`AllowAnyOrigin/Header/Method`) di `Program.cs` — perlu dibatasi sebelum ada environment publik.

### Deployment
- **Tidak ada Dockerfile, docker-compose, atau workflow CI/CD** di kedua repo — deployment saat ini 100% manual (`dotnet run` + `npm run dev` di mesin developer/server yang sama).
- Database di-seed otomatis saat startup lewat `DbSeeder.SeedAsync()` (idempotent — aman dijalankan berulang).

### Konvensi Kode
- **C#:** primary constructor syntax (`public class Foo(Dependency dep)`) dipakai di beberapa service/controller baru (mis. `DashboardController`), tapi belum konsisten di semua file — beberapa controller lama masih pakai constructor eksplisit klasik.
- **TypeScript:** strict mode aktif, `interface` untuk shape objek, `type` untuk union — dipakai konsisten di seluruh service frontend.
- **Response shape backend:** selalu `{ success: bool, data: ... }` atau `{ success: false, message: "..." }` — dipegang konsisten di semua controller termasuk 2 yang baru (`MasterSKUController`, `BinLocationController`).
- **Bahasa pesan operator:** sedang dalam transisi ke Bahasa Indonesia. Modul Master Data (SKU + Bin Location) sudah 100% Indonesia (backend & frontend). Modul lain (Receiving, Putaway, Picking, Dispatch, Adjustment) **masih berbahasa Inggris** — belum dilokalisasi.

### Integrasi Eksternal
- **Belum ada integrasi eksternal aktif.** SAP (Fase 4) baru sebatas rencana arsitektur di dokumen — mapping `BAPI_GOODSMVT_CREATE`, `BAPI_MATPHYSINV_COUNT`, `BAPI_PO_GETDETAIL` disebut sebagai target, tapi tidak ada satu baris kode integrasi pun di repo saat ini.
- **Belum ada realtime layer** (SignalR/WebSocket) — dashboard dan aktivitas stok masih pull-based (client manual refresh / re-fetch).

### Testing
- Ada 1 script `e2e-test.ps1` di root backend (PowerShell) — smoke test end-to-end lewat `Invoke-RestMethod` ke API + `sqlcmd` langsung ke database untuk seeding data uji. Ini **bukan** test framework formal (xUnit/NUnit/Jest) — tidak ditemukan satu pun unit test project di kedua repo.
