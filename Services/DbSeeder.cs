using Microsoft.EntityFrameworkCore;
using Syntera.WMS.API.Data;
using Syntera.WMS.API.Models;

namespace Syntera.WMS.API.Services
{
    public static class DbSeeder
    {
        public static async Task SeedAsync(ApplicationDbContext context)
        {
            // Seed roles
            var roleNames = new[] { "SuperAdmin", "WarehouseManager", "InventoryStaff", "InboundStaff", "OutboundStaff" };
            foreach (var roleName in roleNames)
            {
                if (!await context.Roles.AnyAsync(r => r.RoleName == roleName))
                {
                    context.Roles.Add(new Role { RoleName = roleName, Description = roleName });
                }
            }
            await context.SaveChangesAsync();

            // Seed default admin user
            if (!await context.Users.AnyAsync(u => u.Username == "admin"))
            {
                var superAdminRole = await context.Roles.FirstAsync(r => r.RoleName == "SuperAdmin");
                context.Users.Add(new User
                {
                    Username = "admin",
                    FullName = "Super Admin",
                    Email = "admin@synterawms.com",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@123"),
                    RoleId = superAdminRole.Id,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
                await context.SaveChangesAsync();
            }

            // Seed warehouse manager
            if (!await context.Users.AnyAsync(u => u.Username == "manager"))
            {
                var managerRole = await context.Roles.FirstAsync(r => r.RoleName == "WarehouseManager");
                context.Users.Add(new User
                {
                    Username = "manager",
                    FullName = "Warehouse Manager",
                    Email = "manager@synterawms.com",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Manager@123"),
                    RoleId = managerRole.Id,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
                await context.SaveChangesAsync();
            }

            // Seed inventory staff
            if (!await context.Users.AnyAsync(u => u.Username == "staff"))
            {
                var staffRole = await context.Roles.FirstAsync(r => r.RoleName == "InventoryStaff");
                context.Users.Add(new User
                {
                    Username = "staff",
                    FullName = "Inventory Staff",
                    Email = "staff@synterawms.com",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Staff@123"),
                    RoleId = staffRole.Id,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
                await context.SaveChangesAsync();
            }
        }
    }
}
