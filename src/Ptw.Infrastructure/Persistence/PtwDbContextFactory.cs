using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ptw.Infrastructure.Persistence;

public sealed class PtwDbContextFactory : IDesignTimeDbContextFactory<PtwDbContext>
{
    public PtwDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PtwDbContext>()
            .UseSqlServer("Server=localhost;Database=PtwOnlineDesign;User Id=sa;Password=Design_time_only_123!;TrustServerCertificate=True")
            .Options;
        return new PtwDbContext(options);
    }
}
