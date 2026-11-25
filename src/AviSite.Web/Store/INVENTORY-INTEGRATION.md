Inventory module integration (not wired into Program.cs in this isolated feature pass)

The Inventory feature is intentionally not registered in the production Program.cs by this isolated feature pass. The standalone end-to-end test copies the web project to a temporary fixture, injects `services.AddInventoryModule();` and `app.MapInventoryEndpoints();` into that copy only, then builds and runs it in Development against a disposable data directory and fresh SQLite database. It never edits the checkout's Program.cs or accesses production data.

Focused checks:

- `dotnet build src/AviSite.Web/AviSite.Web.csproj -c Release`
- `node tests/e2e/csp-lint.mjs`
- `node --check tests/e2e/inventory.mjs`
- `node tests/e2e/inventory.mjs`

The fixture exercises the real static SSR module and its registered endpoints. Production integration remains an explicit follow-up: add the two registration lines above to Program.cs when the site is ready to enable inventory.
