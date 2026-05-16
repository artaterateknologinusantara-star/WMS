using System.ComponentModel.DataAnnotations.Schema;

namespace Syntera.WMS.API.Models
{
    [Table("InventoryStock")]
    public class InventoryStock
    {
        public int Id { get; set; }

        public int SKUId { get; set; }

        [ForeignKey("SKUId")]
        public MasterSKU? SKU { get; set; }

        public string? PalletId { get; set; }

        public int? RackId { get; set; }

        [ForeignKey("RackId")]
        public BinLocation? Rack { get; set; }

        public int Qty { get; set; }

        public int ReservedQty { get; set; }

        public int AvailableQty { get; set; }

        public string? Status { get; set; }

        public DateTime? LastMovementDate { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }
    }
}