# Build the API reference

The searchable reference site is generated with the repository-pinned Docfx tool. Generation uses
the public `net10.0` Release surface and the XML documentation emitted beside each assembly. Public
API analyzers keep the surface aligned across the three supported target frameworks.

## Build locally

From the repository root:

```powershell
dotnet tool restore
./eng/verify-documentation.ps1
```

The consolidated script checks Markdown, snippets, links, and every public XML comment; regenerates
API metadata; and writes the static site to `artifacts/docs`. Open `artifacts/docs/index.html`
through a local HTTP server so search and navigation behave as they will when published.

After an existing Release build, skip the repeated solution build:

```powershell
./eng/verify-documentation.ps1 -NoBuild -WarningsAsErrors
```

Use `eng/build-documentation.ps1` directly only when iterating on Docfx output without the other
repository documentation checks.

## What is committed

Commit the Docfx configuration, navigation, package landing pages, and entry-point overrides.
Generated YAML and HTML remain ignored because CI regenerates them from the tagged source and XML
comments. This keeps each published site aligned with the source revision that produced it.

The `XBullet.EasyTesting.Snapshots` compatibility package is intentionally excluded from metadata
generation. It contains type forwarders rather than independent APIs, so its package page links to
the canonical Core and HTTP reference pages.

## Adding or moving public APIs

1. Add complete XML comments, including parameters, type parameters, return values, and readable
   property values.
2. Run `./eng/verify-xml-documentation.ps1`.
3. Update the appropriate package landing page when the API is a primary entry point.
4. Add or update an overwrite when the API should link directly to a conceptual guide.
5. Run `./eng/build-documentation.ps1 -WarningsAsErrors` and inspect the generated page.
