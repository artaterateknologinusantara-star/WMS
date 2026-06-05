using Microsoft.EntityFrameworkCore;
using Syntera.WMS.API.Data;
using Syntera.WMS.API.Models;

namespace Syntera.WMS.API.Services
{
    public class PickingService(ApplicationDbContext context)
    {
        private readonly ApplicationDbContext _context = context;

        // ──────────────────────────────────────────────────────────────
        // GET — flat list of all picking details for the UI table
        // ──────────────────────────────────────────────────────────────
        public async Task<List<PickingListItemDto>> GetPickingListAsync()
        {
            return await _context.PickingDetails
                .OrderByDescending(d => d.CreatedAt)
                .Select(d => new PickingListItemDto
                {
                    Id                = d.Id,
                    PickingId         = d.Header!.PickingNumber,
                    AssignedTo        = d.Header.AssignedTo,
                    SKUNumber         = d.SKU!.SKUCode ?? string.Empty,
                    SKUName           = d.SKU!.SKUName ?? string.Empty,
                    RequestedQty      = d.RequestedQty,
                    PickedQty         = d.PickedQty,
                    RecommendedBin    = d.SuggestedRack != null ? d.SuggestedRack.BinCode ?? string.Empty : string.Empty,
                    SuggestedPalletId = d.SuggestedPalletId ?? string.Empty,
                    StagingLocation   = d.InventoryStock != null && d.InventoryStock.Rack != null && d.InventoryStock.Rack.Zone == "Outbound Staging"
                                            ? d.InventoryStock.Rack.BinCode ?? string.Empty
                                            : string.Empty,
                    Status            = d.Status
                })
                .ToListAsync();
        }

        // ──────────────────────────────────────────────────────────────
        // CREATE — Picking List (planning only)
        // User provides: SKUCode + RequestedQty + AssignedTo
        // System auto-suggests rack & pallet via FIFO (AvailableQty first)
        // ──────────────────────────────────────────────────────────────
        public async Task<List<PickingListItemDto>> CreatePickingAsync(CreatePickingRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.SKUCode))
                throw new ArgumentException("SKU Code is required.");
            if (request.RequestedQty <= 0)
                throw new ArgumentException("Requested quantity must be greater than zero.");
            if (string.IsNullOrWhiteSpace(request.AssignedTo))
                throw new ArgumentException("Assigned To is required.");

            var sku = await _context.MasterSKUs
                .FirstOrDefaultAsync(s => s.SKUCode == request.SKUCode.Trim());
            if (sku == null)
                throw new InvalidOperationException($"SKU '{request.SKUCode}' not found.");

            // FIFO: all eligible Active pallets, excluding those locked in active picking tasks
            var stocks = await _context.InventoryStocks
                .Include(s => s.Rack)
                .Where(s =>
                    s.SKUId == sku.Id &&
                    s.Status == "Active" &&
                    s.AvailableQty > 0 &&
                    !_context.PickingDetails.Any(pd =>
                        pd.InventoryStockId == s.Id &&
                        (pd.Status == "pending" || pd.Status == "in-progress")))
                .OrderBy(s => s.CreatedAt)
                .ToListAsync();

            var totalAvailable = stocks.Sum(s => s.AvailableQty);
            if (totalAvailable < request.RequestedQty)
                throw new InvalidOperationException(
                    $"Stok tidak mencukupi. Tersedia: {totalAvailable} units, Diminta: {request.RequestedQty} units.");

            // Auto-generate picking number PCK-YYYY-NNN
            var year = DateTime.UtcNow.Year;
            var count = await _context.PickingHeaders.CountAsync(h => h.CreatedAt.Year == year);
            var pickingNumber = $"PCK-{year}-{(count + 1):D3}";

            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                var header = new PickingHeader
                {
                    PickingNumber = pickingNumber,
                    AssignedTo    = request.AssignedTo.Trim(),
                    Status        = "pending",
                    CreatedAt     = DateTime.UtcNow
                };
                _context.PickingHeaders.Add(header);
                await _context.SaveChangesAsync(); // get header.Id

                // Split across pallets FIFO — one PickingDetail per pallet used
                var splits = new List<(PickingDetail detail, InventoryStock stock)>();
                var remaining = request.RequestedQty;

