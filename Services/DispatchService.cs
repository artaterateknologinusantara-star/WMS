using Microsoft.EntityFrameworkCore;
using Syntera.WMS.API.Data;
using Syntera.WMS.API.Models;

namespace Syntera.WMS.API.Services
{
    public class DispatchService(ApplicationDbContext context)
    {
        private readonly ApplicationDbContext _context = context;

        // ──────────────────────────────────────────────────────────────
        // GET — list of all dispatch headers + their details
        // ──────────────────────────────────────────────────────────────
        public async Task<List<DispatchListDto>> GetDispatchListAsync()
        {
            var headers = await _context.DispatchHeaders
                .Include(h => h.Details)
                    .ThenInclude(d => d.SKU)
                .OrderByDescending(h => h.CreatedAt)
                .ToListAsync();

            return headers.Select(h => new DispatchListDto
            {
                Id             = h.Id,
                DispatchNumber = h.DispatchNumber,
                DriverName     = h.DriverName,
                VehicleNumber  = h.VehicleNumber,
                Status         = h.Status,
                Notes          = h.Notes ?? string.Empty,
                CreatedAt      = h.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                Items          = h.Details.Select(d => new DispatchItemDto
                {
                    Id             = d.Id,
                    SKUCode        = d.SKU?.SKUCode ?? string.Empty,
                    SKUName        = d.SKU?.SKUName ?? string.Empty,
                    Qty            = d.Qty,
                    StagingBinCode = d.StagingBinCode,
                    PalletId       = d.PalletId,
                    Status         = d.Status,
                }).ToList()
            }).ToList();
        }

        // ──────────────────────────────────────────────────────────────
        // GET — items ready for dispatch (picked + in Outbound Staging,
        //       not yet included in any pending/dispatched dispatch)
        // ──────────────────────────────────────────────────────────────
        public async Task<List<StagingItemDto>> GetStagingItemsAsync()
        {
            // IDs already assigned to an active dispatch
            var assignedPickingDetailIds = await _context.DispatchDetails
                .Where(d => d.Status != "cancelled")
                .Select(d => d.PickingDetailId)
                .ToListAsync();

            var items = await _context.PickingDetails
                .Include(d => d.Header)
                .Include(d => d.SKU)
                .Include(d => d.InventoryStock)
                    .ThenInclude(s => s!.Rack)
                .Where(d =>
                    d.Status == "picked" &&
                    d.InventoryStock != null &&
                    d.InventoryStock.Status == "Outbound Staging" &&
                    !assignedPickingDetailIds.Contains(d.Id))
                .OrderBy(d => d.CreatedAt)
                .ToListAsync();

            return items.Select(d => new StagingItemDto
            {
                PickingDetailId = d.Id,
                PickingNumber   = d.Header?.PickingNumber ?? string.Empty,
                SKUCode         = d.SKU?.SKUCode ?? string.Empty,
                SKUName         = d.SKU?.SKUName ?? string.Empty,
                Qty             = d.PickedQty,
                StagingBinCode  = d.InventoryStock?.Rack?.BinCode ?? string.Empty,
                PalletId        = d.InventoryStock?.PalletId ?? string.Empty,
            }).ToList();
        }

        // ──────────────────────────────────────────────────────────────
        // CREATE — Dispatch (planning)
        // User selects staged items, enters driver/vehicle.
        // Stock is NOT yet finalized — status stays "Outbound Staging".
        // ──────────────────────────────────────────────────────────────
        public async Task<DispatchListDto> CreateDispatchAsync(CreateDispatchRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.DriverName))
                throw new ArgumentException("Driver name is required.");
            if (string.IsNullOrWhiteSpace(request.VehicleNumber))
                throw new ArgumentException("Vehicle number is required.");
            if (request.PickingDetailIds == null || request.PickingDetailIds.Count == 0)
                throw new ArgumentException("At least one item must be selected for dispatch.");

            // Validate all picking details exist, are picked, and in staging
            var details = await _context.PickingDetails
                .Include(d => d.Header)
                .Include(d => d.SKU)
                .Include(d => d.InventoryStock)
                    .ThenInclude(s => s!.Rack)
                .Where(d => request.PickingDetailIds.Contains(d.Id))
                .ToListAsync();

