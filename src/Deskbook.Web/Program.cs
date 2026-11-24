using Deskbook.Web;

var connectionString = Environment.GetEnvironmentVariable("DESKBOOK_DB")
    ?? throw new InvalidOperationException("DESKBOOK_DB is not set (a PostgreSQL connection string)");
var app = WebApp.Build(args, connectionString);
await app.RunAsync();
