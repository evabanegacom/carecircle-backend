using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.FileProviders;
using PeerCare.Services;

namespace PeerCare.Data;

public sealed class PeerCareDesignTimeFactory : IDesignTimeDbContextFactory<PeerCareDbContext>
{
    public PeerCareDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Security:BlindIndexKey"] = "LOCAL-DESIGN-TIME-KEY-NOT-FOR-DEPLOYMENT"
        }).Build();
        var services = new ServiceCollection();
        services.AddDataProtection();
        var provider = services.BuildServiceProvider();
        var crypto = new ContactCrypto(provider.GetRequiredService<IDataProtectionProvider>(), configuration, new DesignEnvironment());
        var options = new DbContextOptionsBuilder<PeerCareDbContext>()
            .UseNpgsql(PeerCareConnectionString.Normalize(configuration.GetConnectionString("PeerCare")))
            .Options;
        return new PeerCareDbContext(options, crypto);
    }

    private sealed class DesignEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "PeerCare";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
