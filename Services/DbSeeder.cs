using Microsoft.EntityFrameworkCore;
using Syntera.WMS.API.Data;
using Syntera.WMS.API.Models;

namespace Syntera.WMS.API.Services
{
    public static class DbSeeder
    {
        public static async Task SeedAsync(ApplicationDbContext context)
        {
            await SeedRolesAsync(context);
            await SeedUsersAsync(context);
            await SeedCategoriesAsync(context);
            await SeedUOMsAsync(context);
            await SeedBinLocationsAsync(context);
            await SeedMasterSKUsAsync(context);
        }

        // ── Roles ─────────────────────────────────────────────────────────

        private static async Task SeedRolesAsync(ApplicationDbContext context)
        {
            var roleNames = new[] { "SuperAdmin", "WarehouseManager", "InventoryStaff", "InboundStaff", "OutboundStaff" };
            foreach (var name in roleNames)
            {
                if (!await context.Roles.AnyAsync(r => r.RoleName == name))
                    context.Roles.Add(new Role { RoleName = name, Description = name });
            }
            await context.SaveChangesAsync();
        }

        // ── Users ─────────────────────────────────────────────────────────

        private static async Task SeedUsersAsync(ApplicationDbContext context)
        {
            var users = new[]
            {
                ("admin",   "Super Admin",       "admin@synterawms.com",   "Admin@123",   "SuperAdmin"),
                ("manager", "Warehouse Manager", "manager@synterawms.com", "Manager@123", "WarehouseManager"),
                ("staff",   "Inventory Staff",   "staff@synterawms.com",   "Staff@123",   "InventoryStaff"),
            };

            foreach (var (username, fullName, email, password, roleName) in users)
            {
                if (!await context.Users.AnyAsync(u => u.Username == username))
                {
                    var role = await context.Roles.FirstAsync(r => r.RoleName == roleName);
                    context.Users.Add(new User
                    {
                        Username     = username,
                        FullName     = fullName,
                        Email        = email,
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                        RoleId       = role.Id,
                        IsActive     = true,
                        CreatedAt    = DateTime.UtcNow,
                        UpdatedAt    = DateTime.UtcNow
                    });
                }
            }
            await context.SaveChangesAsync();
        }

        // ── Categories ────────────────────────────────────────────────────

        private static async Task SeedCategoriesAsync(ApplicationDbContext context)
        {
            var categories = new[] { "Electronics", "FMCG", "Raw Material", "Packaging" };
            foreach (var name in categories)
            {
                if (!await context.Categories.AnyAsync(c => c.CategoryName == name))
                    context.Categories.Add(new Category { CategoryName = name });
            }
            await context.SaveChangesAsync();
        }

        // ── UOMs ──────────────────────────────────────────────────────────

        private static async Task SeedUOMsAsync(ApplicationDbContext context)
        {
            var uoms = new[] {
                ("Pieces",   "PCS"),
                ("Kilogram", "KG"),
                ("Box",      "BOX"),
                ("Carton",   "CTN"),
                ("Liter",    "LTR"),
            };
            foreach (var (name, code) in uoms)
            {
                if (!await context.UOM.AnyAsync(u => u.UOMCode == code))
                    context.UOM.Add(new UOM { UOMName = name, UOMCode = code });
            }
            await context.SaveChangesAsync();
        }

        // ── Bin Locations ─────────────────────────────────────────────────
        // Zones:
        //   A / B / C     → main storage racks
        //   Inbound Staging  → receiving dock (LOADING-*)
        //   Outbound Staging → picking staging area (STG-*)

