using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryWorkspaceAndTwoStepTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_user_data_scopes_UserId_ScopeType",
                schema: "identity",
                table: "user_data_scopes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_identity_user_data_scope_v1",
                schema: "identity",
                table: "user_data_scopes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_inventory_source_type",
                schema: "inventory",
                table: "transactions");

            migrationBuilder.CreateSequence(
                name: "inventory_transfer_number_seq",
                schema: "inventory");

            migrationBuilder.AddColumn<string>(
                name: "AddressEn",
                schema: "inventory",
                table: "warehouses",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressFa",
                schema: "inventory",
                table: "warehouses",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PrimaryResponsibleUserId",
                schema: "inventory",
                table: "warehouses",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "transfers",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceWarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationWarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedQuantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    DispatchTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ActorIdentifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transfers", x => x.Id);
                    table.CheckConstraint("ck_inventory_transfer_cancel", "(\"CancelledAtUtc\" IS NULL) = (\"CancellationReason\" IS NULL)");
                    table.CheckConstraint("ck_inventory_transfer_number", "\"BusinessNumber\" ~ '^ITR-[0-9]{6,}$'");
                    table.CheckConstraint("ck_inventory_transfer_quantity", "\"RequestedQuantity\" > 0");
                    table.CheckConstraint("ck_inventory_transfer_status", "\"Status\" IN ('DRAFT','DISPATCHED','PARTIALLY_RECEIVED','RECEIVED','CANCELLED')");
                    table.CheckConstraint("ck_inventory_transfer_version", "\"Version\" >= 1");
                    table.CheckConstraint("ck_inventory_transfer_warehouses", "\"SourceWarehouseId\" <> \"DestinationWarehouseId\"");
                    table.ForeignKey(
                        name: "FK_transfers_items_ItemId",
                        column: x => x.ItemId,
                        principalSchema: "catalog",
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transfers_transactions_DispatchTransactionId",
                        column: x => x.DispatchTransactionId,
                        principalSchema: "inventory",
                        principalTable: "transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transfers_units_UnitId",
                        column: x => x.UnitId,
                        principalSchema: "reference_data",
                        principalTable: "units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transfers_warehouses_DestinationWarehouseId",
                        column: x => x.DestinationWarehouseId,
                        principalSchema: "inventory",
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transfers_warehouses_SourceWarehouseId",
                        column: x => x.SourceWarehouseId,
                        principalSchema: "inventory",
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "transfer_receipts",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransferId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ActorIdentifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    InventoryTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transfer_receipts", x => x.Id);
                    table.CheckConstraint("ck_inventory_transfer_receipt_quantity", "\"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_transfer_receipts_transactions_InventoryTransactionId",
                        column: x => x.InventoryTransactionId,
                        principalSchema: "inventory",
                        principalTable: "transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transfer_receipts_transfers_TransferId",
                        column: x => x.TransferId,
                        principalSchema: "inventory",
                        principalTable: "transfers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_warehouses_PrimaryResponsibleUserId",
                schema: "inventory",
                table: "warehouses",
                column: "PrimaryResponsibleUserId");

            migrationBuilder.CreateIndex(
                name: "IX_user_data_scopes_UserId",
                schema: "identity",
                table: "user_data_scopes",
                column: "UserId",
                unique: true,
                filter: "\"ScopeType\" = 'ALL'");

            migrationBuilder.CreateIndex(
                name: "IX_user_data_scopes_UserId_ScopeType_ScopeValue",
                schema: "identity",
                table: "user_data_scopes",
                columns: new[] { "UserId", "ScopeType", "ScopeValue" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_identity_user_data_scope_v1",
                schema: "identity",
                table: "user_data_scopes",
                sql: "(\"ScopeType\" = 'ALL' AND \"ScopeValue\" IS NULL) OR (\"ScopeType\" = 'WAREHOUSE' AND \"ScopeValue\" ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_inventory_source_type",
                schema: "inventory",
                table: "transactions",
                sql: "\"SourceDocumentType\" IS NULL OR \"SourceDocumentType\" IN ('PURCHASE_RECEIPT','PRODUCTION_ISSUE','INTERNAL_TRANSFER_DISPATCH','INTERNAL_TRANSFER_RECEIPT','LEGACY_IMPORT')");

            migrationBuilder.CreateIndex(
                name: "IX_transfer_receipts_InventoryTransactionId",
                schema: "inventory",
                table: "transfer_receipts",
                column: "InventoryTransactionId",
                unique: true,
                filter: "\"InventoryTransactionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_transfer_receipts_TransferId_EffectiveDate",
                schema: "inventory",
                table: "transfer_receipts",
                columns: new[] { "TransferId", "EffectiveDate" });

            migrationBuilder.CreateIndex(
                name: "IX_transfers_BusinessNumber",
                schema: "inventory",
                table: "transfers",
                column: "BusinessNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_transfers_DestinationWarehouseId",
                schema: "inventory",
                table: "transfers",
                column: "DestinationWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_transfers_DispatchTransactionId",
                schema: "inventory",
                table: "transfers",
                column: "DispatchTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_transfers_ItemId_CreatedAtUtc",
                schema: "inventory",
                table: "transfers",
                columns: new[] { "ItemId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_transfers_SourceWarehouseId_DestinationWarehouseId_CreatedA~",
                schema: "inventory",
                table: "transfers",
                columns: new[] { "SourceWarehouseId", "DestinationWarehouseId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_transfers_Status_CreatedAtUtc",
                schema: "inventory",
                table: "transfers",
                columns: new[] { "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_transfers_UnitId",
                schema: "inventory",
                table: "transfers",
                column: "UnitId");

            migrationBuilder.AddForeignKey(
                name: "FK_warehouses_users_PrimaryResponsibleUserId",
                schema: "inventory",
                table: "warehouses",
                column: "PrimaryResponsibleUserId",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION identity.guard_warehouse_data_scope()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW."ScopeType" = 'WAREHOUSE' AND NOT EXISTS (
                        SELECT 1 FROM inventory.warehouses w
                        WHERE w."Id" = NEW."ScopeValue"::uuid
                    ) THEN
                        RAISE EXCEPTION 'WAREHOUSE_SCOPE_NOT_FOUND' USING ERRCODE = '23514',
                            CONSTRAINT = 'ck_identity_warehouse_scope_exists';
                    END IF;
                    RETURN NEW;
                END $$;

                CREATE TRIGGER trg_identity_warehouse_data_scope
                BEFORE INSERT OR UPDATE ON identity.user_data_scopes
                FOR EACH ROW EXECUTE FUNCTION identity.guard_warehouse_data_scope();

                CREATE OR REPLACE FUNCTION inventory.guard_transfer_row()
                RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE dispatch_row inventory.transactions%ROWTYPE;
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'TRANSFER_HISTORY_IMMUTABLE' USING ERRCODE = '23514',
                            CONSTRAINT = 'ck_inventory_transfer_immutable';
                    END IF;
                    IF TG_OP = 'INSERT' THEN
                        IF NEW."Status" <> 'DRAFT' OR NEW."DispatchTransactionId" IS NOT NULL OR
                           NEW."CancelledAtUtc" IS NOT NULL THEN
                            RAISE EXCEPTION 'TRANSFER_MUST_START_DRAFT' USING ERRCODE = '23514',
                                CONSTRAINT = 'ck_inventory_transfer_lifecycle';
                        END IF;
                        RETURN NEW;
                    END IF;
                    IF NEW."BusinessNumber" <> OLD."BusinessNumber" OR
                       NEW."CreatedAtUtc" <> OLD."CreatedAtUtc" OR
                       NEW."ActorIdentifier" IS DISTINCT FROM OLD."ActorIdentifier" OR
                       NEW."Version" <> OLD."Version" + 1 THEN
                        RAISE EXCEPTION 'TRANSFER_IDENTITY_IMMUTABLE' USING ERRCODE = '23514',
                            CONSTRAINT = 'ck_inventory_transfer_immutable';
                    END IF;
                    IF OLD."Status" <> 'DRAFT' AND ROW(NEW."ItemId", NEW."UnitId",
                        NEW."SourceWarehouseId", NEW."DestinationWarehouseId", NEW."RequestedQuantity", NEW."Reason")
                        IS DISTINCT FROM ROW(OLD."ItemId", OLD."UnitId", OLD."SourceWarehouseId",
                        OLD."DestinationWarehouseId", OLD."RequestedQuantity", OLD."Reason") THEN
                        RAISE EXCEPTION 'DISPATCHED_TRANSFER_COMMERCIAL_FIELDS_IMMUTABLE' USING ERRCODE = '23514',
                            CONSTRAINT = 'ck_inventory_transfer_immutable';
                    END IF;
                    IF NOT ((OLD."Status" = 'DRAFT' AND NEW."Status" IN ('DRAFT','DISPATCHED','CANCELLED')) OR
                            (OLD."Status" = 'DISPATCHED' AND NEW."Status" IN ('DISPATCHED','PARTIALLY_RECEIVED','RECEIVED','CANCELLED')) OR
                            (OLD."Status" = 'PARTIALLY_RECEIVED' AND NEW."Status" IN ('DISPATCHED','PARTIALLY_RECEIVED','RECEIVED')) OR
                            (OLD."Status" = 'RECEIVED' AND NEW."Status" IN ('DISPATCHED','PARTIALLY_RECEIVED','RECEIVED')) OR
                            (OLD."Status" = 'CANCELLED' AND NEW."Status" = 'CANCELLED')) THEN
                        RAISE EXCEPTION 'INVALID_TRANSFER_TRANSITION' USING ERRCODE = '23514',
                            CONSTRAINT = 'ck_inventory_transfer_lifecycle';
                    END IF;
                    IF OLD."Status" = 'CANCELLED' AND NEW IS DISTINCT FROM OLD THEN
                        RAISE EXCEPTION 'CANCELLED_TRANSFER_IMMUTABLE' USING ERRCODE = '23514',
                            CONSTRAINT = 'ck_inventory_transfer_immutable';
                    END IF;
                    IF NEW."Status" IN ('DISPATCHED','PARTIALLY_RECEIVED','RECEIVED') THEN
                        IF NEW."DispatchTransactionId" IS NULL THEN
                            RAISE EXCEPTION 'TRANSFER_DISPATCH_REQUIRED' USING ERRCODE = '23514',
                                CONSTRAINT = 'ck_inventory_transfer_dispatch_link';
                        END IF;
                        SELECT * INTO dispatch_row FROM inventory.transactions
                        WHERE "Id" = NEW."DispatchTransactionId";
                        IF NOT FOUND OR dispatch_row."TransactionType" <> 'ISSUE' OR
                           dispatch_row."ItemId" <> NEW."ItemId" OR
                           dispatch_row."WarehouseId" <> NEW."SourceWarehouseId" OR
                           dispatch_row."UnitId" <> NEW."UnitId" OR
                           dispatch_row."Quantity" <> NEW."RequestedQuantity" OR
                           dispatch_row."SourceDocumentType" <> 'INTERNAL_TRANSFER_DISPATCH' OR
                           dispatch_row."SourceDocumentId" <> NEW."Id" THEN
                            RAISE EXCEPTION 'INVALID_TRANSFER_DISPATCH_LINK' USING ERRCODE = '23514',
                                CONSTRAINT = 'ck_inventory_transfer_dispatch_link';
                        END IF;
                    END IF;
                    RETURN NEW;
                END $$;

                CREATE TRIGGER trg_inventory_transfer_row
                BEFORE INSERT OR UPDATE OR DELETE ON inventory.transfers
                FOR EACH ROW EXECUTE FUNCTION inventory.guard_transfer_row();

                CREATE OR REPLACE FUNCTION inventory.guard_transfer_receipt()
                RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE transfer_row inventory.transfers%ROWTYPE;
                DECLARE transaction_row inventory.transactions%ROWTYPE;
                DECLARE effective_received numeric(18,6);
                DECLARE dispatch_date date;
                BEGIN
                    IF TG_OP <> 'INSERT' THEN
                        RAISE EXCEPTION 'TRANSFER_RECEIPT_IMMUTABLE' USING ERRCODE = '23514',
                            CONSTRAINT = 'ck_inventory_transfer_receipt_immutable';
                    END IF;
                    SELECT * INTO transfer_row FROM inventory.transfers WHERE "Id" = NEW."TransferId";
                    IF NOT FOUND THEN RETURN NEW; END IF;
                    PERFORM inventory.lock_item_inventory(transfer_row."ItemId");
                    PERFORM inventory.lock_warehouse_inventory(lock_id)
                    FROM (VALUES (transfer_row."SourceWarehouseId"),
                                 (transfer_row."DestinationWarehouseId")) AS locks(lock_id)
                    ORDER BY lock_id;
                    PERFORM 1 FROM inventory.transfers WHERE "Id" = NEW."TransferId" FOR UPDATE;
                    IF transfer_row."Status" NOT IN ('DISPATCHED','PARTIALLY_RECEIVED','RECEIVED') THEN
                        RAISE EXCEPTION 'TRANSFER_NOT_RECEIVABLE' USING ERRCODE = '23514',
                            CONSTRAINT = 'ck_inventory_transfer_receivable';
                    END IF;
                    SELECT * INTO transaction_row FROM inventory.transactions
                    WHERE "Id" = NEW."InventoryTransactionId";
                    IF NOT FOUND OR transaction_row."TransactionType" <> 'RECEIPT' OR
                       transaction_row."ItemId" <> transfer_row."ItemId" OR
                       transaction_row."WarehouseId" <> transfer_row."DestinationWarehouseId" OR
                       transaction_row."UnitId" <> transfer_row."UnitId" OR
                       transaction_row."Quantity" <> NEW."Quantity" OR
                       transaction_row."EffectiveDate" <> NEW."EffectiveDate" OR
                       transaction_row."SourceDocumentType" <> 'INTERNAL_TRANSFER_RECEIPT' OR
                       transaction_row."SourceDocumentId" <> NEW."Id" THEN
                        RAISE EXCEPTION 'INVALID_TRANSFER_RECEIPT_LINK' USING ERRCODE = '23514',
                            CONSTRAINT = 'ck_inventory_transfer_receipt_link';
                    END IF;
                    SELECT "EffectiveDate" INTO dispatch_date
                    FROM inventory.transactions
                    WHERE "Id" = transfer_row."DispatchTransactionId";
                    IF dispatch_date IS NULL OR NEW."EffectiveDate" < dispatch_date THEN
                        RAISE EXCEPTION 'TRANSFER_RECEIPT_BEFORE_DISPATCH' USING ERRCODE = '23514',
                            CONSTRAINT = 'ck_inventory_transfer_receipt_date';
                    END IF;
                    SELECT COALESCE(SUM(r."Quantity"), 0) INTO effective_received
                    FROM inventory.transfer_receipts r
                    WHERE r."TransferId" = NEW."TransferId" AND NOT EXISTS (
                        SELECT 1 FROM inventory.transactions reversal
                        WHERE reversal."OriginalTransactionId" = r."InventoryTransactionId"
                    );
                    IF effective_received + NEW."Quantity" > transfer_row."RequestedQuantity" THEN
                        RAISE EXCEPTION 'TRANSFER_OVER_RECEIPT' USING ERRCODE = '23514',
                            CONSTRAINT = 'ck_inventory_transfer_over_receipt';
                    END IF;
                    RETURN NEW;
                END $$;

                CREATE TRIGGER trg_inventory_transfer_receipt
                BEFORE INSERT OR UPDATE OR DELETE ON inventory.transfer_receipts
                FOR EACH ROW EXECUTE FUNCTION inventory.guard_transfer_receipt();

                CREATE OR REPLACE FUNCTION inventory.validate_transfer_transaction_link()
                RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE original_row inventory.transactions%ROWTYPE;
                DECLARE effective_received numeric(18,6);
                DECLARE expected_status text;
                DECLARE transfer_id uuid;
                BEGIN
                    IF NEW."TransactionType" = 'TRANSFER' AND
                       NEW."SourceDocumentType" IS DISTINCT FROM 'LEGACY_IMPORT' THEN
                        RAISE EXCEPTION 'USE_TRANSFER_WORKFLOW' USING ERRCODE = '23514',
                            CONSTRAINT = 'ck_inventory_transfer_workflow_required';
                    END IF;
                    IF NEW."SourceDocumentType" = 'INTERNAL_TRANSFER_DISPATCH' AND NOT EXISTS (
                        SELECT 1 FROM inventory.transfers t WHERE t."Id" = NEW."SourceDocumentId"
                        AND t."DispatchTransactionId" = NEW."Id"
                    ) THEN
                        RAISE EXCEPTION 'ORPHAN_TRANSFER_DISPATCH' USING ERRCODE = '23514',
                            CONSTRAINT = 'ck_inventory_transfer_dispatch_link';
                    END IF;
                    IF NEW."SourceDocumentType" = 'INTERNAL_TRANSFER_RECEIPT' AND NOT EXISTS (
                        SELECT 1 FROM inventory.transfer_receipts r WHERE r."Id" = NEW."SourceDocumentId"
                        AND r."InventoryTransactionId" = NEW."Id"
                    ) THEN
                        RAISE EXCEPTION 'ORPHAN_TRANSFER_RECEIPT' USING ERRCODE = '23514',
                            CONSTRAINT = 'ck_inventory_transfer_receipt_link';
                    END IF;
                    IF NEW."SourceDocumentType" = 'INTERNAL_TRANSFER_RECEIPT' THEN
                        SELECT r."TransferId" INTO transfer_id
                        FROM inventory.transfer_receipts r
                        WHERE r."InventoryTransactionId" = NEW."Id";
                        SELECT COALESCE(SUM(r."Quantity"), 0) INTO effective_received
                        FROM inventory.transfer_receipts r
                        WHERE r."TransferId" = transfer_id AND NOT EXISTS (
                            SELECT 1 FROM inventory.transactions reversal
                            WHERE reversal."OriginalTransactionId" = r."InventoryTransactionId"
                        );
                        SELECT CASE WHEN effective_received <= 0 THEN 'DISPATCHED'
                            WHEN effective_received >= t."RequestedQuantity" THEN 'RECEIVED'
                            ELSE 'PARTIALLY_RECEIVED' END INTO expected_status
                        FROM inventory.transfers t WHERE t."Id" = transfer_id;
                        IF NOT EXISTS (SELECT 1 FROM inventory.transfers t
                            WHERE t."Id" = transfer_id AND t."Status" = expected_status) THEN
                            RAISE EXCEPTION 'TRANSFER_RECEIPT_STATUS_MISMATCH' USING ERRCODE = '23514',
                                CONSTRAINT = 'ck_inventory_transfer_receipt_link';
                        END IF;
                    END IF;
                    IF NEW."TransactionType" = 'REVERSAL' THEN
                        SELECT * INTO original_row FROM inventory.transactions
                        WHERE "Id" = NEW."OriginalTransactionId";
                        IF original_row."SourceDocumentType" = 'INTERNAL_TRANSFER_DISPATCH' AND NOT EXISTS (
                            SELECT 1 FROM inventory.transfers t WHERE t."Id" = original_row."SourceDocumentId"
                            AND t."Status" = 'CANCELLED'
                        ) THEN
                            RAISE EXCEPTION 'TRANSFER_DISPATCH_REVERSAL_REQUIRES_CANCEL' USING ERRCODE = '23514',
                                CONSTRAINT = 'ck_inventory_transfer_reversal_link';
                        END IF;
                        IF original_row."SourceDocumentType" = 'INTERNAL_TRANSFER_RECEIPT' THEN
                            SELECT r."TransferId" INTO transfer_id FROM inventory.transfer_receipts r
                            WHERE r."InventoryTransactionId" = original_row."Id";
                            SELECT COALESCE(SUM(r."Quantity"), 0) INTO effective_received
                            FROM inventory.transfer_receipts r
                            WHERE r."TransferId" = transfer_id AND NOT EXISTS (
                                SELECT 1 FROM inventory.transactions reversal
                                WHERE reversal."OriginalTransactionId" = r."InventoryTransactionId"
                            );
                            SELECT CASE WHEN effective_received <= 0 THEN 'DISPATCHED'
                                WHEN effective_received >= t."RequestedQuantity" THEN 'RECEIVED'
                                ELSE 'PARTIALLY_RECEIVED' END INTO expected_status
                            FROM inventory.transfers t WHERE t."Id" = transfer_id;
                            IF NOT EXISTS (SELECT 1 FROM inventory.transfers t
                                WHERE t."Id" = transfer_id AND t."Status" = expected_status) THEN
                                RAISE EXCEPTION 'TRANSFER_RECEIPT_REVERSAL_STATUS_MISMATCH' USING ERRCODE = '23514',
                                    CONSTRAINT = 'ck_inventory_transfer_reversal_link';
                            END IF;
                        END IF;
                    END IF;
                    RETURN NEW;
                END $$;

                CREATE CONSTRAINT TRIGGER trg_inventory_transfer_transaction_link
                AFTER INSERT ON inventory.transactions DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW EXECUTE FUNCTION inventory.validate_transfer_transaction_link();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_inventory_transfer_transaction_link ON inventory.transactions;
                DROP FUNCTION IF EXISTS inventory.validate_transfer_transaction_link();
                DROP TRIGGER IF EXISTS trg_inventory_transfer_receipt ON inventory.transfer_receipts;
                DROP FUNCTION IF EXISTS inventory.guard_transfer_receipt();
                DROP TRIGGER IF EXISTS trg_inventory_transfer_row ON inventory.transfers;
                DROP FUNCTION IF EXISTS inventory.guard_transfer_row();
                DROP TRIGGER IF EXISTS trg_identity_warehouse_data_scope ON identity.user_data_scopes;
                DROP FUNCTION IF EXISTS identity.guard_warehouse_data_scope();
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_warehouses_users_PrimaryResponsibleUserId",
                schema: "inventory",
                table: "warehouses");

            migrationBuilder.DropTable(
                name: "transfer_receipts",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "transfers",
                schema: "inventory");

            migrationBuilder.DropIndex(
                name: "IX_warehouses_PrimaryResponsibleUserId",
                schema: "inventory",
                table: "warehouses");

            migrationBuilder.DropIndex(
                name: "IX_user_data_scopes_UserId",
                schema: "identity",
                table: "user_data_scopes");

            migrationBuilder.DropIndex(
                name: "IX_user_data_scopes_UserId_ScopeType_ScopeValue",
                schema: "identity",
                table: "user_data_scopes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_identity_user_data_scope_v1",
                schema: "identity",
                table: "user_data_scopes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_inventory_source_type",
                schema: "inventory",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "AddressEn",
                schema: "inventory",
                table: "warehouses");

            migrationBuilder.DropColumn(
                name: "AddressFa",
                schema: "inventory",
                table: "warehouses");

            migrationBuilder.DropColumn(
                name: "PrimaryResponsibleUserId",
                schema: "inventory",
                table: "warehouses");

            migrationBuilder.DropSequence(
                name: "inventory_transfer_number_seq",
                schema: "inventory");

            migrationBuilder.CreateIndex(
                name: "IX_user_data_scopes_UserId_ScopeType",
                schema: "identity",
                table: "user_data_scopes",
                columns: new[] { "UserId", "ScopeType" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_identity_user_data_scope_v1",
                schema: "identity",
                table: "user_data_scopes",
                sql: "\"ScopeType\" = 'ALL' AND \"ScopeValue\" IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_inventory_source_type",
                schema: "inventory",
                table: "transactions",
                sql: "\"SourceDocumentType\" IS NULL OR \"SourceDocumentType\" IN ('PURCHASE_RECEIPT','PRODUCTION_ISSUE','LEGACY_IMPORT')");
        }
    }
}
