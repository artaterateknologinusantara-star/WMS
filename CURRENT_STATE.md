# CURRENT_STATE.md — SynteraWMS Project Snapshot
> Last updated: 2026-06-05

This document captures the **actual implemented state** of the SynteraWMS project as of this date. Use it to understand what is live vs. what is still planned.

---

## Implementation Status Overview

| Module | Backend | Frontend | Status |
|--------|---------|----------|--------|
| Authentication (login/logout) | ✅ Live | ✅ Live | Complete |
| Inbound Receiving | ✅ Live | ✅ Live | Complete |
| Putaway | ✅ Live | ✅ Live | Complete |
| Inventory Stock on Hand | ✅ Live | ✅ Live | Complete |
| Inventory Adjustment (request) | ✅ Live | ✅ Live | Complete |
| Adjustment Approval | ✅ Live | ✅ Live | Complete |
| Master SKU (full CRUD) | ✅ Live | — | Backend complete |
| Bin Location (CRUD + toggle-active) | ✅ Live | — | Backend complete |
| Picking List (planning) | ✅ Live | ✅ Live | Complete |
| Picking Process (physical) | ✅ Live | ✅ Live | Complete |
| Picking Cancel | ✅ Live | — | Backend complete |
| Picking Force-Complete | ✅ Live | — | Backend complete |
| Outbound Staging | ✅ Live | ✅ Live | Complete (via BinLocation Zone) |
| Dispatch (planning + confirm) | ✅ Live | ✅ Live | Complete |
| Dispatch Cancel | ✅ Live | — | Backend complete |
| BAST Document | ✅ Live (data) | ✅ Live (render+print) | Complete |
| Dashboard / KPIs | ✅ Live | ✅ Live | Complete |
| Role-Based Authorization | ✅ Live | — | 5 policies defined |

---

## Authorization Policies (Program.cs)

| Policy | Roles | Applied To |
|--------|-------|-----------|
| `ManagerOnly` | SuperAdmin, WarehouseManager | Adjustment approve/reject, SKU/Bin write ops |
| `InboundAccess` | SuperAdmin, WarehouseManager, InboundStaff | Receiving, Putaway |
| `OutboundAccess` | SuperAdmin, WarehouseManager, OutboundStaff | Picking, Dispatch |
| `InventoryAccess` | SuperAdmin, WarehouseManager, InventoryStaff | Inventory, Adjustment submit |
| `AnyStaff` | All authenticated roles | BinLocation read, MasterSKU read, Dashboard |

---

## Backend — Live Entities (15)

```
User, Role
MasterSKU (Status: "Active" | "Inactive"), Category, UOM
BinLocation (IsActive, CapacityQty, WarehouseId)
InventoryStock
ReceivingHeader, ReceivingDetail
InventoryAdjustment
StockMovement
PickingHeader, PickingDetail
DispatchHeader, DispatchDetail
```

---

## Backend — Live Endpoints (All Controllers)

### AuthController `/api/auth` — Public
| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | /login | Validate credentials, return JWT |

### InventoryController `/api/inventory` — InventoryAccess
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | / | All InventoryStock records |
| GET | /by-code/{skuCode} | Aggregated stock for one SKU |
| GET | /by-code/{skuCode}/pallets | Individual pallet records per SKU |
| GET | /uom | List of UOM |

### InventoryAdjustmentController `/api/adjustment` — InventoryAccess / ManagerOnly
| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| GET | / | InventoryAccess | All adjustments |
| POST | / | InventoryAccess | Submit new adjustment request |
| POST | /{id}/approve | ManagerOnly | Approve + apply stock change |
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

## Backend — Live Services

### PickingService — Methods
| Method | Description |
|--------|-------------|
| `GetPickingListAsync()` | Flat list of all PickingDetails |
| `CreatePickingAsync(req)` | FIFO reservation → PickingHeader + PickingDetail(s) |
| `ConfirmPickAsync(detailId, req)` | Validate scan → move stock to staging → StockMovement "Picking". Includes idempotent recovery if stock already in Outbound Staging. |
| `CancelPickAsync(detailId)` | Release reservation → StockMovement "Cancellation" → status "cancelled" |
| `ForceCompleteAsync(headerId)` | Mark header "completed" when all details are terminal (picked or cancelled) |

**New DTO:** `PickingHeaderDto` — Id, PickingNumber, AssignedTo, Status, DetailCount, PickedCount, CancelledCount

### DispatchService — Methods
| Method | Description |
|--------|-------------|
| `GetDispatchListAsync()` | Dispatch history with nested DispatchDetails |
| `GetStagingItemsAsync()` | Items in Outbound Staging not yet in active dispatch |
| `CreateDispatchAsync(req)` | Validate staged items → DispatchHeader + DispatchDetail(s) |
| `ConfirmDispatchAsync(headerId, req)` | Finalize: InventoryStock → Dispatched, Qty = 0, StockMovement "Dispatch" |
| `CancelDispatchAsync(headerId)` | Cancel pending dispatch → header + details "cancelled". Stock NOT touched. |

---

## Status Values — Current

### PickingDetail
`"pending"` → `"in-progress"` → `"picked"` (full) | `"in-progress"` (partial) | `"cancelled"` | `"error"`

### PickingHeader
`"pending"` → `"in-progress"` → `"completed"` | `"cancelled"`

### DispatchHeader
`"pending"` → `"dispatched"` | `"cancelled"`

### DispatchDetail
`"pending"` → `"dispatched"` | `"cancelled"`

