using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cardio.Data;

// EF tools do not need to start the web server or bootstrap Identity.
public sealed class PracticeDbFactory : IDesignTimeDbContextFactory<PracticeDb>
{
    public PracticeDb CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Practice")
            ?? "Host=localhost;Port=5432;Database=cardio;Username=cardio_migrator;Password=M123456";
        return new PracticeDb(new DbContextOptionsBuilder<PracticeDb>()
            .UseNpgsql(connection).Options);
    }
}
