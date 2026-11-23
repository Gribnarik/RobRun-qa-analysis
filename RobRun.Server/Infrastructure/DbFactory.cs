using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace RobRun.Server.Infrastructure;

public static class DbFactory
{
    public static ServerVersion Version(string connectionString)
    {
        var probe = new MySqlConnectionStringBuilder(connectionString) { Database = "mysql" };
        return ServerVersion.AutoDetect(probe.ConnectionString);
    }

    public static AppDb Open(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDb>()
            .UseMySql(connectionString, Version(connectionString))
            .Options;
        return new AppDb(options);
    }
}
