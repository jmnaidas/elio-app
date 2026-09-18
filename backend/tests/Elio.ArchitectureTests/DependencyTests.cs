using System.Xml.Linq;

namespace Elio.ArchitectureTests;

public sealed class DependencyTests
{
    [Fact]
    public void Production_project_dependencies_follow_inward_direction()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Elio.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var allowed = new Dictionary<string, string[]>
        {
            ["Elio.Domain"] = [],
            ["Elio.Application"] = ["Elio.Domain"],
            ["Elio.Infrastructure"] = ["Elio.Application", "Elio.Domain"],
            ["Elio.Api"] = ["Elio.Application", "Elio.Infrastructure"]
        };
        foreach (var (project, dependencies) in allowed)
        {
            var document = XDocument.Load(Path.Combine(directory.FullName, "src", project, project + ".csproj"));
            var references = document.Descendants("ProjectReference")
                .Select(element => Path.GetFileNameWithoutExtension(element.Attribute("Include")!.Value));
            Assert.All(references, reference => Assert.Contains(reference, dependencies));
            if (project == "Elio.Domain")
                Assert.Empty(document.Descendants("PackageReference"));
        }
    }
}