        private static async Task SeedBinLocationsAsync(ApplicationDbContext context)
        {
            var bins = new[]
            {
                // Zone A — 3 racks × 2 levels
                ("A-01-001", "A", "R01", "L1", 50),
                ("A-01-002", "A", "R01", "L2", 50),
                ("A-02-001", "A", "R02", "L1", 50),
                ("A-02-002", "A", "R02", "L2", 50),
                ("A-03-001", "A", "R03", "L1", 50),
                ("A-03-002", "A", "R03", "L2", 50),

                // Zone B — 2 racks × 2 levels
                ("B-01-001", "B", "R01", "L1", 50),
                ("B-01-002", "B", "R01", "L2", 50),
                ("B-02-001", "B", "R02", "L1", 50),
                ("B-02-002", "B", "R02", "L2", 50),

                // Zone C — 2 racks × 2 levels
                ("C-01-001", "C", "R01", "L1", 50),
                ("C-01-002", "C", "R01", "L2", 50),
                ("C-02-001", "C", "R02", "L1", 50),
                ("C-02-002", "C", "R02", "L2", 50),

                // Inbound Staging — receiving dock
                ("LOADING-01", "Inbound Staging", "LOADING", "01", 200),
                ("LOADING-02", "Inbound Staging", "LOADING", "02", 200),

                // Outbound Staging — picking / dispatch area
                ("STG-A01", "Outbound Staging", "STG-A", "01", 100),
                ("STG-A02", "Outbound Staging", "STG-A", "02", 100),
                ("STG-B01", "Outbound Staging", "STG-B", "01", 100),
                ("STG-B02", "Outbound Staging", "STG-B", "02", 100),
            };

            foreach (var (code, zone, rack, level, cap) in bins)
            {
                if (!await context.BinLocations.AnyAsync(b => b.BinCode == code))
                {
                    context.BinLocations.Add(new BinLocation
                    {
                        WarehouseId = 1,
                        BinCode     = code,
                        Zone        = zone,
                        Rack        = rack,
                        LevelNo     = level,
                        CapacityQty = cap,
                        IsActive    = true,
                        CreatedAt   = DateTime.UtcNow
                    });
                }
            }
            await context.SaveChangesAsync();
        }

        // ── Master SKUs ───────────────────────────────────────────────────

        private static async Task SeedMasterSKUsAsync(ApplicationDbContext context)
        {
            // Resolve category and UOM IDs
            var electronics  = await context.Categories.FirstOrDefaultAsync(c => c.CategoryName == "Electronics");
            var fmcg         = await context.Categories.FirstOrDefaultAsync(c => c.CategoryName == "FMCG");
            var rawMaterial   = await context.Categories.FirstOrDefaultAsync(c => c.CategoryName == "Raw Material");
            var packaging    = await context.Categories.FirstOrDefaultAsync(c => c.CategoryName == "Packaging");

            var pcs = await context.UOM.FirstOrDefaultAsync(u => u.UOMCode == "PCS");
            var kg  = await context.UOM.FirstOrDefaultAsync(u => u.UOMCode == "KG");
            var ctn = await context.UOM.FirstOrDefaultAsync(u => u.UOMCode == "CTN");
            var ltr = await context.UOM.FirstOrDefaultAsync(u => u.UOMCode == "LTR");

            if (electronics == null || fmcg == null || rawMaterial == null || packaging == null
                || pcs == null || kg == null || ctn == null || ltr == null)
                return;

            var skus = new[]
            {
                ("SKU-LAPTOP-001", "Laptop Dell XPS 15 Inch",        electronics.Id, pcs.Id),
                ("SKU-MOUSE-002",  "Mouse Wireless Logitech M720",   electronics.Id, pcs.Id),
                ("SKU-KEYBD-003",  "Keyboard Mechanical Rexus K9",   electronics.Id, pcs.Id),
                ("SKU-MONIT-004",  "Monitor LG 27 Inch 4K",          electronics.Id, pcs.Id),
                ("SKU-MINYAK-005", "Minyak Goreng Bimoli 2 Liter",   fmcg.Id,        ltr.Id),
                ("SKU-GULA-006",   "Gula Pasir Premium 1 KG",        fmcg.Id,        kg.Id),
                ("SKU-INDOM-007",  "Indomie Goreng 1 Karton",        fmcg.Id,        ctn.Id),
                ("SKU-TEPUN-008",  "Tepung Terigu Segitiga 25KG",    rawMaterial.Id, kg.Id),
                ("SKU-KRDUS-009",  "Kardus Box Large 60x40x40",      packaging.Id,   pcs.Id),
                ("SKU-SPEAK-010",  "Speaker JBL Charge 5 Portable",  electronics.Id, pcs.Id),
            };

            foreach (var (code, name, catId, uomId) in skus)
            {
                if (!await context.MasterSKUs.AnyAsync(s => s.SKUCode == code))
                {
                    context.MasterSKUs.Add(new MasterSKU
                    {
                        SKUCode    = code,
                        SKUName    = name,
                        CategoryId = catId,
                        UOMId      = uomId,
                        Qty        = 0,
                        Status     = "Active",
                        CreatedAt  = DateTime.UtcNow,
                        UpdatedAt  = DateTime.UtcNow
                    });
                }
            }
            await context.SaveChangesAsync();
        }
    }
}
