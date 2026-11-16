# Inventory demo seed

On Windows, start Hadron first (`run-hadron.cmd`), then run `seed-inventory.cmd` from the repository root. The script uses the local API and prompts for a Hadron account when `HADRON_SEED_USERNAME` and `HADRON_SEED_PASSWORD` are not set. Override the API with `-ApiBaseUrl`, or the JSON with `-DataFile`.

The JSON dataset is UTF-8 and contains Persian and English demo names. The PowerShell source is ASCII-only for Windows PowerShell 5.1 compatibility. Seeding uses authenticated API calls; it does not write directly to PostgreSQL.

The current script creates/reuses reference units and values and the product/part/component catalog prerequisites. It does not yet post Inventory warehouse, conversion, transaction, or transfer records; those depend on Inventory-specific permissions and API lifecycle actions and are intentionally not silently fabricated. Run only against a disposable/demo company database. Re-running is intended to reuse catalog records by their names/codes.
