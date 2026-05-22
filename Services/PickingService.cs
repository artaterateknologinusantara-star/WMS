using Microsoft.EntityFrameworkCore;
using Syntera.WMS.API.Data;
using Syntera.WMS.API.Models;

namespace Syntera.WMS.API.Services
{
    public class PickingService(ApplicationDbContext context)
    {
        private readonly ApplicationDbContext _context = context;

        public async Task<List<PickingListItemDto>> GetPickingListAsync()
        {
            return await _context.PickingDetails
                .Include(d => d.Header)
                .Include(d => d.SKU)
                .Include(d => d.InventoryStock)
                    .ThenInclude(s => s!.Rack)
                .OrderByDescending(d => d.CreatedAt)
                .Select(d => new PickingListItemDto
                {
                    Id = d.Id,
                    PickingId = d.Header!.PickingNumber,
                    SKUNumber = d.SKU!.SKUCode ?? string.Empty,
                    SKUName = d.SKU!.SKUName ?? string.Empty,
                    RequestedQty = d.RequestedQty,
                    RecommendedBin = d.InventoryStock != null && d.InventoryStock.Rack != null
                        ? d.InventoryStock.Rack.BinCode ?? string.Empty
                        : string.Empty,
                    PalletId = d.InventoryStock != null ? d.InventoryStock.PalletId ?? string.Empty : string.Empty,
                    PickedQty = d.PickedQty,
                    Status = d.Status
                })
                .ToListAsync();
        }

        public async Task<PickingListItemDto> CreatePickingAsync(CreatePickingRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.SKUCode))
                throw new ArgumentException("SKU Code is required.");
            if (string.IsNullOrWhiteSpace(request.PalletId))
                throw new ArgumentException("Pallet ID is required.");
            if (request.RequestedQty <= 0)
                throw new ArgumentException("Requested quantity must be greater than zero.");
            if (string.IsNullOrWhiteSpace(request.AssignedTo))
                throw new ArgumentException("Assigned To is required.");

            var sku = await _context.MasterSKUs
                .FirstOrDefaultAsync(s => s.SKUCode == request.SKUCode);
            if (sku == null)
                throw new InvalidOperationException($"SKU '{request.SKUCode}' not found.");

            var stock = await _context.InventoryStocks
                .Include(s => s.Rack)
                .FirstOrDefaultAsync(s => s.PalletId == request.PalletId && s.SKUId == sku.Id && s.Status == "Active");
            if (stock == null)
                throw new InvalidOperationException($"Pallet '{request.PalletId}' does not contain SKU '{request.SKUCode}' or is not active.");

            if (stock.AvailableQty < request.RequestedQty)
                throw new InvalidOperationException($"Insufficient available quantity. Available: {stock.AvailableQty}, Requested: {request.RequestedQty}.");

            // Auto-generate picking number: PCK-YYYY-NNN
            var year = DateTime.UtcNow.Year;
            var count = await _context.PickingHeaders.CountAsync(h => h.CreatedAt.Year == year);
            var pickingNumber = $"PCK-{year}-{(count + 1):D3}";

            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                var header = new PickingHeader
                {
                    PickingNumber = pickingNumber,
                    AssignedTo = request.AssignedTo.Trim(),
                    Status = "pending",
                    CreatedAt = DateTime.UtcNow
                };
                _context.PickingHeaders.Add(header);
                await _context.SaveChangesAsync();

                var detail = new PickingDetail
                {
                    PickingHeaderId = header.Id,
                    SKUId = sku.Id,
                    InventoryStockId = stock.Id,
                    RequestedQty = request.RequestedQty,
                    PickedQty = 0,
                    Status = "pending",
                    CreatedAt = DateTime.UtcNow
                };
                _context.PickingDetails.Add(detail);

