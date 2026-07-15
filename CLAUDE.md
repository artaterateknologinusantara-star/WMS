# CLAUDE.md — SynteraWMS Backend (ASP.NET Core 8.0)

## Project Overview

Warehouse Management System backend API built with ASP.NET Core 8.0 and Entity Framework Core. Covers inbound receiving, putaway, inventory management, adjustment approval, outbound picking, dispatch, and dashboard reporting.

- **Solution file:** `Syntera.WMS.API.csproj`
- **Database:** `SynteraWMS_Enterprise` on `localhost\SQLEXPRESS`
- **Backend port:** `http://localhost:5000`
- **Frontend:** `c:\Users\Administrator\WMS-Frontend` (Next.js 15, port 4028)

---

## Tech Stack

| Layer | Technology | Version |
|-------|-----------|---------|
| Framework | ASP.NET Core | 8.0 |
| ORM | Entity Framework Core | 8.0.5 (SqlServer + Tools 10.0.8) |
| Database | SQL Server Express | local SQLEXPRESS |
| Auth | JWT Bearer | 8.0.5 |
| Password Hashing | BCrypt.Net-Next | 4.0.3 |
| API Docs | Swashbuckle/Swagger | 6.5.0 |

---

## Architecture

```
Controllers/     → HTTP layer, thin — delegates to services
Services/        → Business logic, transactions, audit trail, inline DTOs
Models/          → EF entity classes (15 entities)
Data/            → ApplicationDbContext, DbSeeder
Migrations/      → EF migrations
```

### Patterns in Use
- **Service Layer:** All business logic lives in Services; controllers only route and validate HTTP.
- **Inline DTO Pattern:** Request/Response DTOs are defined at the bottom of each service file — no separate `DTOs/` folder.
- **Transaction Management:** Use `await using var tx = await _context.Database.BeginTransactionAsync()` for all multi-step mutations.
- **Audit Trail:** Every stock mutation inserts a `StockMovement` record. No exceptions.
- **Approval Workflow:** Adjustments flow: `Pending → Approved | Rejected`. Only approved adjustments update stock.
- **DbSeeder:** Runs on startup via `SeedData()` — idempotent, checks before inserting.

---

## Controllers (10)

| Controller | Base Route | Auth Policy | Key Endpoints |
|-----------|-----------|-------------|--------------|
| AuthController | `/api/auth` | Public | POST /login |
| InventoryController | `/api/inventory` | AnyStaff | GET list, GET /by-code/{skuCode}, GET /by-code/{skuCode}/pallets, GET /uom |
| InventoryAdjustmentController | `/api/InventoryAdjustment` | InventoryAccess | GET, POST, POST /{id}/approve (ManagerOnly), POST /{id}/reject (ManagerOnly) |
| PutawayController | `/api/putaway` | InboundAccess | POST /confirm, GET /pending, GET /stock/{palletId} |
| ReceivingController | `/api/receiving` | InboundAccess | GET history, POST submit |
| BinLocationController | `/api/binlocation` | AnyStaff | GET (with isOccupied), POST create (ManagerOnly), PATCH /{id}/toggle-active (ManagerOnly) |
| MasterSKUController | `/api/mastersku` | AnyStaff | GET list, GET /categories, POST (ManagerOnly), PUT /{id} (ManagerOnly), PATCH /{id}/deactivate (ManagerOnly) |
| PickingController | `/api/picking` | OutboundAccess | GET list, POST create, POST /{id}/confirm, POST /{id}/cancel, POST /{headerId}/force-complete, GET /check-stock, GET /staging-locations |
| DispatchController | `/api/dispatch` | OutboundAccess | GET list, GET /staging-items, POST create, POST /{id}/confirm, POST /{id}/cancel |
| DashboardController | `/api/dashboard` | AnyStaff | GET /summary, GET /activity |

---

## Controller Endpoint Details

