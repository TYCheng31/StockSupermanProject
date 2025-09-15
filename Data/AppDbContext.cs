using LineBotDemo.Models;
using Microsoft.EntityFrameworkCore;

namespace LineBotDemo.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        // 對應到 app_users 資料表
        public DbSet<AppUser> AppUsers => Set<AppUser>();

        // 對應到 user_stocks 資料表
        public DbSet<UserStock> UserStocks => Set<UserStock>();
    }
}