### InventoryStock
`"Active"` → `"Outbound Staging"` → `"Dispatched"` | `"Empty"`
Cancellation path: `"Active"` ← reservation released, stock never leaves rack

---

## StockMovement Types — All Live

| MovementType | From | To | Triggered By |
|-------------|------|-----|-------------|
| Putaway | null | bin.Id | PutawayService.ConfirmPutawayAsync |
| Picking | sourceRack.Id | stagingBin.Id | PickingService.ConfirmPickAsync |
| Cancellation | stock.RackId | null | PickingService.CancelPickAsync |
| Dispatch | stagingBin.Id | null | DispatchService.ConfirmDispatchAsync |
| Adjustment | null | null | AdjustmentApprovalService.ApproveAsync |

---

## Frontend — Live Pages & Components

| Page | Component | Features |
|------|-----------|---------|
| `/login` | login/page.tsx | Sign-in form |
| `/dashboard` | DashboardContent.tsx | KPI bento, zone chart, activity feed (last 15 movements) |
| `/` (home) | InboundReceivingContent.tsx | Inbound receiving form |
| `/putaway` | PutawayContent.tsx | Task list + scan-confirm modal |
| `/inventory/stock-on-hand` | StockOnHandContent.tsx | SOH table |
| `/inventory/adjustment` | InventoryAdjustmentContent.tsx | Submit adjustment request |
| `/inventory/adjustment-approval` | adjustment-approval/page.tsx | Approve / reject |
| `/outbound/picking` | PickingListContent.tsx | Table, status filter, Create Picking modal (live SKU check, FIFO hint) |
| `/outbound/packing` | PackingContent + PickingProcessModal | Scan rack, scan pallet, confirm qty, staging location picker |
| `/outbound/dispatch` | DispatchContent.tsx | History table, New Dispatch slide-over, Confirm modal, BAST modal + print |

---

## Frontend — Live Services (8)

| File | Key Functions |
|------|--------------|
| auth.service.ts | login, logout |
| receiving.service.ts | submitReceiving, getReceivingHistory |
| putaway.service.ts | getPendingTasks, confirmPutaway, getStockByPallet |
| inventory.service.ts | getAll, getByCode, getStocksByCode, getUOM |
| adjustment.service.ts | getAdjustments, submitAdjustment, approve, reject |
| picking.service.ts | checkStock, getStagingLocations, getPickingList, createPicking, confirmPick |
| dispatch.service.ts | getStagingItems, getDispatchList, createDispatch, confirmDispatch |
| dashboard.service.ts | getDashboardSummary, getDashboardActivity |

> Note: `picking.service.ts` and `dispatch.service.ts` do not yet have `cancel` functions. Backend cancel endpoints exist but frontend UI for cancel is not yet implemented.

---

## Key Business Logic (Implemented)

### FIFO Picking Selection
```
WHERE SKUId = X AND Status = "Active" AND AvailableQty > 0
AND NOT locked in (pending/in-progress) PickingDetail
ORDER BY CreatedAt ASC
```

### Stock Mutation on Pick Confirm
```
Qty          -= actualQty
ReservedQty   = max(0, ReservedQty - actualQty)
AvailableQty  = max(0, Qty - ReservedQty)
RackId        = stagingLocation.Id
Status        = "Outbound Staging"   ← always (not "Empty" anymore)
```

### Stock Release on Pick Cancel
```
ReservedQty   = max(0, ReservedQty - releaseQty)
AvailableQty += releaseQty
```
releaseQty = RequestedQty - PickedQty (unpicked portion only)

### Stock Finalization on Dispatch Confirm
```
Qty = 0, AvailableQty = 0, ReservedQty = 0, Status = "Dispatched"
```

---

## Number Format Conventions

| Document | Format | Example |
|----------|--------|---------|
| Picking Number | PCK-YYYY-NNN (per-year, zero-padded 3-digit) | PCK-2026-001 |
| Dispatch Number | DSP-YYYY-NNN (per-year, zero-padded 3-digit) | DSP-2026-001 |
| Pallet ID | auto-generated during receiving | — |

---

## Outbound Staging Configuration

Staging locations must exist as `BinLocation` records with `Zone = "Outbound Staging"` and `IsActive = true`. Without them, `ConfirmPickAsync` throws `InvalidOperationException`.

Example bins to seed:

| BinCode | Zone |
|---------|------|
| STG-A01 | Outbound Staging |
| STG-B02 | Outbound Staging |
| LOADING-01 | Outbound Staging |

---

## Known Gaps / Planned

| Item | Status | Notes |
|------|--------|-------|
| Frontend cancel picking task | Not implemented | Backend `POST /api/picking/{id}/cancel` exists |
| Frontend cancel dispatch | Not implemented | Backend `POST /api/dispatch/{id}/cancel` exists |
| Frontend master SKU management | Not implemented | Backend CRUD exists |
| Frontend bin location management | Not implemented | Backend CRUD exists |
| Role-based UI access control | Not implemented | Backend policies enforced; frontend shows all menus |
| BAST PDF generation (server-side) | Not implemented | Browser `window.print()` only |

---

## Migration History

| Migration | Date | Description |
|-----------|------|-------------|
| 20260518145051_InitialCreate | 2026-05-18 | Full initial schema (15 entities) |

---

## Port Reference

| Service | Port |
|---------|------|
| Backend API | 5000 |
| Frontend Dev | 4028 |
| SQL Server | localhost\SQLEXPRESS |
