using ARK.Core.Execution;
using ARK.Core.Instances;

namespace ARK.Tests;

public class InstancePathsTests
{
    [Fact]
    public void Default_instance_root_resolves_relative_to_the_executable()
    {
        var paths = new InstancePaths();

        Assert.Equal(InstancePaths.DefaultInstanceName, paths.InstanceName);
        Assert.StartsWith(AppContext.BaseDirectory, paths.Root, StringComparison.Ordinal);
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "instances", "default"), paths.Root);
    }

    [Fact]
    public void Provision_plan_creates_the_full_directory_tree_for_a_named_instance()
    {
        var root = TempRoot.Create();
        try
        {
            var paths = new InstancePaths("Sega Saturn", root);

            // Names are sanitized and the tree does not exist until the plan is applied.
            Assert.Equal("Sega Saturn", paths.InstanceName);
            Assert.False(Directory.Exists(paths.Root));

            var result = new Executor(paths).Execute(paths.BuildProvisionPlan(), apply: true);

            Assert.True(result.Success);
            Assert.Equal(5, result.CompletedCount);
            Assert.True(Directory.Exists(paths.Db));
            Assert.True(Directory.Exists(paths.Dat));
            Assert.True(Directory.Exists(paths.Logs));
            Assert.True(Directory.Exists(paths.Journal));
            Assert.True(Directory.Exists(paths.Quarantine));
        }
        finally
        {
            TempRoot.Delete(root);
        }
    }

    [Fact]
    public void Blank_instance_name_falls_back_to_default()
    {
        Assert.Equal(InstancePaths.DefaultInstanceName, new InstancePaths("   ").InstanceName);
        Assert.Equal(InstancePaths.DefaultInstanceName, new InstancePaths(null).InstanceName);
    }

    [Fact]
    public void Invalid_filename_characters_are_sanitized_out_of_the_instance_name()
    {
        var paths = new InstancePaths("bad:name/here");

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            Assert.DoesNotContain(invalid, paths.InstanceName);
        }
    }
}
