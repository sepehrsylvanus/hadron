using Hadron.Erp.Api.Modules.Collaboration.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hadron.Erp.Api.Modules.Collaboration.Infrastructure;

internal sealed class MeetingSessionConfiguration : IEntityTypeConfiguration<MeetingSession>
{
    public void Configure(EntityTypeBuilder<MeetingSession> b)
    {
        b.ToTable("meeting_sessions", "collaboration");
        b.HasKey(x => x.Id);
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Status).HasMaxLength(20).IsRequired();
        b.Property(x => x.Provider).HasMaxLength(30).IsRequired();
        b.Property(x => x.RoomName).HasMaxLength(180);
        b.Property(x => x.CreatedByDisplayName).HasMaxLength(200).IsRequired();
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Ignore(x => x.Policy);
        b.HasIndex(x => x.RoomName).IsUnique().HasFilter("\"RoomName\" IS NOT NULL");
        b.HasIndex(x => new { x.Status, x.ScheduledStartAtUtc });
        b.HasIndex(x => new { x.ConversationId, x.Status });
        b.HasIndex(x => x.ConversationId).IsUnique();
        b.HasOne(x => x.Conversation).WithOne().HasForeignKey<MeetingSession>(x => x.ConversationId)
            .OnDelete(DeleteBehavior.Restrict);
        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_meeting_sessions_status",
                "\"Status\" IN ('SCHEDULED','LIVE','ENDED','CANCELLED')");
            t.HasCheckConstraint("ck_meeting_sessions_recording_disabled", "\"RecordingEnabled\" = FALSE");
            t.HasCheckConstraint("ck_meeting_sessions_schedule",
                "(\"ScheduledStartAtUtc\" IS NULL AND \"ScheduledEndAtUtc\" IS NULL) OR " +
                "(\"ScheduledStartAtUtc\" IS NOT NULL AND \"ScheduledEndAtUtc\" IS NOT NULL AND \"ScheduledEndAtUtc\" > \"ScheduledStartAtUtc\")");
        });
    }
}

internal sealed class MeetingParticipantConfiguration : IEntityTypeConfiguration<MeetingParticipant>
{
    public void Configure(EntityTypeBuilder<MeetingParticipant> b)
    {
        b.ToTable("meeting_participants", "collaboration");
        b.HasKey(x => x.Id);
        b.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
        b.Property(x => x.Role).HasMaxLength(30).IsRequired();
        b.Property(x => x.AdmissionStatus).HasMaxLength(20).IsRequired();
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Ignore(x => x.IsExternal);
        b.HasIndex(x => new { x.MeetingSessionId, x.UserId }).IsUnique()
            .HasFilter("\"UserId\" IS NOT NULL");
        b.HasIndex(x => x.MeetingSessionId).IsUnique()
            .HasDatabaseName("ux_meeting_participants_one_host")
            .HasFilter("\"Role\" = 'HOST'");
        b.HasIndex(x => new { x.MeetingSessionId, x.AdmissionStatus });
        b.HasOne(x => x.MeetingSession).WithMany(x => x.Participants)
            .HasForeignKey(x => x.MeetingSessionId).OnDelete(DeleteBehavior.Cascade);
        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_meeting_participants_role",
                "\"Role\" IN ('HOST','COHOST','PRESENTER','ATTENDEE','EXTERNAL_GUEST')");
            t.HasCheckConstraint("ck_meeting_participants_admission",
                "\"AdmissionStatus\" IN ('INVITED','WAITING','ADMITTED','DENIED','REVOKED')");
            t.HasCheckConstraint("ck_meeting_participants_identity",
                "(\"Role\" = 'EXTERNAL_GUEST' AND \"UserId\" IS NULL) OR " +
                "(\"Role\" <> 'EXTERNAL_GUEST' AND \"UserId\" IS NOT NULL)");
        });
    }
}

