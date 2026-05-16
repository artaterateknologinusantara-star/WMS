using Microsoft.EntityFrameworkCore;
using Syntera.WMS.API.Models;

namespace Syntera.WMS.API.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(
            DbContextOptions<ApplicationDbContext> options
        ) : base(options)
        {
        }

        public DbSet<MasterSKU> MasterSKUs { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<UOM> UOM { get; set; }
        public DbSet<BinLocation> BinLocations { get; set; }
        public DbSet<InventoryAdjustment> InventoryAdjustments { get; set; }
        public DbSet<StockMovement> StockMovements { get; set; }
        public DbSet<InventoryStock> InventoryStocks { get; set; }
        public DbSet<ReceivingHeader> ReceivingHeaders { get; set; }
        public DbSet<ReceivingDetail> ReceivingDetails { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<Role> Roles { get; set; }
    }
}