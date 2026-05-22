# CLAUDE.md — SynteraWMS Backend (ASP.NET Core 8.0)

## Project Overview

Warehouse Management System backend API built with ASP.NET Core 8.0 and Entity Framework Core. Covers inbound receiving, putaway, inventory management, and adjustment approval workflows.

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
Services/        → Business logic, transactions, audit trail
Models/          → EF entity classes (11 entities)
Data/            → ApplicationDbContext, DbSeeder
DTOs/            → Response shaping (e.g., InventoryLookupDto)
Migrations/      → EF migrations (single: 20260518145051_InitialCreate)
```

### Patterns in Use
- **Service Layer:** All business logic lives in Services, controllers only route and validate HTTP.
- **DTO Pattern:** Shape API responses with dedicated DTO classes — never return raw EF entities directly.
- **Transaction Management:** Use `await using var tx = await _context.Database.BeginTransactionAsync()` for multi-step operations (Putaway, Adjustment approval).
- **Audit Trail:** Every stock mutation inserts a `StockMovement` record.
- **Approval Workflow:** Adjustments flow: `Pending → Approved | Rejected`. Only approved adjustments update stock.
- **DbSeeder:** Runs on startup via `SeedData()` — idempotent, checks before inserting.

---

## Controllers (9)

| Controller | Base Route | Key Operations |
|-----------|-----------|----------------|
| AuthController | `/api/auth` | POST /login |
| InventoryController | `/api/inventory` | GET list, GET /by-code/{skuCode}, GET /uom |
| InventoryAdjustmentController | `/api/adjustment` | GET, POST, POST /{id}/approve, POST /{id}/reject |
| PutawayController | `/api/putaway` | POST /confirm, GET /pending, GET /stock/{palletId} |
| ReceivingController | `/api/receiving` | GET history, POST submit |
| BinLocationController | `/api/binlocation` | CRUD |
| MasterSKUController | `/api/mastersku` | CRUD |
| PickingController | `/api/picking` | GET list, POST create, POST /{id}/confirm |
| DispatchController | `/api/dispatch` | GET list, POST create, POST /{id}/confirm *(planned)* |

---

## Services (8)

| Service | Responsibility |
|---------|----------------|
| AuthService | JWT token generation, BCrypt password verification |
| ReceivingService | Creates `ReceivingHeader` + `ReceivingDetail`, auto-palletizes (max 50 qty/pallet). Does NOT touch `InventoryStock`. |
| PutawayService | **Only place that creates `InventoryStock` records.** Confirms pallet → bin, inserts StockMovement. |
| InventoryService | Aggregates stock from `InventoryStock`, joins with `MasterSKU` and `BinLocation`. |
| AdjustmentApprovalService | Approve: updates `InventoryStock` + inserts `StockMovement`. Reject: status-only, no stock change. |
| PickingService | **Outbound planning.** Creates `PickingHeader` + `PickingDetail`, validates available qty, reserves stock, suggests rack/pallet via FIFO. Also executes physical picking: moves inventory to Outbound Staging, creates StockMovement. |
| DispatchService | *(planned)* Finalizes outbound. Scans staging pallets, assigns driver/vehicle, generates serah terima document, creates StockMovement (Staging → Outbound). |
| DbSeeder | Idempotent seed for roles and default users. |

---

## Database Entities (13 active + 2 planned)

```
── CORE ──────────────────────────────────────────────────
User             → Role (FK)
MasterSKU        → Category, UOM (FKs)
BinLocation      (Zone, Rack, Level, Capacity)
                 ↳ Also used for Outbound Staging locations (e.g. STG-A01, LOADING-01)
InventoryStock   → MasterSKU, BinLocation (tracks pallet, ReservedQty, AvailableQty, Status)
                 ↳ Status lifecycle: Active → Outbound Staging → Dispatched / Empty
ReceivingHeader  (supplier, driver, PO, status)
ReceivingDetail  → ReceivingHeader, MasterSKU, UOM (auto-generated PalletId)
InventoryAdjustment → MasterSKU (Pending/Approved/Rejected)
StockMovement    → MasterSKU (audit trail: Putaway | Picking | Dispatch | Adjustment)
Category
UOM
Role

── OUTBOUND ──────────────────────────────────────────────
PickingHeader    (PickingNumber: PCK-YYYY-NNN, AssignedTo, Status)
                 ↳ Status: pending | in-progress | completed
PickingDetail    → PickingHeader, MasterSKU, InventoryStock
                 ↳ RequestedQty, PickedQty, SuggestedRackId, SuggestedPalletId
                 ↳ Status: pending | in-progress | picked | error

