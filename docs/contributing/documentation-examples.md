# Add executable documentation examples

Documentation examples are product behavior. Their canonical source lives in compiled test files,
and Markdown contains generated copies of named source regions. Do not maintain the same C# example
independently in source and Markdown.

## Where examples live

Use clearly named `DocumentationExamples.cs` files in the existing integration-test project that
owns the demonstrated behavior. This keeps examples close to realistic applications and ensures the
solution's normal multi-target test run executes them on .NET 8, .NET 9, and .NET 10.

The initial examples live in
[`DocumentationExamples.cs`](../../tests/TestApi.IntegrationTests/DocumentationExamples.cs).
Reuse the existing sample applications and fixtures when they remain easy to explain. Add a smaller
application or fixture only when unrelated setup would obscure the feature being taught.

The [complete order workflow](../guides/production-workflows.md) uses
[order-workflow examples](../../tests/TestStartupApi.IntegrationTests/DocumentationExamples.cs)
and the named factory region in
[`StartupApiFactory.cs`](../../tests/TestStartupApi.IntegrationTests/StartupApiFactory.cs).
These examples demonstrate composed application behavior and its failure paths without external
infrastructure.

The [end-to-end flow guide](../guides/end-to-end-flows.md) composes existing canonical regions from
the controller, HTTP, messaging, authentication, snapshot, Functions, and Aspire test classes. Keep
these snippets tied to their owning tests instead of creating duplicate implementations for the guide.

The [Durable orchestration recipes](../guides/azure-functions-durable-recipes.md) use
[`DurableWorkflowExamples.cs`](../../tests/TestFunctions.IntegrationTests/DurableWorkflowExamples.cs)
for partial batch failures, approval races, isolated child contexts, and explicitly driven continuations.

## Add a source region

Wrap a complete, focused example in a uniquely named region:

```csharp
#region docs-feature-name
[Fact]
public async Task Feature_behaves_as_documented()
{
    // Arrange, act, assert, and dispose owned resources.
}
#endregion
```

Region names use the `docs-` prefix and lowercase hyphenated words. A snippet should show the
complete lifecycle needed to understand the feature and should remain readable outside its source
file. Prefer one behavior per region.

## Add the Markdown marker

Reference the repository-relative source path and region immediately above a fenced block:

````markdown
&lt;!-- snippet: tests/ExampleProject/DocumentationExamples.cs#docs-feature-name --&gt;
```csharp
generated content
```
&lt;!-- end-snippet --&gt;
````

The outer fence in the example above is illustrative. In a real page, use the marker, one C# fence,
and the end marker exactly as shown by existing pages.

## Synchronize and verify snippets

Regenerate marked blocks after changing canonical source:

```powershell
./eng/sync-documentation-snippets.ps1
```

Run the same non-mutating check used by CI:

```powershell
./eng/sync-documentation-snippets.ps1 -Check
```

Check mode returns a nonzero exit code and reports `DOCSNIP001` when generated Markdown differs from
its source region. It also fails for a missing source file, missing region, duplicate region, or
source path outside the repository.

## Run canonical examples

Run the documentation examples on every supported target framework:

```shell
dotnet test tests/TestApi.IntegrationTests/TestApi.IntegrationTests.csproj --framework net8.0 --filter-class TestApi.IntegrationTests.MinimalControllerDocumentationExample --filter-class TestApi.IntegrationTests.RealisticControllerDocumentationExample --filter-class TestApi.IntegrationTests.FailureDiagnosticsDocumentationExample
dotnet test tests/TestApi.IntegrationTests/TestApi.IntegrationTests.csproj --framework net9.0 --filter-class TestApi.IntegrationTests.MinimalControllerDocumentationExample --filter-class TestApi.IntegrationTests.RealisticControllerDocumentationExample --filter-class TestApi.IntegrationTests.FailureDiagnosticsDocumentationExample
dotnet test tests/TestApi.IntegrationTests/TestApi.IntegrationTests.csproj --framework net10.0 --filter-class TestApi.IntegrationTests.MinimalControllerDocumentationExample --filter-class TestApi.IntegrationTests.RealisticControllerDocumentationExample --filter-class TestApi.IntegrationTests.FailureDiagnosticsDocumentationExample
```

The regular solution test job already runs the complete multi-target integration-test project. The
focused commands are useful while editing examples.

Run all five order-workflow cases on each supported framework:

```shell
dotnet test tests/TestStartupApi.IntegrationTests/TestStartupApi.IntegrationTests.csproj --framework net8.0 --filter-class TestStartupApi.IntegrationTests.OrderWorkflowDocumentationExamples
dotnet test tests/TestStartupApi.IntegrationTests/TestStartupApi.IntegrationTests.csproj --framework net9.0 --filter-class TestStartupApi.IntegrationTests.OrderWorkflowDocumentationExamples
dotnet test tests/TestStartupApi.IntegrationTests/TestStartupApi.IntegrationTests.csproj --framework net10.0 --filter-class TestStartupApi.IntegrationTests.OrderWorkflowDocumentationExamples
```

## Review checklist

- [ ] The source region has a unique `docs-` name.
- [ ] The example compiles and executes as a test.
- [ ] It includes relevant setup, action, assertion, ownership, and cleanup.
- [ ] Values are deterministic and contain no credentials or personal data.
- [ ] Failure text asserts only stable diagnostic fields.
- [ ] The Markdown block was regenerated rather than copied manually.
- [ ] Snippet check and all affected target-framework tests pass.
