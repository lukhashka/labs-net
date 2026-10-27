using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Catalog.Dal;

// Used only by `dotnet ef` when the startup project can't build the context itself
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=CatalogDb;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        return new CatalogDbContext(options);
    }
}
