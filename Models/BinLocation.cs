using System.ComponentModel.DataAnnotations.Schema;

namespace Syntera.WMS.API.Models
{
    [Table("BinLocations")]
    public class BinLocation
    {
        public int Id { get; set; }
        public int WarehouseId { get; set; }
        public string? BinCode { get; set; }
        public string? Zone { get; set; }
        public string? Rack { get; set; }
        public string? LevelNo { get; set; }
        public int CapacityQty { get; set; }
        public bool IsActive { get; set; }
        public DateTime? CreatedAt { get; set; }
    }
}