Per-endpoint description for every controller (expands the compact table above).

### AuthController `/api/auth` — Public
| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | /login | Validate credentials, return JWT |

### InventoryController `/api/inventory` — AnyStaff
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | / | All InventoryStock records |
| GET | /by-code/{skuCode} | Aggregated stock for one SKU |
| GET | /by-code/{skuCode}/pallets | Individual pallet records per SKU |
| GET | /uom | List of UOM |

### InventoryAdjustmentController `/api/InventoryAdjustment` — InventoryAccess / ManagerOnly
| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| GET | / | InventoryAccess | All adjustments |
| POST | / | InventoryAccess | Submit new adjustment request |
| POST | /{id}/approve | ManagerOnly | Approve + apply stock change (blocked if SKU has `ReservedQty > 0` in an active picking task — see Business Rules) |
| POST | /{id}/reject | ManagerOnly | Reject (status-only) |

### PutawayController `/api/putaway` — InboundAccess
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | /pending | Pallets not yet put away |
| GET | /stock/{palletId} | Stock position for a pallet |
| POST | /confirm | Confirm pallet → bin (creates InventoryStock) |

### ReceivingController `/api/receiving` — InboundAccess
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | / | Receiving history |
| POST | / | Submit inbound receiving |

### BinLocationController `/api/binlocation` — AnyStaff / ManagerOnly
| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| GET | / | AnyStaff | All bins with `isOccupied` flag (all, including inactive) |
| POST | / | ManagerOnly | Create new bin location |
| PATCH | /{id}/toggle-active | ManagerOnly | Toggle IsActive (blocks if occupied) |

### MasterSKUController `/api/mastersku` — AnyStaff / ManagerOnly
| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| GET | / | AnyStaff | All SKUs (with category, UOM, isActive, qty) |
| GET | /categories | AnyStaff | All categories |
| POST | / | ManagerOnly | Create new SKU |
| PUT | /{id} | ManagerOnly | Update SKU (code, name, category, UOM) |
| PATCH | /{id}/deactivate | ManagerOnly | Deactivate SKU (Status → "Inactive") |

### PickingController `/api/picking` — OutboundAccess
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | / | All picking details (flat list for UI table) |
| GET | /check-stock | Live stock check + FIFO suggestion for SKU |
| GET | /staging-locations | Active Outbound Staging bins |
| POST | / | Create picking task (planning; FIFO auto-suggest, reserves stock) |
| POST | /{id}/confirm | Confirm physical pick (validate scan rack/pallet, move to staging) |
| POST | /{id}/cancel | Cancel picking detail (release reservation, StockMovement "Cancellation") |
| POST | /{headerId}/force-complete | Force-close terminal header (all details picked or cancelled) |

### DispatchController `/api/dispatch` — OutboundAccess
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | / | All dispatch records with nested details |
| GET | /staging-items | Staged items ready for dispatch (not yet in active dispatch) |
| POST | / | Create dispatch (select staged items + driver/vehicle) |
| POST | /{id}/confirm | Confirm dispatch → InventoryStock = Dispatched, BAST data returned |
| POST | /{id}/cancel | Cancel pending dispatch (header + details → "cancelled"; stock stays in staging) |

### DashboardController `/api/dashboard` — AnyStaff
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | /summary | KPIs: inbound, inventory, outbound, adjustments + stockByZone |
| GET | /activity | Last 15 StockMovements (activity feed) |

---

## Service Method Reference

Per-method breakdown for the two most complex services (Picking and Dispatch). See the Services (8) table above for the other six.

### PickingService — Methods
| Method | Description |
|--------|-------------|
| `GetPickingListAsync()` | Flat list of all PickingDetails |
| `CreatePickingAsync(req)` | FIFO reservation → PickingHeader + PickingDetail(s) |
| `ConfirmPickAsync(detailId, req)` | Validate scan → move stock to staging → StockMovement "Picking". Includes idempotent recovery if stock already in Outbound Staging. |
| `CancelPickAsync(detailId)` | Release reservation → StockMovement "Cancellation" → status "cancelled" |
| `ForceCompleteAsync(headerId)` | Mark header "completed" when all details are terminal (picked or cancelled) |