internal sealed class MeetingGuestInviteConfiguration : IEntityTypeConfiguration<MeetingGuestInvite>
{
    public void Configure(EntityTypeBuilder<MeetingGuestInvite> b)
    {
        b.ToTable("meeting_guest_invites", "collaboration");
        b.HasKey(x => x.Id);
        b.Property(x => x.TokenHash).HasMaxLength(64).IsFixedLength().IsRequired();
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => x.ParticipantId).IsUnique();
        b.HasIndex(x => new { x.MeetingSessionId, x.ExpiresAtUtc });
        b.HasOne(x => x.MeetingSession).WithMany().HasForeignKey(x => x.MeetingSessionId)
            .OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Participant).WithOne().HasForeignKey<MeetingGuestInvite>(x => x.ParticipantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MeetingGuestAccessTicketConfiguration : IEntityTypeConfiguration<MeetingGuestAccessTicket>
{
    public void Configure(EntityTypeBuilder<MeetingGuestAccessTicket> b)
    {
        b.ToTable("meeting_guest_access_tickets", "collaboration");
        b.HasKey(x => x.Id);
        b.Property(x => x.TokenHash).HasMaxLength(64).IsFixedLength().IsRequired();
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => x.ParticipantId).IsUnique();
        b.HasIndex(x => x.ExpiresAtUtc);
        b.HasOne(x => x.MeetingSession).WithMany().HasForeignKey(x => x.MeetingSessionId)
            .OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Participant).WithMany().HasForeignKey(x => x.ParticipantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MeetingAttendanceIntervalConfiguration : IEntityTypeConfiguration<MeetingAttendanceInterval>
{
    public void Configure(EntityTypeBuilder<MeetingAttendanceInterval> b)
    {
        b.ToTable("meeting_attendance_intervals", "collaboration");
        b.HasKey(x => x.Id);
        b.Property(x => x.Source).HasMaxLength(20).IsRequired();
        b.HasIndex(x => new { x.MeetingSessionId, x.ParticipantId, x.ConnectedAtUtc });
        b.HasIndex(x => x.ParticipantId).IsUnique()
            .HasDatabaseName("ux_meeting_attendance_one_open_interval")
            .HasFilter("\"DisconnectedAtUtc\" IS NULL");
        b.HasOne(x => x.MeetingSession).WithMany().HasForeignKey(x => x.MeetingSessionId)
            .OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Participant).WithMany(x => x.AttendanceIntervals)
            .HasForeignKey(x => x.ParticipantId).OnDelete(DeleteBehavior.Cascade);
        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_meeting_attendance_source", "\"Source\" IN ('CLIENT','PROVIDER')");
            t.HasCheckConstraint("ck_meeting_attendance_interval",
                "\"DisconnectedAtUtc\" IS NULL OR \"DisconnectedAtUtc\" >= \"ConnectedAtUtc\"");
        });
    }
}

internal sealed class MeetingEventConfiguration : IEntityTypeConfiguration<MeetingEvent>
{
    public void Configure(EntityTypeBuilder<MeetingEvent> b)
    {
        b.ToTable("meeting_events", "collaboration");
        b.HasKey(x => x.Id);
        b.Property(x => x.EventType).HasMaxLength(50).IsRequired();
        b.Property(x => x.ActorDisplayName).HasMaxLength(200).IsRequired();
        b.Property(x => x.Source).HasMaxLength(20).IsRequired();
        b.Property(x => x.Detail).HasMaxLength(500);
        b.HasIndex(x => new { x.MeetingSessionId, x.OccurredAtUtc });
        b.HasOne(x => x.MeetingSession).WithMany(x => x.Events)
            .HasForeignKey(x => x.MeetingSessionId).OnDelete(DeleteBehavior.Cascade);
        b.ToTable(t => t.HasCheckConstraint("ck_meeting_events_source",
            "\"Source\" IN ('UI','CLIENT','PROVIDER')"));
    }
}
