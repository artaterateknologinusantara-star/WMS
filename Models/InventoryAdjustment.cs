using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Syntera.WMS.API.Models
{
    [Table("InventoryAdjustments")]
    public class InventoryAdjustment
    {
        [Key]
        public int Id { get; set; }

        public string AdjustmentNo { get; set; } = string.Empty;

        public int SKUId { get; set; }

        [ForeignKey("SKUId")]
        public MasterSKU? SKU { get; set; }

        public string? PalletId { get; set; }

        public int PrevQty { get; set; }

        public int NewQty { get; set; }

        public string? AdjustmentType { get; set; }

        public string? Reason { get; set; }

        public string? Remarks { get; set; }

        public int? AdjustedBy { get; set; }

        public int? RequestedBy { get; set; }

        public DateTime? RequestedAt { get; set; }

        public string ApprovalStatus { get; set; } = "Pending";

        public int? ApprovedBy { get; set; }

        public DateTime? ApprovedAt { get; set; }

        public string? RejectedReason { get; set; }

        public bool IsProcessed { get; set; } = false;

        public DateTime CreatedAt { get; set; }
    }
}