            if (details.Count != request.PickingDetailIds.Count)
                throw new InvalidOperationException("One or more selected items not found.");

            foreach (var d in details)
            {
                if (d.Status != "picked")
                    throw new InvalidOperationException($"Item {d.Id} (SKU {d.SKU?.SKUCode}) is not in 'picked' status.");
                if (d.InventoryStock?.Status != "Outbound Staging")
                    throw new InvalidOperationException($"Item {d.Id} (SKU {d.SKU?.SKUCode}) is not in Outbound Staging.");
            }

            // Check none are already assigned
            var alreadyAssigned = await _context.DispatchDetails
                .Where(dd => request.PickingDetailIds.Contains(dd.PickingDetailId) && dd.Status != "cancelled")
                .Select(dd => dd.PickingDetailId)
                .ToListAsync();
            if (alreadyAssigned.Count > 0)
                throw new InvalidOperationException($"Item(s) already assigned to another dispatch: {string.Join(", ", alreadyAssigned)}.");

            // Auto-generate dispatch number DSP-YYYY-NNN
            var year  = DateTime.UtcNow.Year;
            var count = await _context.DispatchHeaders.CountAsync(h => h.CreatedAt.Year == year);
            var dispatchNumber = $"DSP-{year}-{(count + 1):D3}";

            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                var header = new DispatchHeader
                {
                    DispatchNumber = dispatchNumber,
                    DriverName     = request.DriverName.Trim(),
                    VehicleNumber  = request.VehicleNumber.Trim(),
                    Notes          = request.Notes?.Trim(),
                    Status         = "pending",
                    CreatedAt      = DateTime.UtcNow,
                };
                _context.DispatchHeaders.Add(header);
                await _context.SaveChangesAsync();

                var dispatchDetails = details.Select(d => new DispatchDetail
                {
                    DispatchHeaderId = header.Id,
                    PickingDetailId  = d.Id,
                    SKUId            = d.SKUId,
                    Qty              = d.PickedQty,
                    StagingBinCode   = d.InventoryStock?.Rack?.BinCode ?? string.Empty,
                    PalletId         = d.InventoryStock?.PalletId ?? string.Empty,
                    Status           = "pending",
                    CreatedAt        = DateTime.UtcNow,
                }).ToList();

