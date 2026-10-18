using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCollaborationMeetingsV1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "meeting_sessions",
                schema: "collaboration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    Provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    RoomName = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: true),
                    ScheduledStartAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ScheduledEndAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    AttendeeMicrophone = table.Column<bool>(type: "boolean", nullable: false),
                    AttendeeCamera = table.Column<bool>(type: "boolean", nullable: false),
                    AttendeeScreenShare = table.Column<bool>(type: "boolean", nullable: false),
                    GuestMicrophone = table.Column<bool>(type: "boolean", nullable: false),
                    GuestCamera = table.Column<bool>(type: "boolean", nullable: false),
                    GuestScreenShare = table.Column<bool>(type: "boolean", nullable: false),
                    ChatEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    FileSharingEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    EphemeralWhiteboardEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    RecordingEnabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_meeting_sessions", x => x.Id);
                    table.CheckConstraint("ck_meeting_sessions_recording_disabled", "\"RecordingEnabled\" = FALSE");
                    table.CheckConstraint("ck_meeting_sessions_schedule", "(\"ScheduledStartAtUtc\" IS NULL AND \"ScheduledEndAtUtc\" IS NULL) OR (\"ScheduledStartAtUtc\" IS NOT NULL AND \"ScheduledEndAtUtc\" IS NOT NULL AND \"ScheduledEndAtUtc\" > \"ScheduledStartAtUtc\")");
                    table.CheckConstraint("ck_meeting_sessions_status", "\"Status\" IN ('SCHEDULED','LIVE','ENDED','CANCELLED')");
                    table.ForeignKey(
                        name: "FK_meeting_sessions_conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalSchema: "collaboration",
                        principalTable: "conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "meeting_events",
                schema: "collaboration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MeetingSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ParticipantId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_meeting_events", x => x.Id);
                    table.CheckConstraint("ck_meeting_events_source", "\"Source\" IN ('UI','CLIENT','PROVIDER')");
                    table.ForeignKey(
                        name: "FK_meeting_events_meeting_sessions_MeetingSessionId",
                        column: x => x.MeetingSessionId,
                        principalSchema: "collaboration",
                        principalTable: "meeting_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "meeting_participants",
                schema: "collaboration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MeetingSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Role = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    AdmissionStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AdmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeniedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_meeting_participants", x => x.Id);
                    table.CheckConstraint("ck_meeting_participants_admission", "\"AdmissionStatus\" IN ('INVITED','WAITING','ADMITTED','DENIED','REVOKED')");
                    table.CheckConstraint("ck_meeting_participants_identity", "(\"Role\" = 'EXTERNAL_GUEST' AND \"UserId\" IS NULL) OR (\"Role\" <> 'EXTERNAL_GUEST' AND \"UserId\" IS NOT NULL)");
                    table.CheckConstraint("ck_meeting_participants_role", "\"Role\" IN ('HOST','COHOST','PRESENTER','ATTENDEE','EXTERNAL_GUEST')");
                    table.ForeignKey(
                        name: "FK_meeting_participants_meeting_sessions_MeetingSessionId",
                        column: x => x.MeetingSessionId,
                        principalSchema: "collaboration",
                        principalTable: "meeting_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "meeting_attendance_intervals",
                schema: "collaboration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MeetingSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DisconnectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_meeting_attendance_intervals", x => x.Id);
                    table.CheckConstraint("ck_meeting_attendance_interval", "\"DisconnectedAtUtc\" IS NULL OR \"DisconnectedAtUtc\" >= \"ConnectedAtUtc\"");
                    table.CheckConstraint("ck_meeting_attendance_source", "\"Source\" IN ('CLIENT','PROVIDER')");
                    table.ForeignKey(
                        name: "FK_meeting_attendance_intervals_meeting_participants_Participa~",
                        column: x => x.ParticipantId,
                        principalSchema: "collaboration",
                        principalTable: "meeting_participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_meeting_attendance_intervals_meeting_sessions_MeetingSessio~",
                        column: x => x.MeetingSessionId,
                        principalSchema: "collaboration",
                        principalTable: "meeting_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "meeting_guest_access_tickets",
                schema: "collaboration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MeetingSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_meeting_guest_access_tickets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_meeting_guest_access_tickets_meeting_participants_Participa~",
                        column: x => x.ParticipantId,
                        principalSchema: "collaboration",
                        principalTable: "meeting_participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_meeting_guest_access_tickets_meeting_sessions_MeetingSessio~",
                        column: x => x.MeetingSessionId,
                        principalSchema: "collaboration",
                        principalTable: "meeting_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "meeting_guest_invites",
                schema: "collaboration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MeetingSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RedeemedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_meeting_guest_invites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_meeting_guest_invites_meeting_participants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalSchema: "collaboration",
                        principalTable: "meeting_participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_meeting_guest_invites_meeting_sessions_MeetingSessionId",
                        column: x => x.MeetingSessionId,
                        principalSchema: "collaboration",
                        principalTable: "meeting_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_meeting_attendance_intervals_MeetingSessionId_ParticipantId~",
                schema: "collaboration",
                table: "meeting_attendance_intervals",
                columns: new[] { "MeetingSessionId", "ParticipantId", "ConnectedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "ux_meeting_attendance_one_open_interval",
                schema: "collaboration",
                table: "meeting_attendance_intervals",
                column: "ParticipantId",
                unique: true,
                filter: "\"DisconnectedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_meeting_events_MeetingSessionId_OccurredAtUtc",
                schema: "collaboration",
                table: "meeting_events",
                columns: new[] { "MeetingSessionId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_meeting_guest_access_tickets_ExpiresAtUtc",
                schema: "collaboration",
                table: "meeting_guest_access_tickets",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_meeting_guest_access_tickets_MeetingSessionId",
                schema: "collaboration",
                table: "meeting_guest_access_tickets",
                column: "MeetingSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_meeting_guest_access_tickets_ParticipantId",
                schema: "collaboration",
                table: "meeting_guest_access_tickets",
                column: "ParticipantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_meeting_guest_access_tickets_TokenHash",
                schema: "collaboration",
                table: "meeting_guest_access_tickets",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_meeting_guest_invites_MeetingSessionId_ExpiresAtUtc",
                schema: "collaboration",
                table: "meeting_guest_invites",
                columns: new[] { "MeetingSessionId", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_meeting_guest_invites_ParticipantId",
                schema: "collaboration",
                table: "meeting_guest_invites",
                column: "ParticipantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_meeting_guest_invites_TokenHash",
                schema: "collaboration",
                table: "meeting_guest_invites",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_meeting_participants_MeetingSessionId_AdmissionStatus",
                schema: "collaboration",
                table: "meeting_participants",
                columns: new[] { "MeetingSessionId", "AdmissionStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_meeting_participants_MeetingSessionId_UserId",
                schema: "collaboration",
                table: "meeting_participants",
                columns: new[] { "MeetingSessionId", "UserId" },
                unique: true,
                filter: "\"UserId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_meeting_participants_one_host",
                schema: "collaboration",
                table: "meeting_participants",
                column: "MeetingSessionId",
                unique: true,
                filter: "\"Role\" = 'HOST'");

            migrationBuilder.CreateIndex(
                name: "IX_meeting_sessions_ConversationId",
                schema: "collaboration",
                table: "meeting_sessions",
                column: "ConversationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_meeting_sessions_ConversationId_Status",
                schema: "collaboration",
                table: "meeting_sessions",
                columns: new[] { "ConversationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_meeting_sessions_RoomName",
                schema: "collaboration",
                table: "meeting_sessions",
                column: "RoomName",
                unique: true,
                filter: "\"RoomName\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_meeting_sessions_Status_ScheduledStartAtUtc",
                schema: "collaboration",
                table: "meeting_sessions",
                columns: new[] { "Status", "ScheduledStartAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "meeting_attendance_intervals",
                schema: "collaboration");

            migrationBuilder.DropTable(
                name: "meeting_events",
                schema: "collaboration");

            migrationBuilder.DropTable(
                name: "meeting_guest_access_tickets",
                schema: "collaboration");

            migrationBuilder.DropTable(
                name: "meeting_guest_invites",
                schema: "collaboration");

            migrationBuilder.DropTable(
                name: "meeting_participants",
                schema: "collaboration");

            migrationBuilder.DropTable(
                name: "meeting_sessions",
                schema: "collaboration");
        }
    }
}