**DTO:** `PickingHeaderDto` — Id, PickingNumber, AssignedTo, Status, DetailCount, PickedCount, CancelledCount

### DispatchService — Methods
| Method | Description |
|--------|-------------|
| `GetDispatchListAsync()` | Dispatch history with nested DispatchDetails |
| `GetStagingItemsAsync()` | Items in Outbound Staging not yet in active dispatch |
| `CreateDispatchAsync(req)` | Validate staged items → DispatchHeader + DispatchDetail(s) |
| `ConfirmDispatchAsync(headerId, req)` | Finalize: InventoryStock → Dispatched, Qty = 0, StockMovement "Dispatch" |
| `CancelDispatchAsync(headerId)` | Cancel pending dispatch → header + details "cancelled". Stock NOT touched. |

---

## Services (8)

| Service | Responsibility |
|---------|----------------|
| AuthService | JWT token generation, BCrypt password verification |
| ReceivingService | Creates `ReceivingHeader` + `ReceivingDetail`, auto-palletizes (max 50 qty/pallet). Does NOT touch `InventoryStock`. |
| PutawayService | **Only place that creates `InventoryStock` records.** Confirms pallet → bin, inserts StockMovement (type: "Putaway"). |
| InventoryService | Aggregates stock from `InventoryStock`, joins with `MasterSKU` and `BinLocation`. |
| AdjustmentApprovalService | Approve: updates `InventoryStock` + inserts `StockMovement`. Reject: status-only, no stock change. |
| PickingService | **Outbound planning + physical execution.** Create (FIFO reserve), Confirm (scan validate + move to staging), Cancel (release reservation), ForceComplete (close terminal header). |
| DispatchService | **Finalizes outbound.** Create (select staged items), Confirm (zero stock + StockMovement "Dispatch"), Cancel (pending dispatch only — stock stays in staging). |
| DbSeeder | Idempotent seed for roles and default users. |

---

## Database Entities (15)

```
── CORE ──────────────────────────────────────────────────────
User             → Role (FK)
Role
MasterSKU        → Category, UOM (FKs)
Category
UOM
BinLocation      (Zone, Rack, BinCode, Capacity, IsActive)
                 ↳ Zone = "Outbound Staging" for staging bins (STG-A01, STG-A02, STG-B01, STG-B02)
                 ↳ Zone = "Inbound Staging" for receiving-dock bins (LOADING-01, LOADING-02) — NOT outbound staging
InventoryStock   → MasterSKU (SKUId), BinLocation (RackId)
                 ↳ PalletId, Qty, ReservedQty, AvailableQty, Status, LastMovementDate
                 ↳ Status lifecycle: "Active" → "Outbound Staging" → "Dispatched" ("Empty" is never produced by current code — see InventoryStock Status Lifecycle section below)
ReceivingHeader  (ReceivingNumber, SupplierName, DriverName, PO, Status)
                 ↳ Status: "Draft" → "Putaway" (when all pallets put away)
ReceivingDetail  → ReceivingHeader, MasterSKU, UOM (auto-generated PalletId)
InventoryAdjustment → MasterSKU (Pending/Approved/Rejected)
StockMovement    → MasterSKU (full audit trail for all movement types)
                 ↳ MovementType: "Putaway" | "Picking" | "Dispatch" | "Adjustment"
                 ↳ Fields: Qty, QtyBefore, QtyAfter, FromRackId, ToRackId, ReferenceNo,
                           MovementRemarks, MovementReferenceType, CreatedBy, CreatedAt

── OUTBOUND ──────────────────────────────────────────────────
PickingHeader    (PickingNumber: PCK-YYYY-NNN, AssignedTo, Status, CreatedAt, UpdatedAt)
                 ↳ Status: "pending" | "in-progress" | "completed" | "cancelled"
PickingDetail    → PickingHeader, MasterSKU, InventoryStock
                 ↳ RequestedQty, PickedQty
                 ↳ SuggestedRackId (FK → BinLocation), SuggestedPalletId (stored at creation time)
                 ↳ Status: "pending" | "in-progress" | "picked" | "cancelled" | "error"
DispatchHeader   (DispatchNumber: DSP-YYYY-NNN, DriverName, VehicleNumber, Notes, Status)
                 ↳ Status: "pending" | "dispatched" | "cancelled"
DispatchDetail   → DispatchHeader, PickingDetail, MasterSKU
                 ↳ Qty, StagingBinCode (snapshot at creation), PalletId (snapshot)
                 ↳ Status: "pending" | "dispatched" | "cancelled"
```

