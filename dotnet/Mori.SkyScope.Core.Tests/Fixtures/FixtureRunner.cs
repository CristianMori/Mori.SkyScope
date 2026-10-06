// Mori.SkyScope — Adapts one core module to the generic fixture format in spec/fixtures/*.json.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mori.SkyScope.Core.Tests.Fixtures;

/// <summary>
/// Adapts one core module to the generic fixture format in <c>spec/fixtures/*.json</c>.
/// The TS side has the identical interface in <c>ts/packages/core/src/fixtures/drivers.ts</c>.
/// </summary>
public interface IFixtureDriver
{
    /// <summary>The <c>component</c> value of the fixture files this driver handles.</summary>
    string Component { get; }
    /// <summary>Builds the initial state from the <c>setup</c> object of a case (an empty object when the case has none).</summary>
    object Create(JsonElement setup);
    /// <summary>Applies one step to the state and returns the state to continue with; query steps append their answers to the state.</summary>
    object Step(object state, JsonElement step);
    /// <summary>Everything a fixture may assert on, as camelCase JSON.</summary>
    JsonNode Snapshot(object state);
}

/// <summary>One case from a fixture file: its component, name, setup, steps and the expected snapshot properties.</summary>
public sealed record FixtureCase(string Component, string Name, JsonElement Setup, JsonElement[] Steps, JsonElement Expect)
{
    /// <summary>Formats as <c>component: name</c> for the test explorer.</summary>
    public override string ToString() => $"{Component}: {Name}";
}

/// <summary>Locates and loads every fixture case and runs one case against a driver.</summary>
public static class Fixtures
{
    /// <summary>Absolute path of <c>spec/fixtures</c>, found by walking up from the test output directory.</summary>
    public static readonly string Directory = Locate();

    /// <summary>Every case of every fixture file, in file-name then declaration order.</summary>
    public static IReadOnlyList<FixtureCase> All { get; } = Load();

    /// <summary>Web defaults (camelCase, case-insensitive) used by every driver to serialize snapshots.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static string Locate()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "spec", "fixtures");
            if (System.IO.Directory.Exists(candidate)) return candidate;
        }
        throw new InvalidOperationException("Could not find spec/fixtures above " + AppContext.BaseDirectory);
    }

    private static List<FixtureCase> Load()
    {
        var cases = new List<FixtureCase>();
        var empty = JsonDocument.Parse("{}").RootElement.Clone();
        foreach (var file in System.IO.Directory.GetFiles(Directory, "*.json").Order())
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var root = doc.RootElement.Clone();
            var component = root.GetProperty("component").GetString()!;
            foreach (var c in root.GetProperty("cases").EnumerateArray())
            {
                var setup = c.TryGetProperty("setup", out var s) ? s : empty;
                var steps = c.TryGetProperty("steps", out var st) ? st.EnumerateArray().ToArray() : [];
                cases.Add(new FixtureCase(component, c.GetProperty("name").GetString()!, setup, steps, c.GetProperty("expect")));
            }
        }
        return cases;
    }

    /// <summary>Creates the state, applies every step and compares each expected property with the snapshot by deep JSON equality, failing with both values and the full snapshot.</summary>
    public static void Run(FixtureCase fixture, IFixtureDriver driver)
    {
        var state = driver.Create(fixture.Setup);
        foreach (var step in fixture.Steps) state = driver.Step(state, step);
        // Round-trip through text so both sides are JsonElement-backed and numbers compare numerically (1 == 1.0).
        var actual = JsonNode.Parse(driver.Snapshot(state).ToJsonString())!;

        foreach (var expected in fixture.Expect.EnumerateObject())
        {
            var actualValue = actual[expected.Name];
            var expectedNode = JsonNode.Parse(expected.Value.GetRawText());
            if (!JsonNode.DeepEquals(expectedNode, actualValue))
                throw new Xunit.Sdk.XunitException(
                    $"[{fixture}] property \"{expected.Name}\"\n  expected: {expectedNode?.ToJsonString() ?? "null"}\n  actual:   {actualValue?.ToJsonString() ?? "null"}\n  snapshot: {actual.ToJsonString()}");
        }
    }
}

/// <summary>Runs every fixture case through its C# driver; the TS test suite runs the same files.</summary>
public class FixtureTests
{
    private static readonly Dictionary<string, IFixtureDriver> Drivers =
        new IFixtureDriver[] { new FieldDriver(), new SignalBufferDriver(), new DecimationDriver(), new ScaleDriver(), new SignalStoreDriver(), new SynthDriver(), new PaintDriver(), new GeometryDriver(), new CameraDriver(), new InteractionDriver(), new SceneDriver(), new ClipDriver(), new TrendDriver(), new SignalTreeDriver(), new GaugeDriver(), new CartesianDriver(), new PieDriver(), new PolarDriver(), new HeatmapDriver(), new RobotLayersDriver(), new SceneControllerDriver(), new PlaybackDriver(), new MqttMappingDriver(), new McapDriver(), new Math3Driver(), new Camera3DDriver(), new FrameTreeDriver(), new LayerMessageDriver(), new Scene3DDriver(), new Scene3DControllerDriver(), new MeshFormatsDriver(), new UrdfDriver() }.ToDictionary(d => d.Component);

    /// <summary>Every fixture case as theory data.</summary>
    public static TheoryData<FixtureCase> Cases()
    {
        var data = new TheoryData<FixtureCase>();
        foreach (var c in Fixtures.All) data.Add(c);
        return data;
    }

    /// <summary>The driver for the component of the case must exist and the expectations of the case must hold.</summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void Fixture(FixtureCase fixture)
    {
        Assert.True(Drivers.TryGetValue(fixture.Component, out var driver), $"No C# fixture driver registered for component \"{fixture.Component}\"");
        Fixtures.Run(fixture, driver!);
    }

    /// <summary>Every component named in a fixture file has a registered driver, so no fixture goes silently untested.</summary>
    [Fact]
    public void Every_fixture_component_has_a_driver()
    {
        var missing = Fixtures.All.Select(c => c.Component).Distinct().Where(c => !Drivers.ContainsKey(c)).ToList();
        Assert.Empty(missing);
    }
}
