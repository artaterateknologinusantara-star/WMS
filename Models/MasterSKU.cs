using System.ComponentModel.DataAnnotations.Schema;

namespace Syntera.WMS.API.Models
{
    [Table("MasterSKU")]
    public class MasterSKU
    {
        public int Id { get; set; }

        public string? SKUCode { get; set; }

        public string? SKUName { get; set; }

        public int? CategoryId { get; set; }
        public int? UOMId { get; set; }
        public string? Barcode { get; set; }
        public string? PalletId { get; set; }
        public int? BinLocationId { get; set; }
        public int Qty { get; set; }
        public int MinStock { get; set; }
        public int MaxStock { get; set; }
        public string? Status { get; set; }
        public DateTime? LastMovement { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public Category? Category { get; set; }
        public UOM? UOM { get; set; }
        public BinLocation? BinLocation { get; set; }
    }
}