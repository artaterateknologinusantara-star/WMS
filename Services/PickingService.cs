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
        public async Task<PickingListItemDto> CreatePickingAsync(CreatePickingRequest request)
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

            // FIFO: oldest Active stock first with sufficient AvailableQty
            var stock = await _context.InventoryStocks
                .Include(s => s.Rack)
                .Where(s =>
                    s.SKUId == sku.Id &&
                    s.Status == "Active" &&
                    s.AvailableQty >= request.RequestedQty)
                .OrderBy(s => s.CreatedAt)
                .FirstOrDefaultAsync();

            if (stock == null)
                throw new InvalidOperationException(
                    $"No available stock for SKU '{request.SKUCode}' with sufficient quantity. " +
                    $"Requested: {request.RequestedQty}. Ensure stock has been put away.");

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
                await _context.SaveChangesAsync();

                var detail = new PickingDetail
                {
                    PickingHeaderId   = header.Id,
                    SKUId             = sku.Id,
                    InventoryStockId  = stock.Id,
                    RequestedQty      = request.RequestedQty,
                    PickedQty         = 0,
                    SuggestedRackId   = stock.RackId,
                    SuggestedPalletId = stock.PalletId,
                    Status            = "pending",
                    CreatedAt         = DateTime.UtcNow
                };
                _context.PickingDetails.Add(detail);

                // Reserve stock
                stock.ReservedQty  += request.RequestedQty;
                stock.AvailableQty -= request.RequestedQty;
                stock.UpdatedAt     = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                return new PickingListItemDto
                {
                    Id                = detail.Id,
                    PickingId         = header.PickingNumber,
                    AssignedTo        = header.AssignedTo,
                    SKUNumber         = sku.SKUCode ?? string.Empty,
                    SKUName           = sku.SKUName ?? string.Empty,
                    RequestedQty      = detail.RequestedQty,
                    PickedQty         = 0,
                    RecommendedBin    = stock.Rack?.BinCode ?? string.Empty,
                    SuggestedPalletId = stock.PalletId ?? string.Empty,
                    Status            = "pending"
                };
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

            // ── Validate qty ──
            if (actualQty <= 0)
                throw new ArgumentException("Picked quantity must be greater than zero.");
            if (actualQty > detail.RequestedQty)
                throw new ArgumentException($"Picked quantity ({actualQty}) exceeds requested quantity ({detail.RequestedQty}).");
            if (actualQty > stock.Qty)
                throw new InvalidOperationException($"Picked quantity ({actualQty}) exceeds physical stock on pallet ({stock.Qty}).");

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
                stock.Status           = stock.Qty <= 0 ? "Empty" : "Outbound Staging";
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
}