                _context.DispatchDetails.AddRange(dispatchDetails);
                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                return new DispatchListDto
                {
                    Id             = header.Id,
                    DispatchNumber = header.DispatchNumber,
                    DriverName     = header.DriverName,
                    VehicleNumber  = header.VehicleNumber,
                    Status         = header.Status,
                    Notes          = header.Notes ?? string.Empty,
                    CreatedAt      = header.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                    Items          = dispatchDetails.Zip(details, (dd, d) => new DispatchItemDto
                    {
                        Id             = dd.Id,
                        SKUCode        = d.SKU?.SKUCode ?? string.Empty,
                        SKUName        = d.SKU?.SKUName ?? string.Empty,
                        Qty            = dd.Qty,
                        StagingBinCode = dd.StagingBinCode,
                        PalletId       = dd.PalletId,
                        Status         = dd.Status,
                    }).ToList()
                };
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        // ──────────────────────────────────────────────────────────────
        // CONFIRM — Dispatch (physical execution)
        // Moves all InventoryStock records → Status "Dispatched"
        // Creates StockMovement audit entries.
        // ──────────────────────────────────────────────────────────────
        public async Task<DispatchListDto> ConfirmDispatchAsync(int headerId, ConfirmDispatchRequest request)
        {
            var header = await _context.DispatchHeaders
                .Include(h => h.Details)
                    .ThenInclude(d => d.PickingDetail)
                        .ThenInclude(pd => pd!.InventoryStock)
                            .ThenInclude(s => s!.Rack)
                .Include(h => h.Details)
                    .ThenInclude(d => d.SKU)
                .FirstOrDefaultAsync(h => h.Id == headerId)
                ?? throw new InvalidOperationException($"Dispatch {headerId} not found.");

            if (header.Status == "dispatched")
                throw new InvalidOperationException("This dispatch has already been confirmed.");

            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                foreach (var detail in header.Details)
                {
                    var stock = detail.PickingDetail?.InventoryStock
                        ?? throw new InvalidOperationException($"InventoryStock not found for dispatch item {detail.Id}.");

                    var stagingRackId = stock.RackId;
                    var qtyBefore     = stock.Qty;

                    // Finalize stock: mark as Dispatched
                    stock.Qty              = 0;
                    stock.AvailableQty     = 0;
                    stock.ReservedQty      = 0;
                    stock.Status           = "Dispatched";
                    stock.LastMovementDate = DateTime.UtcNow;
                    stock.UpdatedAt        = DateTime.UtcNow;

                    // StockMovement: OUTBOUND STAGING → OUT
                    _context.StockMovements.Add(new StockMovement
                    {
                        SKUId                 = detail.SKUId,
                        MovementType          = "Dispatch",
                        Qty                   = detail.Qty,
                        ReferenceNo           = header.DispatchNumber,
                        QtyBefore             = qtyBefore,
                        QtyAfter              = 0,
                        FromRackId            = stagingRackId,
                        ToRackId              = null,
                        MovementRemarks       = string.IsNullOrWhiteSpace(request.Notes)
                            ? $"Dispatch: {detail.Qty} pcs {detail.SKU?.SKUCode} pallet {detail.PalletId} → {header.DriverName} ({header.VehicleNumber})"
                            : $"Dispatch: {detail.Qty} pcs {detail.SKU?.SKUCode} pallet {detail.PalletId} → {header.DriverName} ({header.VehicleNumber}). Notes: {request.Notes}",
                        MovementReferenceType = "Dispatch",
                        CreatedAt             = DateTime.UtcNow,
                    });

                    detail.Status    = "dispatched";
                    detail.UpdatedAt = DateTime.UtcNow;
                }

                header.Status    = "dispatched";
                header.Notes     = string.IsNullOrWhiteSpace(request.Notes) ? header.Notes : request.Notes;
                header.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                return new DispatchListDto
                {
                    Id             = header.Id,
                    DispatchNumber = header.DispatchNumber,
                    DriverName     = header.DriverName,
                    VehicleNumber  = header.VehicleNumber,
                    Status         = header.Status,
                    Notes          = header.Notes ?? string.Empty,
                    CreatedAt      = header.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                    Items          = header.Details.Select(d => new DispatchItemDto
                    {
                        Id             = d.Id,
                        SKUCode        = d.SKU?.SKUCode ?? string.Empty,
                        SKUName        = d.SKU?.SKUName ?? string.Empty,
                        Qty            = d.Qty,
                        StagingBinCode = d.StagingBinCode,
                        PalletId       = d.PalletId,
                        Status         = d.Status,
                    }).ToList()
                };
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // DTOs
    // ══════════════════════════════════════════════════════════════════

    public class CreateDispatchRequest
    {
        public string DriverName { get; set; } = string.Empty;
        public string VehicleNumber { get; set; } = string.Empty;
        public string? Notes { get; set; }
        public List<int> PickingDetailIds { get; set; } = new();
    }

    public class ConfirmDispatchRequest
    {
        public string? Notes { get; set; }
    }

    public class StagingItemDto
    {
        public int    PickingDetailId { get; set; }
        public string PickingNumber   { get; set; } = string.Empty;
        public string SKUCode         { get; set; } = string.Empty;
        public string SKUName         { get; set; } = string.Empty;
        public int    Qty             { get; set; }
        public string StagingBinCode  { get; set; } = string.Empty;
        public string PalletId        { get; set; } = string.Empty;
    }

    public class DispatchItemDto
    {
        public int    Id             { get; set; }
        public string SKUCode        { get; set; } = string.Empty;
        public string SKUName        { get; set; } = string.Empty;
        public int    Qty            { get; set; }
        public string StagingBinCode { get; set; } = string.Empty;
        public string PalletId       { get; set; } = string.Empty;
        public string Status         { get; set; } = string.Empty;
    }

    public class DispatchListDto
    {
        public int                  Id             { get; set; }
        public string               DispatchNumber { get; set; } = string.Empty;
        public string               DriverName     { get; set; } = string.Empty;
        public string               VehicleNumber  { get; set; } = string.Empty;
        public string               Status         { get; set; } = string.Empty;
        public string               Notes          { get; set; } = string.Empty;
        public string               CreatedAt      { get; set; } = string.Empty;
        public List<DispatchItemDto> Items         { get; set; } = new();
    }
}
