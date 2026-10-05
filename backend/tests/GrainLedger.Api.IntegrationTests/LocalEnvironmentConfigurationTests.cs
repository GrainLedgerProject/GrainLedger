using GrainLedger.Api.Configuration;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class LocalEnvironmentConfigurationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "grainledger-env-" + Guid.NewGuid());

    public LocalEnvironmentConfigurationTests() => Directory.CreateDirectory(root);

    [Fact]
    public void Development_loads_repository_env_from_project_directory()
    {
        File.WriteAllText(Path.Combine(root, "GrainLedger.slnx"), "<Solution />");
        var project = Path.Combine(root, "backend", "src", "Api");
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(root, ".env"), "Authentication__Google__ClientSecret='literal$hash#value'\n");
        var builder = new ConfigurationBuilder();
        builder.AddLocalEnvironmentFile(project, true);
        Assert.Equal("literal$hash#value", builder.Build()["Authentication:Google:ClientSecret"]);
    }

    [Fact]
    public void Deployment_uses_only_content_root_and_explicit_overrides_win()
    {
        File.WriteAllText(Path.Combine(root, ".env"), "Bootstrap__AdminEmail='local@example.com'\n");
        var builder = new ConfigurationBuilder();
        builder.AddLocalEnvironmentFile(root, false);
        builder.AddInMemoryCollection(new Dictionary<string, string?> { ["Bootstrap:AdminEmail"] = "override@example.com" });
        Assert.Equal("override@example.com", builder.Build()["Bootstrap:AdminEmail"]);
        var child = Directory.CreateDirectory(Path.Combine(root, "app"));
        var isolated = new ConfigurationBuilder();
        isolated.AddLocalEnvironmentFile(child.FullName, false);
        Assert.Null(isolated.Build()["Bootstrap:AdminEmail"]);
    }

    public void Dispose() => Directory.Delete(root, true);
}
