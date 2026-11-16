# Inventory demo seed

On Windows, start Hadron first (`run-hadron.cmd`), then run `seed-inventory.cmd` from the repository root. The script uses the local API and prompts for a Hadron account when `HADRON_SEED_USERNAME` and `HADRON_SEED_PASSWORD` are not set. Override the API with `-ApiBaseUrl`, or the JSON with `-DataFile`.

The JSON dataset is UTF-8 and contains Persian and English demo names. The PowerShell source is ASCII-only for Windows PowerShell 5.1 compatibility. Seeding uses authenticated API calls; it does not write directly to PostgreSQL.

The seed creates or reuses reference units and values, products/parts/components, five warehouses, item-specific unit conversions, inventory opening/receipt/issue/adjustment/reversal transactions, and seven transfers across draft, dispatched, partially received, received, reversed-receipt, and cancelled states. It uses the first active user returned by the Inventory responsible-users API for newly created warehouses. The seed posts through authenticated API endpoints and does not write directly to PostgreSQL.

Records are tagged in their reasons with `[INV-SEED:...]` markers so reruns reuse existing transactions and transfers and resume unfinished transfer transitions. Dates are anchored to the server's Tehran business date; the dataset is designed to leave non-negative running balances. Run only against a disposable/demo company database. The user must have permissions to manage reference/catalog data, warehouses, unit conversions, transactions, reversals, and transfers, and must have access to all warehouses.