---

## Outbound Flow

```
Picking List (Planning)  — POST /api/picking
  └─ User inputs: SKUCode + RequestedQty + AssignedTo
  └─ System finds eligible InventoryStock (FIFO by CreatedAt, AvailableQty > 0, Status = "Active")
  └─ Excludes pallets locked in active PickingDetail (status pending/in-progress)
  └─ Reserves stock: ReservedQty += takeQty, AvailableQty -= takeQty
  └─ Creates PickingHeader (PCK-YYYY-NNN) + PickingDetail(s) — one per pallet (FIFO split)
  └─ Stores SuggestedRackId + SuggestedPalletId on each PickingDetail

      ↓

Picking Process (Physical Execution)  — POST /api/picking/{id}/confirm
  └─ Operator scans rack → validated vs SuggestedRack.BinCode (case-insensitive)
  └─ Operator scans pallet → validated vs SuggestedPalletId (case-insensitive)
  └─ PickedQty must be > 0 and ≤ RequestedQty and ≤ stock.Qty
  └─ Staging location resolved: explicit StagingLocationCode or auto-assign first Outbound Staging bin
  └─ InventoryStock mutated:
       Qty -= actualQty
       ReservedQty = max(0, ReservedQty - actualQty)
       AvailableQty = max(0, Qty - ReservedQty)
       RackId = stagingLocation.Id
       Status = "Outbound Staging"   ← always, regardless of remaining Qty (verified PickingService.cs:277)
  └─ StockMovement inserted: MovementType="Picking", From=sourceRack, To=stagingLocation
  └─ PickingDetail.Status: "picked" (full) | "in-progress" (partial)
  └─ PickingHeader.Status: "completed" (all details picked) | "in-progress"

      ↓

Outbound Staging
  └─ InventoryStock.Status = "Outbound Staging", RackId = stagingBin.Id
  └─ Eligible for dispatch via GET /api/dispatch/staging-items

      ↓

Dispatch (Planning)  — POST /api/dispatch
  └─ User selects staged PickingDetailIds + DriverName + VehicleNumber
  └─ Validates: detail.Status="picked", InventoryStock.Status="Outbound Staging", not double-assigned
  └─ Creates DispatchHeader (DSP-YYYY-NNN) + DispatchDetail(s) with StagingBinCode/PalletId snapshots
  └─ Stock status remains "Outbound Staging" at this stage

      ↓

Dispatch Confirm  — POST /api/dispatch/{id}/confirm
  └─ InventoryStock finalized: Qty=0, AvailableQty=0, ReservedQty=0, Status="Dispatched"
  └─ StockMovement inserted: MovementType="Dispatch", From=stagingRack, To=null
  └─ DispatchHeader.Status = "dispatched"
  └─ BAST data returned to frontend for document rendering
  └─ Inventory officially leaves the warehouse
```

---

## Authorization Policies (Program.cs)

