using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Exb.Data;

/// <summary>
/// Used only by the EF Core command-line tools, when adding a migration or
/// generating a deploy script.
///
/// Without it the tooling boots the web host to find a DbContext, which would
/// run first-time setup — migrating and seeding a database — as a side effect of
/// asking for a migration file. This keeps design-time work entirely offline: it
/// never connects, so the connection string here only has to be well-formed.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ExhibitionDbContext>
{
    public ExhibitionDbContext CreateDbContext(string[] args)
    {
        string connectionString = Environment.GetEnvironmentVariable("EXB_CONNECTION")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=ExhibitionTracker;Integrated Security=true;TrustServerCertificate=true";

        var options = new DbContextOptionsBuilder<ExhibitionDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new ExhibitionDbContext(options);
    }
}
