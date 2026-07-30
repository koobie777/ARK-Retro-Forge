using System.Diagnostics.CodeAnalysis;
using ARK.Core.Systems;

namespace ARK.Tests;

public sealed class SystemRegistryTests : IDisposable
{
    private readonly string _dir;

    public SystemRegistryTests()
    {
        _dir = TempRoot.Create();
    }

    public void Dispose()
    {
        TempRoot.Delete(_dir);
        GC.SuppressFinalize(this);
    }

    // Gate 3 — the registry loads from config/systems/*.json; a system added by config alone resolves.
    [Fact]
    [RequiresUnreferencedCode("Loads SystemRegistry.")]
    public void Loads_definitions_from_json_including_a_config_only_system()
    {
        File.WriteAllText(
            Path.Combine(_dir, "zzz.json"),
            """{ "code": "zzz", "displayName": "Zork System", "aliases": ["ZZZ"], "romExtensions": [".zz"] }""");

        var registry = SystemRegistry.Load(_dir);
        var zzz = registry.Resolve("zzz");

        Assert.NotNull(zzz);
        Assert.Equal("Zork System", zzz!.DisplayName);
        Assert.Contains(".zz", zzz.RomExtensions);
        Assert.Equal(zzz, registry.ResolveByAlias("ZZZ"));
    }

    // Gate 4 — an unrecognized code resolves to null; never substituted for another system.
    [Fact]
    [RequiresUnreferencedCode("Loads SystemRegistry.")]
    public void Unknown_code_resolves_to_null_without_fallback()
    {
        File.WriteAllText(Path.Combine(_dir, "n64.json"), """{ "code": "n64", "displayName": "Nintendo 64" }""");

        var registry = SystemRegistry.Load(_dir);

        Assert.NotNull(registry.Resolve("n64"));
        Assert.Null(registry.Resolve("psx"));
        Assert.Null(registry.Resolve("xyz"));
    }

    // Gate 5 — the shipped cartridge systems carry extensions, archive extensions, aliases, qualifiers.
    [Theory]
    [InlineData("n64", ".z64")]
    [InlineData("snes", ".sfc")]
    [InlineData("nes", ".nes")]
    [RequiresUnreferencedCode("Loads SystemRegistry.")]
    public void Shipped_cartridge_systems_are_fully_defined(string code, string romExtension)
    {
        var registry = SystemRegistry.Load(TestFixtures.ShippedSystemsDirectory());
        var system = registry.Resolve(code);

        Assert.NotNull(system);
        Assert.NotEmpty(system!.Aliases);
        Assert.Contains(romExtension, system.RomExtensions);
        Assert.NotEmpty(system.ArchiveExtensions);
        Assert.NotEmpty(system.FormatQualifiers);
    }
}