| Policy | Allowed Roles | Used By |
|--------|--------------|---------|
| `ManagerOnly` | SuperAdmin, WarehouseManager | Adjustment approve/reject, MasterSKU write, BinLocation write |
| `InboundAccess` | SuperAdmin, WarehouseManager, InboundStaff | Receiving, Putaway |
| `OutboundAccess` | SuperAdmin, WarehouseManager, OutboundStaff | Picking, Dispatch |
| `InventoryAccess` | SuperAdmin, WarehouseManager, InventoryStaff | Adjustment submit/view (approve/reject is `ManagerOnly`) |
| `AnyStaff` | All roles | BinLocation read, MasterSKU read, Dashboard, **Inventory (view)** |

> **Verified 2026-07-15:** `InventoryController` uses `AnyStaff` (`InventoryController.cs:13`), not `InventoryAccess` as earlier drafts of this table claimed — any authenticated staff role (including InboundStaff/OutboundStaff) can read stock data, not just InventoryStaff/Manager/SuperAdmin.

---

## Critical Business Rules

### Inbound (unchanged)
1. **`InventoryStock` is created ONLY by `PutawayService.ConfirmPutawayAsync()`** — Receiving creates pallets in draft; putaway activates stock.
2. **Max 50 qty per pallet** — Enforced during both receiving palletization and putaway confirmation.
3. **Stock mutations always produce a `StockMovement` record** — Never update stock without this.
4. **Adjustment approval is the only path to adjust existing stock** — Direct stock edits via endpoints are not permitted.
5. **JWT expiry is 8 hours** — Configured in `appsettings.json → Jwt:ExpiryHours`.
6. **Passwords hashed with BCrypt** — Always use `BCrypt.Net.BCrypt.Verify()` / `HashPassword()`.
7. **One pallet per bin** — A bin with `Status = "Active"` stock cannot accept another pallet during putaway.

### Outbound — Picking List (Planning)
8. **Picking List is a planning module only** — Does NOT perform rack scanning, pallet scanning, or physical picking.
9. **System auto-suggests rack and pallet via FIFO** — Orders `InventoryStock` by `CreatedAt ASC`, picks pallets with `AvailableQty > 0`, `Status = "Active"`, not locked in an active PickingDetail.
10. **Cross-pallet split is automatic** — If RequestedQty exceeds one pallet, system creates multiple PickingDetails (FIFO), one per pallet.
11. **Creating a picking task reserves stock immediately** — `ReservedQty += takeQty`, `AvailableQty -= takeQty`.
12. **Picking number format:** `PCK-YYYY-NNN` — zero-padded 3-digit sequence per calendar year.

### Outbound — Picking Process (Physical Execution)
13. **Four mandatory validations before confirming:** scanned rack matches suggested rack, scanned pallet matches suggested pallet, PickedQty > 0, PickedQty ≤ RequestedQty and ≤ stock.Qty.
14. **Staging location auto-assigned** if not specified: first `BinLocation` with `Zone = "Outbound Staging"` ordered by `BinCode`. If no active bin with that Zone exists at all, `ConfirmPickAsync` throws `InvalidOperationException` ("No staging location available...").
15. **Partial pick keeps status `in-progress`** — Only `PickedQty >= RequestedQty` sets status to `"picked"`.

### Outbound — Staging
16. **Staging locations are `BinLocation` records with `Zone = "Outbound Staging"`** — Examples: `STG-A01`, `STG-A02`, `STG-B01`, `STG-B02`. Do NOT create a separate staging table. **Note:** `LOADING-01`/`LOADING-02` are seeded with `Zone = "Inbound Staging"` (receiving dock), not `"Outbound Staging"` — do not use them as outbound staging examples (verified `DbSeeder.cs` — earlier drafts of this doc incorrectly listed `LOADING-01` here).
17. **`GET /api/dispatch/staging-items`** returns only PickingDetails with `Status = "picked"`, InventoryStock `Status = "Outbound Staging"`, not yet assigned to a non-cancelled dispatch.

