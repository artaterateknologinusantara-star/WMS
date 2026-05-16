using System.ComponentModel.DataAnnotations.Schema;

namespace Syntera.WMS.API.Models
{
    [Table("StockMovements")]
    public class StockMovement
    {
        public int Id { get; set; }

        public int SKUId { get; set; }

        [ForeignKey("SKUId")]
        public MasterSKU? SKU { get; set; }

        public string? MovementType { get; set; }

        public int Qty { get; set; }

        public string? ReferenceNo { get; set; }

        public int? QtyBefore { get; set; }

        public int? QtyAfter { get; set; }

        public int? FromRackId { get; set; }

        public int? ToRackId { get; set; }

        public string? MovementRemarks { get; set; }

        public string? MovementReferenceType { get; set; }

        public int? CreatedBy { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
