---
name: wf-tests
description: Choose, write, and run tests for Wayfarer changes — Content.Tests unit tests and Content.IntegrationTests server/client tests with PoolManager. Use when adding a test, deciding which tests to run, or investigating a CI test failure.
license: AGPL-3.0-only
metadata:
  author: Wayfarer 14 contributors
  website: https://wayfarer14.com
  repository: https://gitlab.com/projectwayfarer/wayfarer14
  version: "1.0"
---

# Tests

## Which project

| Project | Use for | Speed |
|---|---|---|
| `Content.Tests` | Pure logic that does not need a running game: parsing, math, data helpers | Fast |
| `Content.IntegrationTests` | Anything involving entities, prototypes, systems, maps, or client/server interaction | Slow (boots a server, often a client) |

Frontier's tests live in `Content.IntegrationTests/Tests/_NF/`. Put Wayfarer tests in `Content.IntegrationTests/Tests/_WF/` (create it if needed), namespace `Content.IntegrationTests.Tests._WF`.

## Integration test shape

`Content.IntegrationTests/Tests/_NF/IllegalComponentTest.cs` is a short, complete example.

```cs
[TestFixture]
public sealed class ExampleTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: ExampleTestDummy
  components:
  - type: Example
";

    [Test]
    public async Task ExampleDoesThing()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var uid = entMan.SpawnEntity("ExampleTestDummy", MapCoordinates.Nullspace);
            Assert.That(entMan.HasComponent<ExampleComponent>(uid));
        });

        await pair.CleanReturnAsync();
    }
}
```

- Always end with `await pair.CleanReturnAsync()` so the pooled server can be reused.
- Touch entities only inside `WaitPost` / `WaitAssertion`; use `RunTicks` / `WaitRunTicks` to let systems process.
- Use `[TestPrototypes]` for test-only entities rather than depending on real game content that may change.
- Request special settings through `PoolSettings` (for example `Connected = true` when a client must be attached) only when the test needs them.

## Running tests

```sh
dotnet build --configuration DebugOpt --no-restore /m
dotnet test --no-build --configuration DebugOpt Content.Tests/Content.Tests.csproj -- NUnit.ConsoleOut=0
dotnet test --no-build --configuration DebugOpt Content.IntegrationTests/Content.IntegrationTests.csproj --filter "FullyQualifiedName~ExampleTest" -- NUnit.ConsoleOut=0 NUnit.MapWarningTo=Failed
```

Use `--filter` while iterating; run the full integration suite before submitting changes to prototypes, maps, or widely used systems. CI runs the shipyard tests as a separate job.

## Common CI failures

- **Prototype or YAML errors**: an unknown component, bad field, or missing parent. Run the YAML linter locally.
- **Map warnings promoted to failures** (`NUnit.MapWarningTo=Failed`): usually a renamed or removed prototype still used in a map.
- **Duplicate locale keys** or missing `Loc` keys.
- **Shipyard tests**: a vessel prototype pointing at a missing or broken map.