### Outbound — Dispatch
18. **Dispatch number format:** `DSP-YYYY-NNN` — zero-padded 3-digit sequence per calendar year.
19. **Dispatch creation validates:** each PickingDetail must have `Status = "picked"` and its InventoryStock must have `Status = "Outbound Staging"`. Items already assigned to another non-cancelled dispatch are rejected.
20. **Dispatch confirmation is the only operation that finalizes outbound** — Sets `InventoryStock.Status = "Dispatched"`, zeroes all qty fields, inserts StockMovement `MovementType = "Dispatch"`.
21. **BAST (Berita Acara Serah Terima)** — dispatch confirmation returns the full dispatch record; the frontend renders the handover document. Not stored as a separate DB entity.

### Outbound — Cancellation
23. **Picking task cancellation** releases the reservation: `ReservedQty = max(0, ReservedQty - releaseQty)`, `AvailableQty += releaseQty`, where `releaseQty = RequestedQty - PickedQty` (only the unpicked portion — a partially-picked detail releases just the remainder). Inserts StockMovement `MovementType = "Cancellation"`. Blocked if status is already `"picked"` or `"cancelled"`.
24. **Dispatch cancellation** is only allowed while `DispatchHeader.Status = "pending"`. Sets header + all details to `"cancelled"`. InventoryStock is NOT touched — goods remain in Outbound Staging and can be re-assigned to a new dispatch.
25. **Force-complete** (`POST /api/picking/{headerId}/force-complete`) closes a PickingHeader whose details are all terminal (`"picked"` or `"cancelled"`). Throws if any detail is still `"pending"` or `"in-progress"`.

### Idempotent Recovery
26. **ConfirmPick idempotent recovery:** if `InventoryStock.Status` is already `"Outbound Staging"` when a re-confirm is attempted (e.g. network timeout caused prior call to partially succeed), the service auto-recovers by updating the PickingDetail status without re-moving stock.

### Master Data
27. **MasterSKU deactivate** (`PATCH /api/mastersku/{id}/deactivate`) sets `Status = "Inactive"`. Does not delete the record. Only `ManagerOnly` roles can deactivate.
28. **BinLocation toggle-active** (`PATCH /api/binlocation/{id}/toggle-active`) blocks deactivation of any bin currently occupied by `Active` or `Outbound Staging` stock.

### Traceability
29. **Every outbound StockMovement records:** SKUId, MovementType, Qty, QtyBefore, QtyAfter, ReferenceNo (picking or dispatch number), FromRackId, ToRackId (null for Dispatch/Cancellation), MovementRemarks (human-readable), CreatedAt.

---

## StockMovement Reference

| MovementType | FromRackId | ToRackId | Triggered By |
|-------------|-----------|---------|-------------|
| Putaway | null | binLocation.Id | PutawayService.ConfirmPutawayAsync |
| Picking | sourceRack.Id | stagingBin.Id | PickingService.ConfirmPickAsync |
| Dispatch | stagingBin.Id | null | DispatchService.ConfirmDispatchAsync |
| Adjustment | stock.RackId* | stock.RackId* | AdjustmentApprovalService.ApproveAdjustmentAsync |
| Cancellation | stock.RackId | null | PickingService.CancelPickAsync (releases reservation) |

\* **Adjustment nuance (verified `AdjustmentApprovalService.cs:76-100`):** if a matching `InventoryStock` with `Status = "Active"` exists for the SKU (matched by PalletId if the adjustment specifies one, otherwise the oldest Active pallet), `FromRackId` and `ToRackId` are both set to that stock's `RackId` — **not null**. `FromRackId`/`ToRackId` are only `null` in the fallback path, when no Active `InventoryStock` exists at all for the SKU and the adjustment instead mutates `MasterSKU.Qty` directly.

---

## InventoryStock Status Lifecycle