── PLANNED ───────────────────────────────────────────────
DispatchHeader   (DispatchNumber, DriverName, VehicleNumber, Status) *(planned)*
DispatchDetail   → DispatchHeader, PickingDetail, InventoryStock *(planned)*
```

---

## Critical Business Rules

### Inbound (unchanged)
1. **`InventoryStock` is created ONLY by `PutawayService.ConfirmPutawayAsync()`** — Receiving creates pallets in draft, putaway activates stock.
2. **Max 50 qty per pallet** — Enforced during both receiving palletization and putaway confirmation.
3. **Stock mutations always produce a `StockMovement` record** — Never update stock without this.
4. **Adjustment approval is the only path to adjust existing stock** — Direct stock edits via endpoints are not permitted.
5. **JWT expiry is 8 hours** — Configured in `appsettings.json → Jwt:ExpiryHours`.
6. **Passwords hashed with BCrypt** — Always use `BCrypt.Net.BCrypt.Verify()` / `HashPassword()`.

### Outbound — Picking List (Planning)
7. **Picking List is a planning module only** — It does NOT perform physical picking, rack scanning, or pallet scanning. It creates the picking task and suggests rack/pallet.
8. **System auto-suggests rack and pallet** — `PickingService.CreatePickingAsync()` searches `InventoryStock` for available stock, applies FIFO where possible, and sets `SuggestedRackId` + `SuggestedPalletId` on `PickingDetail`.
9. **Creating a picking task reserves stock** — `InventoryStock.ReservedQty += RequestedQty`, `AvailableQty -= RequestedQty`. Reservation is released if picking is cancelled.
10. **Picking task must target a specific pallet with sufficient `AvailableQty`** — Partial-pallet picking is allowed; cross-pallet splitting within one task is not.

### Outbound — Picking Process (Physical Execution)
11. **Picking Process is the physical warehouse execution step** — The operator goes to the rack, scans the rack barcode, scans the pallet barcode, and confirms qty picked.
12. **All four validations are mandatory before confirming:**
    - Scanned rack must match `SuggestedRackId`
    - Scanned pallet must match `SuggestedPalletId`
    - Pallet must contain the correct SKU
    - `PickedQty` must be > 0 and ≤ `RequestedQty`
13. **Confirming a pick moves inventory to Outbound Staging:**
    - `InventoryStock.RackId` → updated to staging location (e.g. STG-A01)
    - `InventoryStock.Status` → `"Outbound Staging"`
    - `InventoryStock.Qty` and `ReservedQty` decremented by actual picked qty
    - `StockMovement` inserted: `MovementType = "Picking"`, `FromRackId = sourceRack`, `ToRackId = stagingLocation`
14. **Partial pick keeps status `in-progress`** — Full pick (`PickedQty == RequestedQty`) sets status to `picked`.

### Outbound — Staging
15. **Staging locations are `BinLocation` records with Zone = "Staging"** — Examples: `STG-A01`, `STG-B02`, `LOADING-01`. Do NOT create a separate staging table.
16. **Inventory in staging has `Status = "Outbound Staging"`** and `InventoryStock.RackId` points to the staging `BinLocation`.

### Outbound — Dispatch *(planned)*
17. **Dispatch is the only operation that finalizes outbound** — It scans staging pallets, assigns driver/vehicle, and creates a serah terima document.
18. **Dispatch creates `StockMovement`:** `MovementType = "Dispatch"`, `FromRackId = stagingLocation`, `ToRackId = null`. `InventoryStock.Status` → `"Dispatched"`.
19. **Inventory is officially out of the warehouse only after Dispatch confirmation.**

### Traceability (all outbound operations)
20. **Every outbound action must record:** SKU, RackId, PalletId, Qty, PickingNumber, StagingLocation, Timestamp, and (for dispatch) DriverName, VehicleNumber, DispatchDocument.

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

Single migration: `20260518145051_InitialCreate`.

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
- Use `var` for local variables when type is obvious from the right-hand side.
- Controller actions return `IActionResult` or `ActionResult<T>`.
- DTOs go in `DTOs/` folder; name them `{Entity}Dto` or `{Operation}Dto`.

### Service Pattern
```csharp
// Services receive ApplicationDbContext via constructor injection
public class SomeService
{
    private readonly ApplicationDbContext _context;
    public SomeService(ApplicationDbContext context) => _context = context;

    public async Task<SomeDto> DoWorkAsync(RequestModel req)
    {
        await using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            // ... mutations ...
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
4. Create service in `Services/`.
5. Register service as `Scoped` in `Program.cs`.
6. Add controller in `Controllers/`.

### Error Handling
- Return `BadRequest(new { message = "..." })` for client errors.
- Return `NotFound(new { message = "..." })` for missing resources.
- Return `Ok(result)` for success.
- Let unhandled exceptions bubble to the default exception middleware (do not swallow).

---

## Security Notes
- JWT secret key must be at least 32 characters and kept out of source control for production.
- CORS is currently `AllowAnyOrigin` — restrict to specific frontend origin in production.
- All endpoints requiring authentication must be decorated with `[Authorize]`.
- Never return raw `User` entity (contains PasswordHash) from API responses.
