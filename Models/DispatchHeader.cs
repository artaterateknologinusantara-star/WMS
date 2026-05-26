using System.ComponentModel.DataAnnotations.Schema;

namespace Syntera.WMS.API.Models
{
    [Table("DispatchHeader")]
    public class DispatchHeader
    {
        public int Id { get; set; }

        public string DispatchNumber { get; set; } = string.Empty;

        public string DriverName { get; set; } = string.Empty;

        public string VehicleNumber { get; set; } = string.Empty;

        // pending | dispatched
        public string Status { get; set; } = "pending";

        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        public ICollection<DispatchDetail> Details { get; set; } = new List<DispatchDetail>();
    }
}