```
(created by Putaway)
     ↓
  "Active"            ← normal rack storage; AvailableQty can be reserved by picking
     ↓  ConfirmPickAsync
"Outbound Staging"    ← in staging area; awaiting dispatch
     ↓  ConfirmDispatchAsync
  "Dispatched"        ← Qty=0; officially out of warehouse

Note: an "Empty" status is not produced anywhere in the current codebase — grep across all
.cs files confirms it. Confirm-pick always sets "Outbound Staging" (even when the pallet is
fully drained) and dispatch-confirm always sets "Dispatched". Treat "Empty" as a legacy/aspirational
value that never shipped, not a live status.

Cancellation path (picking cancelled before physical pick):
  "Active" ← ReservedQty released back; stock never left the rack
```

---

## Seeded Roles & Users

| Role | Username | Password |
|------|----------|----------|
| SuperAdmin | admin | Admin@123 |
| WarehouseManager | manager | Manager@123 |
| InventoryStaff | staff | Staff@123 |
| InboundStaff | — | — |
| OutboundStaff | — | — |

---

## Configuration

**`appsettings.json`** — do not commit secrets to source control in production:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost\\SQLEXPRESS;Database=SynteraWMS_Enterprise;Trusted_Connection=True;TrustServerCertificate=True;"
  },
  "Jwt": {
    "Key": "SynteraWMS_SuperSecretKey_2026_Artatera!@#$%^&*()",
    "Issuer": "SynteraWMS",
    "Audience": "SynteraWMS",
    "ExpiryHours": 8
  }
}
```

---

## Migrations

```bash
# Add new migration
dotnet ef migrations add <MigrationName>

# Apply to database
dotnet ef database update
```

Always run migrations before starting the server after schema changes.

---

## Development Commands

```bash
# Run API (port 5000)
dotnet run

# Build
dotnet build

# Update database
dotnet ef database update
```

---

## Coding Conventions

### C# Style
- Use `async/await` throughout — all service methods return `Task<T>`.
- Services are registered as **Scoped** — never store state between requests.
- Use primary constructor syntax: `public class SomeService(ApplicationDbContext context)`.
- Use `var` for local variables when type is obvious from the right-hand side.
- Controller actions return `IActionResult`.
- **DTOs are defined inline at the bottom of the service file** that owns them.
- Controller response shape: `Ok(new { success = true, data = result })` or `BadRequest(new { success = false, message = "..." })`.

### Service Pattern
```csharp
public class SomeService(ApplicationDbContext context)
{
    private readonly ApplicationDbContext _context = context;

    public async Task<SomeDto> DoWorkAsync(SomeRequest req)
    {
        await using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            // ... mutations ...
            _context.StockMovements.Add(new StockMovement { ... }); // always required
            await _context.SaveChangesAsync();
            await tx.CommitAsync();
            return result;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }
}
```

### Adding a New Feature
1. Add entity to `Models/` if needed.
2. Register `DbSet<T>` in `ApplicationDbContext`.
3. Add migration: `dotnet ef migrations add FeatureName`.
4. Create service in `Services/` with inline request/response DTOs at the bottom.
5. Register service as `Scoped` in `Program.cs`.
6. Add controller in `Controllers/`.

### Error Handling
- Throw `ArgumentException` for input validation failures → controller returns 400.
- Throw `InvalidOperationException` for business rule violations → controller returns 400.
- Return `NotFound(new { success = false, message = "..." })` for missing resources.
- Wrap controller action bodies in try/catch; return `StatusCode(500, ...)` for unhandled exceptions.
- Never swallow exceptions in services — always rethrow after rollback.

---

## Security Notes
- JWT secret key must be at least 32 characters and kept out of source control in production.
- CORS is currently open — restrict to the frontend origin in production.
- All endpoints requiring authentication must be decorated with `[Authorize]`.
- Never return raw `User` entity (contains PasswordHash) from API responses.
