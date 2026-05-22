using System.ComponentModel.DataAnnotations.Schema;

namespace Syntera.WMS.API.Models
{
    [Table("PickingHeader")]
    public class PickingHeader
    {
        public int Id { get; set; }

        public string PickingNumber { get; set; } = string.Empty;

        public string AssignedTo { get; set; } = string.Empty;

        public string Status { get; set; } = "Pending";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        public ICollection<PickingDetail> Details { get; set; } = new List<PickingDetail>();
    }
}
