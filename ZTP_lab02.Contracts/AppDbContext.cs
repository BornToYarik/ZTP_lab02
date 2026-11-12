using Microsoft.EntityFrameworkCore;

namespace ZTP_lab02.Contracts;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options): base(options)
    {
    }

    public DbSet<Notification> Notifications => Set<Notification>();
}