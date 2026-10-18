using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCollaborationWorkspace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "collaboration");

            migrationBuilder.CreateSequence(
                name: "official_letter_number_seq",
                schema: "collaboration");

            migrationBuilder.CreateTable(
                name: "conversations",
                schema: "collaboration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsPrivate = table.Column<bool>(type: "boolean", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversations", x => x.Id);
                    table.CheckConstraint("ck_conversations_type", "\"Type\" IN ('CHANNEL','DIRECT')");
                    table.CheckConstraint("ck_conversations_version", "\"Version\" >= 1");
                });

            migrationBuilder.CreateTable(
                name: "official_letters",
                schema: "collaboration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Body = table.Column<string>(type: "character varying(40000)", maxLength: 40000, nullable: false),
                    Classification = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SenderUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SenderDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    IssuedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AcknowledgedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_official_letters", x => x.Id);
                    table.CheckConstraint("ck_official_letters_classification", "\"Classification\" IN ('NORMAL','CONFIDENTIAL','HIGHLY_CONFIDENTIAL')");
                    table.CheckConstraint("ck_official_letters_status", "\"Status\" IN ('DRAFT','ISSUED','ACKNOWLEDGED','CLOSED')");
                });

            migrationBuilder.CreateTable(
                name: "call_sessions",
                schema: "collaboration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    RoomName = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    HostUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    HostDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    VideoEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_call_sessions", x => x.Id);
                    table.CheckConstraint("ck_call_sessions_status", "\"Status\" IN ('ACTIVE','ENDED')");
                    table.ForeignKey(
                        name: "FK_call_sessions_conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalSchema: "collaboration",
                        principalTable: "conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "conversation_members",
                schema: "collaboration",
                columns: table => new
                {
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    JoinedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastReadAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversation_members", x => new { x.ConversationId, x.UserId });
                    table.CheckConstraint("ck_conversation_members_role", "\"Role\" IN ('OWNER','MEMBER')");
                    table.ForeignKey(
                        name: "FK_conversation_members_conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalSchema: "collaboration",
                        principalTable: "conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                schema: "collaboration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Body = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    StickerCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ReplyToMessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    OfficialLetterId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EditedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_messages", x => x.Id);
                    table.CheckConstraint("ck_messages_content", "\"DeletedAtUtc\" IS NOT NULL OR \"Body\" IS NOT NULL OR \"StickerCode\" IS NOT NULL OR \"OfficialLetterId\" IS NOT NULL");
                    table.CheckConstraint("ck_messages_type", "\"Type\" IN ('TEXT','STICKER','SYSTEM','LETTER_REFERENCE','CALL_EVENT')");
                    table.ForeignKey(
                        name: "FK_messages_conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalSchema: "collaboration",
                        principalTable: "conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "official_letter_events",
                schema: "collaboration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OfficialLetterId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_official_letter_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_official_letter_events_official_letters_OfficialLetterId",
                        column: x => x.OfficialLetterId,
                        principalSchema: "collaboration",
                        principalTable: "official_letters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "official_letter_recipients",
                schema: "collaboration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OfficialLetterId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_official_letter_recipients", x => x.Id);
                    table.CheckConstraint("ck_official_letter_recipients_type", "\"Type\" IN ('TO','CC')");
                    table.ForeignKey(
                        name: "FK_official_letter_recipients_official_letters_OfficialLetterId",
                        column: x => x.OfficialLetterId,
                        principalSchema: "collaboration",
                        principalTable: "official_letters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "attachments",
                schema: "collaboration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    OfficialLetterId = table.Column<Guid>(type: "uuid", nullable: true),
                    UploadedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UploadedByDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ObjectKey = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ValidationStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attachments", x => x.Id);
                    table.CheckConstraint("ck_collaboration_attachments_owner", "num_nonnulls(\"MessageId\", \"OfficialLetterId\") = 1");
                    table.CheckConstraint("ck_collaboration_attachments_size", "\"Size\" > 0 AND \"Size\" <= 26214400");
                    table.ForeignKey(
                        name: "FK_attachments_messages_MessageId",
                        column: x => x.MessageId,
                        principalSchema: "collaboration",
                        principalTable: "messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_attachments_official_letters_OfficialLetterId",
                        column: x => x.OfficialLetterId,
                        principalSchema: "collaboration",
                        principalTable: "official_letters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "message_reactions",
                schema: "collaboration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_message_reactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_message_reactions_messages_MessageId",
                        column: x => x.MessageId,
                        principalSchema: "collaboration",
                        principalTable: "messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_attachments_MessageId",
                schema: "collaboration",
                table: "attachments",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_attachments_ObjectKey",
                schema: "collaboration",
                table: "attachments",
                column: "ObjectKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_attachments_OfficialLetterId",
                schema: "collaboration",
                table: "attachments",
                column: "OfficialLetterId");

            migrationBuilder.CreateIndex(
                name: "IX_call_sessions_ConversationId_Status",
                schema: "collaboration",
                table: "call_sessions",
                columns: new[] { "ConversationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_call_sessions_RoomName",
                schema: "collaboration",
                table: "call_sessions",
                column: "RoomName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_conversation_members_UserId",
                schema: "collaboration",
                table: "conversation_members",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_conversations_Type_IsArchived",
                schema: "collaboration",
                table: "conversations",
                columns: new[] { "Type", "IsArchived" });

            migrationBuilder.CreateIndex(
                name: "IX_message_reactions_MessageId_UserId_Code",
                schema: "collaboration",
                table: "message_reactions",
                columns: new[] { "MessageId", "UserId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_messages_ConversationId_CreatedAtUtc",
                schema: "collaboration",
                table: "messages",
                columns: new[] { "ConversationId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_messages_OfficialLetterId",
                schema: "collaboration",
                table: "messages",
                column: "OfficialLetterId");

            migrationBuilder.CreateIndex(
                name: "IX_official_letter_events_OfficialLetterId_OccurredAtUtc",
                schema: "collaboration",
                table: "official_letter_events",
                columns: new[] { "OfficialLetterId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_official_letter_recipients_OfficialLetterId_UserId_Type",
                schema: "collaboration",
                table: "official_letter_recipients",
                columns: new[] { "OfficialLetterId", "UserId", "Type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_official_letter_recipients_UserId",
                schema: "collaboration",
                table: "official_letter_recipients",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_official_letters_Number",
                schema: "collaboration",
                table: "official_letters",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_official_letters_Status_CreatedAtUtc",
                schema: "collaboration",
                table: "official_letters",
                columns: new[] { "Status", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attachments",
                schema: "collaboration");

            migrationBuilder.DropTable(
                name: "call_sessions",
                schema: "collaboration");

            migrationBuilder.DropTable(
                name: "conversation_members",
                schema: "collaboration");

            migrationBuilder.DropTable(
                name: "message_reactions",
                schema: "collaboration");

            migrationBuilder.DropTable(
                name: "official_letter_events",
                schema: "collaboration");

            migrationBuilder.DropTable(
                name: "official_letter_recipients",
                schema: "collaboration");

            migrationBuilder.DropTable(
                name: "messages",
                schema: "collaboration");

            migrationBuilder.DropTable(
                name: "official_letters",
                schema: "collaboration");

            migrationBuilder.DropTable(
                name: "conversations",
                schema: "collaboration");

            migrationBuilder.DropSequence(
                name: "official_letter_number_seq",
                schema: "collaboration");
        }
    }
}
