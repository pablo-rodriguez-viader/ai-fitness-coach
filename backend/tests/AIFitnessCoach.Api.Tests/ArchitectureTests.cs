using System.Xml.Linq;
using Xunit;

namespace AIFitnessCoach.Api.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void ProjectReferences_RespectCleanArchitectureBoundaries()
    {
        var root = FindWorkspaceRoot();
        Assert.Empty(ProjectReferences(root, "AIFitnessCoach.Domain"));
        Assert.Equal(
            ["AIFitnessCoach.Domain"],
            ProjectReferences(root, "AIFitnessCoach.Application"));
        Assert.Equal(
            ["AIFitnessCoach.AI", "AIFitnessCoach.Application", "AIFitnessCoach.Domain"],
            ProjectReferences(root, "AIFitnessCoach.Infrastructure"));
        Assert.Equal(
            ["AIFitnessCoach.AI", "AIFitnessCoach.Application", "AIFitnessCoach.Infrastructure"],
            ProjectReferences(root, "AIFitnessCoach.Api"));
        Assert.Empty(ProjectReferences(root, "AIFitnessCoach.AI"));
    }

    [Fact]
    public void EndpointPresentation_DoesNotDependOnEfDbContext()
    {
        var root = FindWorkspaceRoot();
        var endpointsPath = Path.Combine(
            root,
            "backend",
            "src",
            "AIFitnessCoach.Api",
            "Endpoints",
            "ApiEndpoints.cs");

        Assert.DoesNotContain("FitnessDbContext", File.ReadAllText(endpointsPath));
    }

    private static string FindWorkspaceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(
                directory.FullName,
                "backend",
                "src",
                "AIFitnessCoach.Api",
                "AIFitnessCoach.Api.csproj")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the workspace root.");
    }

    private static string[] ProjectReferences(string root, string projectName)
    {
        var projectDirectory = projectName == "AIFitnessCoach.Api.Tests"
            ? Path.Combine(root, "backend", "tests", projectName)
            : Path.Combine(root, "backend", "src", projectName);
        var projectPath = Path.Combine(projectDirectory, $"{projectName}.csproj");
        var xml = XDocument.Load(projectPath);
        // Include paths use Windows separators ("..\X\X.csproj"); on Linux "\" is not a
        // separator, so normalise before taking the file name.
        return xml.Descendants("ProjectReference")
            .Select(reference => Path.GetFileNameWithoutExtension(
                (reference.Attribute("Include")?.Value
                    ?? throw new InvalidDataException("Project reference has no Include path."))
                .Replace('\\', '/')))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }
}