                // Reserve qty in InventoryStock
                stock.ReservedQty += request.RequestedQty;
                stock.AvailableQty -= request.RequestedQty;
                stock.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                return new PickingListItemDto
                {
                    Id = detail.Id,
                    PickingId = header.PickingNumber,
                    SKUNumber = sku.SKUCode ?? string.Empty,
                    SKUName = sku.SKUName ?? string.Empty,
                    RequestedQty = detail.RequestedQty,
                    RecommendedBin = stock.Rack?.BinCode ?? string.Empty,
                    PalletId = stock.PalletId ?? string.Empty,
                    PickedQty = 0,
                    Status = "pending"
                };
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        public async Task<PickingListItemDto> ConfirmPickAsync(int detailId, ConfirmPickRequest request)
        {
            var detail = await _context.PickingDetails
                .Include(d => d.Header)
                .Include(d => d.SKU)
                .Include(d => d.InventoryStock)
                    .ThenInclude(s => s!.Rack)
                .FirstOrDefaultAsync(d => d.Id == detailId);

            if (detail == null)
                throw new InvalidOperationException($"Picking item {detailId} not found.");

            if (detail.Status == "picked")
                throw new InvalidOperationException("This picking item has already been confirmed.");

            if (detail.Status == "error")
                throw new InvalidOperationException("Cannot confirm a picking item in error state.");

            var stock = detail.InventoryStock
                ?? throw new InvalidOperationException("Associated inventory stock record not found.");

            var actualQty = request.PickedQty ?? detail.RequestedQty;

            if (actualQty <= 0)
                throw new ArgumentException("Picked quantity must be greater than zero.");

            if (actualQty > detail.RequestedQty)
                throw new ArgumentException($"Picked quantity {actualQty} exceeds requested quantity {detail.RequestedQty}.");

            if (actualQty > stock.Qty)
                throw new InvalidOperationException($"Picked quantity {actualQty} exceeds available stock {stock.Qty}.");

            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                var qtyBefore = stock.Qty;

                // Deduct actual picked qty from stock
                stock.Qty -= actualQty;
                stock.ReservedQty = Math.Max(0, stock.ReservedQty - actualQty);
                stock.AvailableQty = stock.Qty - stock.ReservedQty;
                stock.LastMovementDate = DateTime.UtcNow;
                stock.UpdatedAt = DateTime.UtcNow;
                if (stock.Qty <= 0)
                {
                    stock.Qty = 0;
                    stock.AvailableQty = 0;
                    stock.ReservedQty = 0;
                    stock.Status = "Empty";
                }

                _context.StockMovements.Add(new StockMovement
                {
                    SKUId = detail.SKUId,
                    MovementType = "Picking",
                    Qty = actualQty,
                    ReferenceNo = detail.Header!.PickingNumber,
                    QtyBefore = qtyBefore,
                    QtyAfter = stock.Qty,
                    FromRackId = stock.RackId,
                    ToRackId = null,
                    MovementRemarks = !string.IsNullOrWhiteSpace(request.Notes)
                        ? $"Picking: {actualQty} units of {detail.SKU?.SKUCode} from pallet {stock.PalletId}. Notes: {request.Notes}"
                        : $"Picking: {actualQty} units of {detail.SKU?.SKUCode} from pallet {stock.PalletId}",
                    MovementReferenceType = "Picking",
                    CreatedAt = DateTime.UtcNow
                });

                // Full pick or partial pick
                detail.PickedQty = actualQty;
                detail.Status = actualQty >= detail.RequestedQty ? "picked" : "in-progress";
                detail.UpdatedAt = DateTime.UtcNow;

                // Update header: completed only if every detail is fully picked
                var siblingStatuses = await _context.PickingDetails
                    .Where(d => d.PickingHeaderId == detail.PickingHeaderId && d.Id != detail.Id)
                    .Select(d => d.Status)
                    .ToListAsync();

                var allPicked = detail.Status == "picked" && siblingStatuses.All(s => s == "picked");
                detail.Header!.Status = allPicked ? "completed" : "in-progress";
                detail.Header.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                return new PickingListItemDto
                {
                    Id = detail.Id,
                    PickingId = detail.Header.PickingNumber,
                    SKUNumber = detail.SKU?.SKUCode ?? string.Empty,
                    SKUName = detail.SKU?.SKUName ?? string.Empty,
                    RequestedQty = detail.RequestedQty,
                    RecommendedBin = stock.Rack?.BinCode ?? string.Empty,
                    PalletId = stock.PalletId ?? string.Empty,
                    PickedQty = detail.PickedQty,
                    Status = detail.Status
                };
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }
    }

    // ============ DTOs ============

    public class CreatePickingRequest
    {
        public string SKUCode { get; set; } = string.Empty;
        public string PalletId { get; set; } = string.Empty;
        public int RequestedQty { get; set; }
        public string AssignedTo { get; set; } = string.Empty;
    }

    public class ConfirmPickRequest
    {
        public int? PickedQty { get; set; }
        public string? Notes { get; set; }
    }

    public class PickingListItemDto
    {
        public int Id { get; set; }
        public string PickingId { get; set; } = string.Empty;
        public string SKUNumber { get; set; } = string.Empty;
        public string SKUName { get; set; } = string.Empty;
        public int RequestedQty { get; set; }
        public string RecommendedBin { get; set; } = string.Empty;
        public string PalletId { get; set; } = string.Empty;
        public int PickedQty { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}