                foreach (var stock in stocks)
                {
                    if (remaining <= 0) break;
                    var takeQty = Math.Min(remaining, stock.AvailableQty);

                    var detail = new PickingDetail
                    {
                        PickingHeaderId   = header.Id,
                        SKUId             = sku.Id,
                        InventoryStockId  = stock.Id,
                        RequestedQty      = takeQty,
                        PickedQty         = 0,
                        SuggestedRackId   = stock.RackId,
                        SuggestedPalletId = stock.PalletId,
                        Status            = "pending",
                        CreatedAt         = DateTime.UtcNow
                    };
                    _context.PickingDetails.Add(detail);

                    stock.ReservedQty  += takeQty;
                    stock.AvailableQty -= takeQty;
                    stock.UpdatedAt     = DateTime.UtcNow;

                    remaining -= takeQty;
                    splits.Add((detail, stock));
                }

                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                return splits.Select(t => new PickingListItemDto
                {
                    Id                = t.detail.Id,
                    PickingId         = header.PickingNumber,
                    AssignedTo        = header.AssignedTo,
                    SKUNumber         = sku.SKUCode ?? string.Empty,
                    SKUName           = sku.SKUName ?? string.Empty,
                    RequestedQty      = t.detail.RequestedQty,
                    PickedQty         = 0,
                    RecommendedBin    = t.stock.Rack?.BinCode ?? string.Empty,
                    SuggestedPalletId = t.stock.PalletId ?? string.Empty,
                    Status            = "pending"
                }).ToList();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        // ──────────────────────────────────────────────────────────────
        // CONFIRM — Picking Process (physical execution)
        // Validates scanned rack & pallet against stored suggestions.
        // Moves inventory to Outbound Staging + creates StockMovement.
        // ──────────────────────────────────────────────────────────────
        public async Task<PickingListItemDto> ConfirmPickAsync(int detailId, ConfirmPickRequest request)
        {
            var detail = await _context.PickingDetails
                .Include(d => d.Header)
                .Include(d => d.SKU)
                .Include(d => d.SuggestedRack)
                .Include(d => d.InventoryStock)
                    .ThenInclude(s => s!.Rack)
                .FirstOrDefaultAsync(d => d.Id == detailId)
                ?? throw new InvalidOperationException($"Picking item {detailId} not found.");

            if (detail.Status == "picked")
                throw new InvalidOperationException("This picking item has already been confirmed.");
            if (detail.Status == "error")
                throw new InvalidOperationException("Cannot confirm a picking item in error state.");

            var stock = detail.InventoryStock
                ?? throw new InvalidOperationException("Associated inventory stock record not found.");

            var actualQty = request.PickedQty ?? detail.RequestedQty;

            // ── Auto-resolve: stock already in Outbound Staging but detail not updated
            // This happens when a previous ConfirmPick succeeded on stock but failed before
            // updating the PickingDetail (e.g. network timeout). Idempotent recovery.
            if (stock.Status == "Outbound Staging")
            {
                await using var recoverTx = await _context.Database.BeginTransactionAsync();
                try
                {
                    detail.PickedQty  = detail.RequestedQty;
                    detail.Status     = "picked";
                    detail.UpdatedAt  = DateTime.UtcNow;

                    var siblingStatuses = await _context.PickingDetails
                        .Where(d => d.PickingHeaderId == detail.PickingHeaderId && d.Id != detail.Id)
                        .Select(d => d.Status).ToListAsync();

                    detail.Header!.Status   = siblingStatuses.All(s => s == "picked") ? "completed" : "in-progress";
                    detail.Header.UpdatedAt = DateTime.UtcNow;

                    await _context.SaveChangesAsync();
                    await recoverTx.CommitAsync();

                    return new PickingListItemDto
                    {
                        Id                = detail.Id,
                        PickingId         = detail.Header.PickingNumber,
                        AssignedTo        = detail.Header.AssignedTo,
                        SKUNumber         = detail.SKU?.SKUCode ?? string.Empty,
                        SKUName           = detail.SKU?.SKUName ?? string.Empty,
                        RequestedQty      = detail.RequestedQty,
                        PickedQty         = detail.PickedQty,
                        RecommendedBin    = detail.SuggestedRack?.BinCode ?? string.Empty,
                        SuggestedPalletId = detail.SuggestedPalletId ?? string.Empty,
                        StagingLocation   = stock.Rack?.BinCode ?? string.Empty,
                        Status            = detail.Status
                    };
                }
                catch { await recoverTx.RollbackAsync(); throw; }
            }

            if (stock.Status == "Dispatched")
                throw new InvalidOperationException(
                    $"Pallet {stock.PalletId} sudah di-dispatch. Task ini tidak bisa diproses lagi.");

            if (stock.Qty <= 0)
                throw new InvalidOperationException(
                    $"Stok pallet {stock.PalletId} sudah kosong (Qty = 0).");

            // ── Validate qty ──
            if (actualQty <= 0)
                throw new ArgumentException("Picked quantity must be greater than zero.");
            if (actualQty > detail.RequestedQty)
                throw new ArgumentException($"Picked quantity ({actualQty}) exceeds requested quantity ({detail.RequestedQty}).");
            if (actualQty > stock.Qty)
                throw new ArgumentException($"Picked quantity ({actualQty}) melebihi stok fisik di pallet ({stock.Qty}).");

            // ── Validate scanned rack against suggestion ──
            if (!string.IsNullOrWhiteSpace(request.ScannedRackCode) && detail.SuggestedRack != null)
            {
                if (!string.Equals(request.ScannedRackCode.Trim(), detail.SuggestedRack.BinCode, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException(
                        $"Wrong rack scanned: '{request.ScannedRackCode}'. " +
                        $"Expected: '{detail.SuggestedRack.BinCode}'.");
            }

            // ── Validate scanned pallet against suggestion ──
            if (!string.IsNullOrWhiteSpace(request.ScannedPalletId) && !string.IsNullOrWhiteSpace(detail.SuggestedPalletId))
            {
                if (!string.Equals(request.ScannedPalletId.Trim(), detail.SuggestedPalletId, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException(
                        $"Wrong pallet scanned: '{request.ScannedPalletId}'. " +
                        $"Expected: '{detail.SuggestedPalletId}'.");
            }

            // ── Resolve staging location ──
            BinLocation stagingLocation;
            if (!string.IsNullOrWhiteSpace(request.StagingLocationCode))
            {
                stagingLocation = await _context.BinLocations
                    .FirstOrDefaultAsync(b => b.BinCode == request.StagingLocationCode.Trim() && b.IsActive == true)
                    ?? throw new InvalidOperationException($"Staging location '{request.StagingLocationCode}' not found or inactive.");
            }
            else
            {
                // Auto-assign first available staging bin
                stagingLocation = await _context.BinLocations
                    .Where(b => b.Zone == "Outbound Staging" && b.IsActive == true)
                    .OrderBy(b => b.BinCode)
                    .FirstOrDefaultAsync()
                    ?? throw new InvalidOperationException(
                        "No staging location available. Create BinLocation records with Zone = 'Outbound Staging'.");
            }

            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                var sourceRackId = stock.RackId;
                var qtyBefore    = stock.Qty;

                // ── Move inventory to Outbound Staging ──
                stock.Qty             -= actualQty;
                stock.ReservedQty      = Math.Max(0, stock.ReservedQty - actualQty);
                stock.AvailableQty     = Math.Max(0, stock.Qty - stock.ReservedQty);
                stock.RackId           = stagingLocation.Id;
                stock.Status           = "Outbound Staging";
                stock.LastMovementDate = DateTime.UtcNow;
                stock.UpdatedAt        = DateTime.UtcNow;

                // ── StockMovement: RACK → OUTBOUND STAGING ──
                _context.StockMovements.Add(new StockMovement
                {
                    SKUId                 = detail.SKUId,
                    MovementType          = "Picking",
                    Qty                   = actualQty,
                    ReferenceNo           = detail.Header!.PickingNumber,
                    QtyBefore             = qtyBefore,
                    QtyAfter              = stock.Qty,
                    FromRackId            = sourceRackId,
                    ToRackId              = stagingLocation.Id,
                    MovementRemarks       = string.IsNullOrWhiteSpace(request.Notes)
                        ? $"Picking: {actualQty} pcs {detail.SKU?.SKUCode} pallet {detail.SuggestedPalletId} → staging {stagingLocation.BinCode}"
                        : $"Picking: {actualQty} pcs {detail.SKU?.SKUCode} pallet {detail.SuggestedPalletId} → staging {stagingLocation.BinCode}. Notes: {request.Notes}",
                    MovementReferenceType = "Picking",
                    CreatedAt             = DateTime.UtcNow
                });

                // ── Update detail status ──
                detail.PickedQty  = actualQty;
                detail.Status     = actualQty >= detail.RequestedQty ? "picked" : "in-progress";
                detail.UpdatedAt  = DateTime.UtcNow;

                // ── Update header status ──
                var siblingStatuses = await _context.PickingDetails
                    .Where(d => d.PickingHeaderId == detail.PickingHeaderId && d.Id != detail.Id)
                    .Select(d => d.Status)
                    .ToListAsync();

                var allPicked = detail.Status == "picked" && siblingStatuses.All(s => s == "picked");
                detail.Header!.Status    = allPicked ? "completed" : "in-progress";
                detail.Header.UpdatedAt  = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                return new PickingListItemDto
                {
                    Id                = detail.Id,
                    PickingId         = detail.Header.PickingNumber,
                    AssignedTo        = detail.Header.AssignedTo,
                    SKUNumber         = detail.SKU?.SKUCode ?? string.Empty,
                    SKUName           = detail.SKU?.SKUName ?? string.Empty,
                    RequestedQty      = detail.RequestedQty,
                    PickedQty         = detail.PickedQty,
                    RecommendedBin    = detail.SuggestedRack?.BinCode ?? string.Empty,
                    SuggestedPalletId = detail.SuggestedPalletId ?? string.Empty,
                    StagingLocation   = stagingLocation.BinCode,
                    Status            = detail.Status
                };
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        // ──────────────────────────────────────────────────────────────
        // CANCEL — release reservation and mark PickingDetail cancelled
        // Releases: ReservedQty, AvailableQty (qty not yet physically picked)
        // Blocked when status is already "picked" or "cancelled".
        // ──────────────────────────────────────────────────────────────
        public async Task<PickingListItemDto> CancelPickAsync(int detailId)
        {
            var detail = await _context.PickingDetails
                .Include(d => d.Header)
                .Include(d => d.SKU)
                .Include(d => d.SuggestedRack)
                .Include(d => d.InventoryStock)
                    .ThenInclude(s => s!.Rack)
                .FirstOrDefaultAsync(d => d.Id == detailId)
                ?? throw new InvalidOperationException($"Picking item {detailId} not found.");

            if (detail.Status == "cancelled")
                throw new InvalidOperationException("This picking item is already cancelled.");
            if (detail.Status == "picked")
                throw new InvalidOperationException("Cannot cancel a picking item that has already been completed.");

            var stock = detail.InventoryStock
                ?? throw new InvalidOperationException("Associated inventory stock record not found.");

            // qty that was reserved but never physically picked
            var releaseQty = detail.RequestedQty - detail.PickedQty;

            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                if (releaseQty > 0)
                {
                    var qtyBefore = stock.Qty;

                    stock.ReservedQty  = Math.Max(0, stock.ReservedQty - releaseQty);
                    stock.AvailableQty += releaseQty;
                    stock.UpdatedAt    = DateTime.UtcNow;

                    _context.StockMovements.Add(new StockMovement
                    {
                        SKUId                 = detail.SKUId,
                        MovementType          = "Cancellation",
                        Qty                   = releaseQty,
                        ReferenceNo           = detail.Header!.PickingNumber,
                        QtyBefore             = qtyBefore,
                        QtyAfter              = stock.Qty,
                        FromRackId            = stock.RackId,
                        ToRackId              = null,
                        MovementRemarks       = $"Picking cancelled: {releaseQty} pcs {detail.SKU?.SKUCode} pallet {detail.SuggestedPalletId} released from reservation",
                        MovementReferenceType = "Picking",
                        CreatedAt             = DateTime.UtcNow
                    });
                }

                detail.Status    = "cancelled";
                detail.UpdatedAt = DateTime.UtcNow;

                // Determine new header status
                var siblingStatuses = await _context.PickingDetails
                    .Where(d => d.PickingHeaderId == detail.PickingHeaderId && d.Id != detail.Id)
                    .Select(d => d.Status)
                    .ToListAsync();

                var allStatuses = siblingStatuses.Append("cancelled").ToList();
                var allTerminal = allStatuses.All(s => s == "picked" || s == "cancelled");
                if (allTerminal)
                    detail.Header!.Status = allStatuses.Any(s => s == "picked") ? "completed" : "cancelled";

                detail.Header!.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                return new PickingListItemDto
                {
                    Id                = detail.Id,
                    PickingId         = detail.Header.PickingNumber,
                    AssignedTo        = detail.Header.AssignedTo,
                    SKUNumber         = detail.SKU?.SKUCode ?? string.Empty,
                    SKUName           = detail.SKU?.SKUName ?? string.Empty,
                    RequestedQty      = detail.RequestedQty,
                    PickedQty         = detail.PickedQty,
                    RecommendedBin    = detail.SuggestedRack?.BinCode ?? string.Empty,
                    SuggestedPalletId = detail.SuggestedPalletId ?? string.Empty,
                    StagingLocation   = string.Empty,
                    Status            = detail.Status
                };
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        // ──────────────────────────────────────────────────────────────
        // FORCE-COMPLETE — close a PickingHeader whose details are all
        // terminal (every detail is "picked" or "cancelled").
        // Throws if any detail is still "pending" or "in-progress".
        // ──────────────────────────────────────────────────────────────
        public async Task<PickingHeaderDto> ForceCompleteAsync(int headerId)
        {
            var header = await _context.PickingHeaders
                .Include(h => h.Details)
                .FirstOrDefaultAsync(h => h.Id == headerId)
                ?? throw new InvalidOperationException($"Picking header {headerId} not found.");

            if (header.Status == "completed")
                throw new InvalidOperationException($"Picking header {header.PickingNumber} is already completed.");

            var blocking = header.Details!
                .Any(d => d.Status == "pending" || d.Status == "in-progress");

            if (blocking)
                throw new InvalidOperationException(
                    $"Cannot force-complete {header.PickingNumber}: one or more details are still pending or in-progress.");

            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                header.Status    = "completed";
                header.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                return new PickingHeaderDto
                {
                    Id            = header.Id,
                    PickingNumber = header.PickingNumber,
                    AssignedTo    = header.AssignedTo,
                    Status        = header.Status,
                    DetailCount   = header.Details!.Count,
                    PickedCount   = header.Details!.Count(d => d.Status == "picked"),
                    CancelledCount = header.Details!.Count(d => d.Status == "cancelled")
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

    public class CreatePickingRequest
    {
        public string SKUCode      { get; set; } = string.Empty;
        public int    RequestedQty { get; set; }
        public string AssignedTo   { get; set; } = string.Empty;
    }

    public class ConfirmPickRequest
    {
        // Physical scan validation — compared against stored suggestions
        public string? ScannedRackCode      { get; set; }
        public string? ScannedPalletId      { get; set; }

        // Actual picked quantity (defaults to RequestedQty if omitted)
        public int?    PickedQty            { get; set; }

        // Staging location (auto-assigned if omitted)
        public string? StagingLocationCode  { get; set; }

        public string? Notes                { get; set; }
    }

    public class PickingListItemDto
    {
        public int    Id                { get; set; }
        public string PickingId         { get; set; } = string.Empty;
        public string AssignedTo        { get; set; } = string.Empty;
        public string SKUNumber         { get; set; } = string.Empty;
        public string SKUName           { get; set; } = string.Empty;
        public int    RequestedQty      { get; set; }
        public int    PickedQty         { get; set; }
        public string RecommendedBin    { get; set; } = string.Empty;
        public string SuggestedPalletId { get; set; } = string.Empty;
        public string StagingLocation   { get; set; } = string.Empty;
        public string Status            { get; set; } = string.Empty;
    }

    public class PickingHeaderDto
    {
        public int    Id             { get; set; }
        public string PickingNumber  { get; set; } = string.Empty;
        public string AssignedTo     { get; set; } = string.Empty;
        public string Status         { get; set; } = string.Empty;
        public int    DetailCount    { get; set; }
        public int    PickedCount    { get; set; }
        public int    CancelledCount { get; set; }
    }
}
