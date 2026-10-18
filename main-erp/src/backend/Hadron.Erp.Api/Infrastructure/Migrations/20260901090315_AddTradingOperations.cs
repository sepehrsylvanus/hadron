using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTradingOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "trading");

            migrationBuilder.CreateSequence(
                name: "trade_project_number_seq",
                schema: "trading");

            migrationBuilder.CreateSequence(
                name: "trade_reference_value_number_seq",
                schema: "trading");

            migrationBuilder.CreateSequence(
                name: "trade_shipment_number_seq",
                schema: "trading");

            migrationBuilder.CreateTable(
                name: "audit_events",
                schema: "trading",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PreviousJson = table.Column<string>(type: "jsonb", nullable: true),
                    CurrentJson = table.Column<string>(type: "jsonb", nullable: true),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "projects",
                schema: "trading",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TitleFa = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    TitleEn = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerCodeSnapshot = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    CustomerNameFaSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CustomerNameEnSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupplierNumberSnapshot = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    SupplierNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Priority = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TargetDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_projects", x => x.Id);
                    table.CheckConstraint("ck_trade_project_priority", "\"Priority\" IN ('LOW','NORMAL','HIGH','CRITICAL')");
                    table.CheckConstraint("ck_trade_project_status", "\"Status\" IN ('DRAFT','ACTIVE','WAITING_ORDER','FX_PROCESS','READY_TO_SHIP','IN_TRANSIT','IN_CUSTOMS','CLEARED','DELIVERED','CLOSED','CANCELLED')");
                });

            migrationBuilder.CreateTable(
                name: "reference_values",
                schema: "trading",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferenceType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    NameFa = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reference_values1", x => x.Id);
                    table.CheckConstraint("ck_trade_reference_type", "\"ReferenceType\" IN ('CUSTOMER','BUSINESS_CARD','BANK','FORWARDER','CARRIER','DESTINATION','INSURANCE_COMPANY','INSPECTION_COMPANY','CUSTOMS_OFFICE','CUSTOMS_BROKER')");
                });

            migrationBuilder.CreateTable(
                name: "shipments",
                schema: "trading",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TransportMode = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    ForwarderId = table.Column<Guid>(type: "uuid", nullable: true),
                    ForwarderCodeSnapshot = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ForwarderNameFaSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ForwarderNameEnSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Origin = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Destination = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EstimatedDeparture = table.Column<DateOnly>(type: "date", nullable: true),
                    EstimatedArrival = table.Column<DateOnly>(type: "date", nullable: true),
                    ActualDeparture = table.Column<DateOnly>(type: "date", nullable: true),
                    ActualArrival = table.Column<DateOnly>(type: "date", nullable: true),
                    GrossWeight = table.Column<decimal>(type: "numeric(24,6)", precision: 24, scale: 6, nullable: true),
                    VolumeCbm = table.Column<decimal>(type: "numeric(24,6)", precision: 24, scale: 6, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shipments", x => x.Id);
                    table.CheckConstraint("ck_trade_shipment_volume", "\"VolumeCbm\" IS NULL OR \"VolumeCbm\" >= 0");
                    table.CheckConstraint("ck_trade_shipment_weight", "\"GrossWeight\" IS NULL OR \"GrossWeight\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "costs",
                schema: "trading",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScopeType = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    ScopeId = table.Column<Guid>(type: "uuid", nullable: false),
                    CostType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(24,6)", precision: 24, scale: 6, nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrencyCodeSnapshot = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CurrencyNameFaSnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CurrencyNameEnSnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ReferenceNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IsVoided = table.Column<bool>(type: "boolean", nullable: false),
                    VoidedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    VoidedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VoidReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_costs", x => x.Id);
                    table.CheckConstraint("ck_trade_cost_amount", "\"Amount\" > 0");
                    table.CheckConstraint("ck_trade_cost_scope", "\"ScopeType\" IN ('PROJECT','SHIPMENT','SHIPMENT_LEG','CUSTOMS')");
                    table.ForeignKey(
                        name: "FK_costs_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalSchema: "trading",
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_registrations",
                schema: "trading",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    RegistrationNumber = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    RegistrationDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RegisteredAmount = table.Column<decimal>(type: "numeric(24,6)", precision: 24, scale: 6, nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrencyCodeSnapshot = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CurrencyNameFaSnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CurrencyNameEnSnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_registrations", x => x.Id);
                    table.CheckConstraint("ck_trade_order_amount", "\"RegisteredAmount\" > 0");
                    table.ForeignKey(
                        name: "FK_order_registrations_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalSchema: "trading",
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "report_notes",
                schema: "trading",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Note = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_report_notes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_report_notes_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalSchema: "trading",
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "customs_clearances",
                schema: "trading",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClearanceNumber = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ClearanceType = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    CustomsOfficeId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomsOfficeCodeSnapshot = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    CustomsOfficeNameFaSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CustomsOfficeNameEnSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DeclarationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ReleaseDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customs_clearances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_customs_clearances_shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalSchema: "trading",
                        principalTable: "shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "project_shipments",
                schema: "trading",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocationMethod = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    AllocationValue = table.Column<decimal>(type: "numeric(24,6)", precision: 24, scale: 6, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_shipments", x => x.Id);
                    table.CheckConstraint("ck_trade_link_method", "\"AllocationMethod\" IN ('GOODS_VALUE','CBM','WEIGHT','COUNT','MANUAL_PERCENT','EQUAL')");
                    table.CheckConstraint("ck_trade_link_value", "\"AllocationValue\" >= 0");
                    table.ForeignKey(
                        name: "FK_project_shipments_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalSchema: "trading",
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_project_shipments_shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalSchema: "trading",
                        principalTable: "shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "shipment_legs",
                schema: "trading",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    FromLocation = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ToLocation = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TransportMode = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    EstimatedDeparture = table.Column<DateOnly>(type: "date", nullable: true),
                    EstimatedArrival = table.Column<DateOnly>(type: "date", nullable: true),
                    ActualDeparture = table.Column<DateOnly>(type: "date", nullable: true),
                    ActualArrival = table.Column<DateOnly>(type: "date", nullable: true),
                    TrackingNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shipment_legs", x => x.Id);
                    table.CheckConstraint("ck_trade_shipment_leg_sequence", "\"Sequence\" > 0");
                    table.ForeignKey(
                        name: "FK_shipment_legs_shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalSchema: "trading",
                        principalTable: "shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fx_allocations",
                schema: "trading",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderRegistrationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProcessType = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    DealStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReceiptStatus = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(24,6)", precision: 24, scale: 6, nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrencyCodeSnapshot = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CurrencyNameFaSnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CurrencyNameEnSnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    BankId = table.Column<Guid>(type: "uuid", nullable: true),
                    BankCodeSnapshot = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    BankNameFaSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    BankNameEnSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RequestDate = table.Column<DateOnly>(type: "date", nullable: false),
                    AllocationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ReferenceNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fx_allocations", x => x.Id);
                    table.CheckConstraint("ck_trade_fx_amount", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_fx_allocations_order_registrations_OrderRegistrationId",
                        column: x => x.OrderRegistrationId,
                        principalSchema: "trading",
                        principalTable: "order_registrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fx_allocations_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalSchema: "trading",
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_EntityType_EntityId_OccurredAtUtc",
                schema: "trading",
                table: "audit_events",
                columns: new[] { "EntityType", "EntityId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_costs_ProjectId_CurrencyCodeSnapshot_IsVoided_EffectiveDate",
                schema: "trading",
                table: "costs",
                columns: new[] { "ProjectId", "CurrencyCodeSnapshot", "IsVoided", "EffectiveDate" });

            migrationBuilder.CreateIndex(
                name: "IX_costs_ScopeType_ScopeId",
                schema: "trading",
                table: "costs",
                columns: new[] { "ScopeType", "ScopeId" });

            migrationBuilder.CreateIndex(
                name: "IX_customs_clearances_ClearanceNumber",
                schema: "trading",
                table: "customs_clearances",
                column: "ClearanceNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customs_clearances_ShipmentId_Status",
                schema: "trading",
                table: "customs_clearances",
                columns: new[] { "ShipmentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_fx_allocations_OrderRegistrationId",
                schema: "trading",
                table: "fx_allocations",
                column: "OrderRegistrationId");

            migrationBuilder.CreateIndex(
                name: "IX_fx_allocations_ProjectId_DealStatus_RequestDate",
                schema: "trading",
                table: "fx_allocations",
                columns: new[] { "ProjectId", "DealStatus", "RequestDate" });

            migrationBuilder.CreateIndex(
                name: "IX_order_registrations_ProjectId_Status",
                schema: "trading",
                table: "order_registrations",
                columns: new[] { "ProjectId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_order_registrations_RegistrationNumber",
                schema: "trading",
                table: "order_registrations",
                column: "RegistrationNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_project_shipments_ProjectId_ShipmentId",
                schema: "trading",
                table: "project_shipments",
                columns: new[] { "ProjectId", "ShipmentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_project_shipments_ShipmentId",
                schema: "trading",
                table: "project_shipments",
                column: "ShipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_projects_CustomerId",
                schema: "trading",
                table: "projects",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_projects_ProjectNumber",
                schema: "trading",
                table: "projects",
                column: "ProjectNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_projects_Status_Priority_TargetDate",
                schema: "trading",
                table: "projects",
                columns: new[] { "Status", "Priority", "TargetDate" });

            migrationBuilder.CreateIndex(
                name: "IX_projects_SupplierId",
                schema: "trading",
                table: "projects",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_reference_values_ReferenceType_Code",
                schema: "trading",
                table: "reference_values",
                columns: new[] { "ReferenceType", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reference_values_ReferenceType_IsActive_SortOrder",
                schema: "trading",
                table: "reference_values",
                columns: new[] { "ReferenceType", "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_report_notes_ProjectId_CreatedAtUtc",
                schema: "trading",
                table: "report_notes",
                columns: new[] { "ProjectId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_shipment_legs_ShipmentId_Sequence",
                schema: "trading",
                table: "shipment_legs",
                columns: new[] { "ShipmentId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_shipments_ShipmentNumber",
                schema: "trading",
                table: "shipments",
                column: "ShipmentNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_shipments_Status_EstimatedArrival",
                schema: "trading",
                table: "shipments",
                columns: new[] { "Status", "EstimatedArrival" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_events",
                schema: "trading");

            migrationBuilder.DropTable(
                name: "costs",
                schema: "trading");

            migrationBuilder.DropTable(
                name: "customs_clearances",
                schema: "trading");

            migrationBuilder.DropTable(
                name: "fx_allocations",
                schema: "trading");

            migrationBuilder.DropTable(
                name: "project_shipments",
                schema: "trading");

            migrationBuilder.DropTable(
                name: "reference_values",
                schema: "trading");

            migrationBuilder.DropTable(
                name: "report_notes",
                schema: "trading");

            migrationBuilder.DropTable(
                name: "shipment_legs",
                schema: "trading");

            migrationBuilder.DropTable(
                name: "order_registrations",
                schema: "trading");

            migrationBuilder.DropTable(
                name: "shipments",
                schema: "trading");

            migrationBuilder.DropTable(
                name: "projects",
                schema: "trading");

            migrationBuilder.DropSequence(
                name: "trade_project_number_seq",
                schema: "trading");

            migrationBuilder.DropSequence(
                name: "trade_reference_value_number_seq",
                schema: "trading");

            migrationBuilder.DropSequence(
                name: "trade_shipment_number_seq",
                schema: "trading");
        }
    }
}
