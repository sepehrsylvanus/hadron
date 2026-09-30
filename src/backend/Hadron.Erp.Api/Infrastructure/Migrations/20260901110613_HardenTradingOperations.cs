using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HardenTradingOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc",
                schema: "trading",
                table: "project_shipments",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<long>(
                name: "Version",
                schema: "trading",
                table: "project_shipments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddCheckConstraint(
                name: "ck_trade_shipment_actual_dates",
                schema: "trading",
                table: "shipments",
                sql: "\"ActualArrival\" IS NULL OR \"ActualDeparture\" IS NULL OR \"ActualArrival\" >= \"ActualDeparture\"");

            migrationBuilder.AddCheckConstraint(
                name: "ck_trade_shipment_estimated_dates",
                schema: "trading",
                table: "shipments",
                sql: "\"EstimatedArrival\" IS NULL OR \"EstimatedDeparture\" IS NULL OR \"EstimatedArrival\" >= \"EstimatedDeparture\"");

            migrationBuilder.AddCheckConstraint(
                name: "ck_trade_shipment_mode",
                schema: "trading",
                table: "shipments",
                sql: "\"TransportMode\" IN ('SEA','AIR','ROAD','RAIL','MULTIMODAL')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_trade_shipment_status",
                schema: "trading",
                table: "shipments",
                sql: "\"Status\" IN ('DRAFT','WAITING_LINK','READY','IN_TRANSIT','ARRIVED','IN_CUSTOMS','PARTIAL_CLEARED','CLEARED','DELIVERED','CANCELLED')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_trade_leg_actual_dates",
                schema: "trading",
                table: "shipment_legs",
                sql: "\"ActualArrival\" IS NULL OR \"ActualDeparture\" IS NULL OR \"ActualArrival\" >= \"ActualDeparture\"");

            migrationBuilder.AddCheckConstraint(
                name: "ck_trade_leg_estimated_dates",
                schema: "trading",
                table: "shipment_legs",
                sql: "\"EstimatedArrival\" IS NULL OR \"EstimatedDeparture\" IS NULL OR \"EstimatedArrival\" >= \"EstimatedDeparture\"");

            migrationBuilder.AddCheckConstraint(
                name: "ck_trade_leg_mode",
                schema: "trading",
                table: "shipment_legs",
                sql: "\"TransportMode\" IN ('SEA','AIR','ROAD','RAIL','MULTIMODAL')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_trade_leg_status",
                schema: "trading",
                table: "shipment_legs",
                sql: "\"Status\" IN ('PLANNED','DEPARTED','IN_TRANSIT','ARRIVED','DELAYED','CANCELLED')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_trade_order_status",
                schema: "trading",
                table: "order_registrations",
                sql: "\"Status\" IN ('DRAFT','SUBMITTED','ACTIVE','SUSPENDED','CANCELLED')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_trade_fx_dates",
                schema: "trading",
                table: "fx_allocations",
                sql: "\"AllocationDate\" IS NULL OR \"AllocationDate\" >= \"RequestDate\"");

            migrationBuilder.AddCheckConstraint(
                name: "ck_trade_fx_deal",
                schema: "trading",
                table: "fx_allocations",
                sql: "\"DealStatus\" IN ('NOT_STARTED','IN_QUEUE','PRIORITY_CONFIRMED','ALLOCATED','DEAL_DONE','CANCELLED')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_trade_fx_process",
                schema: "trading",
                table: "fx_allocations",
                sql: "\"ProcessType\" IN ('REQUEST','ALLOCATION','DEAL','RECEIPT','CORRECTION')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_trade_fx_receipt",
                schema: "trading",
                table: "fx_allocations",
                sql: "\"ReceiptStatus\" IN ('NOT_RECEIVED','PARTIAL','COMPLETE','CANCELLED')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_trade_customs_dates",
                schema: "trading",
                table: "customs_clearances",
                sql: "\"ReleaseDate\" IS NULL OR \"DeclarationDate\" IS NULL OR \"ReleaseDate\" >= \"DeclarationDate\"");

            migrationBuilder.AddCheckConstraint(
                name: "ck_trade_customs_status",
                schema: "trading",
                table: "customs_clearances",
                sql: "\"Status\" IN ('NOT_STARTED','DOCUMENTS_PENDING','IN_PROGRESS','PARTIAL','COMPLETE','BLOCKED','CANCELLED')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_trade_customs_type",
                schema: "trading",
                table: "customs_clearances",
                sql: "\"ClearanceType\" IN ('DEFINITIVE','TEMPORARY','TRANSIT','RETURN')");

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION trading.prevent_audit_event_mutation()
                RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN
                    RAISE EXCEPTION 'Trading audit events are immutable.' USING ERRCODE = '23514';
                END;
                $body$;

                CREATE TRIGGER trg_trade_audit_events_immutable
                BEFORE UPDATE OR DELETE ON trading.audit_events
                FOR EACH ROW EXECUTE FUNCTION trading.prevent_audit_event_mutation();

                CREATE OR REPLACE FUNCTION trading.validate_cost_scope()
                RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN
                    IF NEW."ScopeType" = 'PROJECT' THEN
                        IF NEW."ScopeId" <> NEW."ProjectId" THEN
                            RAISE EXCEPTION 'PROJECT cost scope must reference its project.' USING ERRCODE = '23514';
                        END IF;
                    ELSIF NEW."ScopeType" = 'SHIPMENT' THEN
                        IF NOT EXISTS (
                            SELECT 1 FROM trading.project_shipments ps
                            WHERE ps."ProjectId" = NEW."ProjectId" AND ps."ShipmentId" = NEW."ScopeId") THEN
                            RAISE EXCEPTION 'SHIPMENT cost scope is not linked to its project.' USING ERRCODE = '23514';
                        END IF;
                    ELSIF NEW."ScopeType" = 'SHIPMENT_LEG' THEN
                        IF NOT EXISTS (
                            SELECT 1 FROM trading.project_shipments ps
                            JOIN trading.shipment_legs sl ON sl."ShipmentId" = ps."ShipmentId"
                            WHERE ps."ProjectId" = NEW."ProjectId" AND sl."Id" = NEW."ScopeId") THEN
                            RAISE EXCEPTION 'SHIPMENT_LEG cost scope is not linked to its project.' USING ERRCODE = '23514';
                        END IF;
                    ELSIF NEW."ScopeType" = 'CUSTOMS' THEN
                        IF NOT EXISTS (
                            SELECT 1 FROM trading.project_shipments ps
                            JOIN trading.customs_clearances cc ON cc."ShipmentId" = ps."ShipmentId"
                            WHERE ps."ProjectId" = NEW."ProjectId" AND cc."Id" = NEW."ScopeId") THEN
                            RAISE EXCEPTION 'CUSTOMS cost scope is not linked to its project.' USING ERRCODE = '23514';
                        END IF;
                    ELSE
                        RAISE EXCEPTION 'Unsupported Trading cost scope.' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $body$;

                CREATE TRIGGER trg_trade_cost_scope_integrity
                BEFORE INSERT OR UPDATE OF "ProjectId", "ScopeType", "ScopeId" ON trading.costs
                FOR EACH ROW EXECUTE FUNCTION trading.validate_cost_scope();

                CREATE OR REPLACE FUNCTION trading.require_open_project()
                RETURNS trigger LANGUAGE plpgsql AS $body$
                DECLARE project_id uuid;
                BEGIN
                    project_id := NEW."ProjectId";
                    IF EXISTS (
                        SELECT 1 FROM trading.projects p
                        WHERE p."Id" = project_id AND p."Status" IN ('CLOSED', 'CANCELLED')) THEN
                        RAISE EXCEPTION 'Closed or cancelled Trading project is not operational.' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $body$;

                CREATE TRIGGER trg_trade_order_open_project
                BEFORE INSERT OR UPDATE ON trading.order_registrations
                FOR EACH ROW EXECUTE FUNCTION trading.require_open_project();
                CREATE TRIGGER trg_trade_fx_open_project
                BEFORE INSERT OR UPDATE ON trading.fx_allocations
                FOR EACH ROW EXECUTE FUNCTION trading.require_open_project();
                CREATE TRIGGER trg_trade_project_shipment_open_project
                BEFORE INSERT OR UPDATE ON trading.project_shipments
                FOR EACH ROW EXECUTE FUNCTION trading.require_open_project();
                CREATE TRIGGER trg_trade_cost_open_project
                BEFORE INSERT ON trading.costs
                FOR EACH ROW EXECUTE FUNCTION trading.require_open_project();

                CREATE OR REPLACE FUNCTION trading.require_operational_shipment_projects()
                RETURNS trigger LANGUAGE plpgsql AS $body$
                DECLARE shipment_id uuid;
                BEGIN
                    shipment_id := CASE WHEN TG_TABLE_NAME = 'shipments' THEN NEW."Id" ELSE NEW."ShipmentId" END;
                    IF EXISTS (
                        SELECT 1 FROM trading.project_shipments ps
                        JOIN trading.projects p ON p."Id" = ps."ProjectId"
                        WHERE ps."ShipmentId" = shipment_id AND p."Status" IN ('CLOSED', 'CANCELLED')) THEN
                        RAISE EXCEPTION 'Shipment belongs to a closed or cancelled Trading project.' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $body$;

                CREATE TRIGGER trg_trade_shipment_operational_projects
                BEFORE UPDATE ON trading.shipments
                FOR EACH ROW EXECUTE FUNCTION trading.require_operational_shipment_projects();
                CREATE TRIGGER trg_trade_leg_operational_projects
                BEFORE INSERT OR UPDATE ON trading.shipment_legs
                FOR EACH ROW EXECUTE FUNCTION trading.require_operational_shipment_projects();
                CREATE TRIGGER trg_trade_customs_operational_projects
                BEFORE INSERT OR UPDATE ON trading.customs_clearances
                FOR EACH ROW EXECUTE FUNCTION trading.require_operational_shipment_projects();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_trade_customs_operational_projects ON trading.customs_clearances;
                DROP TRIGGER IF EXISTS trg_trade_leg_operational_projects ON trading.shipment_legs;
                DROP TRIGGER IF EXISTS trg_trade_shipment_operational_projects ON trading.shipments;
                DROP FUNCTION IF EXISTS trading.require_operational_shipment_projects();
                DROP TRIGGER IF EXISTS trg_trade_cost_open_project ON trading.costs;
                DROP TRIGGER IF EXISTS trg_trade_project_shipment_open_project ON trading.project_shipments;
                DROP TRIGGER IF EXISTS trg_trade_fx_open_project ON trading.fx_allocations;
                DROP TRIGGER IF EXISTS trg_trade_order_open_project ON trading.order_registrations;
                DROP FUNCTION IF EXISTS trading.require_open_project();
                DROP TRIGGER IF EXISTS trg_trade_cost_scope_integrity ON trading.costs;
                DROP FUNCTION IF EXISTS trading.validate_cost_scope();
                DROP TRIGGER IF EXISTS trg_trade_audit_events_immutable ON trading.audit_events;
                DROP FUNCTION IF EXISTS trading.prevent_audit_event_mutation();
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_trade_shipment_actual_dates",
                schema: "trading",
                table: "shipments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_trade_shipment_estimated_dates",
                schema: "trading",
                table: "shipments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_trade_shipment_mode",
                schema: "trading",
                table: "shipments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_trade_shipment_status",
                schema: "trading",
                table: "shipments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_trade_leg_actual_dates",
                schema: "trading",
                table: "shipment_legs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_trade_leg_estimated_dates",
                schema: "trading",
                table: "shipment_legs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_trade_leg_mode",
                schema: "trading",
                table: "shipment_legs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_trade_leg_status",
                schema: "trading",
                table: "shipment_legs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_trade_order_status",
                schema: "trading",
                table: "order_registrations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_trade_fx_dates",
                schema: "trading",
                table: "fx_allocations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_trade_fx_deal",
                schema: "trading",
                table: "fx_allocations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_trade_fx_process",
                schema: "trading",
                table: "fx_allocations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_trade_fx_receipt",
                schema: "trading",
                table: "fx_allocations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_trade_customs_dates",
                schema: "trading",
                table: "customs_clearances");

            migrationBuilder.DropCheckConstraint(
                name: "ck_trade_customs_status",
                schema: "trading",
                table: "customs_clearances");

            migrationBuilder.DropCheckConstraint(
                name: "ck_trade_customs_type",
                schema: "trading",
                table: "customs_clearances");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUtc",
                schema: "trading",
                table: "project_shipments");

            migrationBuilder.DropColumn(
                name: "Version",
                schema: "trading",
                table: "project_shipments");
        }
    }
}
