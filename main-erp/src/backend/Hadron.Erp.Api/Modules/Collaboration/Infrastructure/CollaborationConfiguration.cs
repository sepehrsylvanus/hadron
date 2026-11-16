using Hadron.Erp.Api.Modules.Collaboration.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hadron.Erp.Api.Modules.Collaboration.Infrastructure;

internal sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> b)
    {
        b.ToTable("conversations", "collaboration"); b.HasKey(x => x.Id);
        b.Property(x => x.Type).HasMaxLength(20).IsRequired(); b.Property(x => x.Name).HasMaxLength(160).IsRequired();
        b.Property(x => x.Description).HasMaxLength(1000); b.Property(x => x.CreatedByDisplayName).HasMaxLength(200).IsRequired();
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => new { x.Type, x.IsArchived });
        b.ToTable(t => t.HasCheckConstraint("ck_conversations_type", "\"Type\" IN ('CHANNEL','DIRECT')"));
        b.ToTable(t => t.HasCheckConstraint("ck_conversations_version", "\"Version\" >= 1"));
    }
}

internal sealed class ConversationMemberConfiguration : IEntityTypeConfiguration<ConversationMember>
{
    public void Configure(EntityTypeBuilder<ConversationMember> b)
    {
        b.ToTable("conversation_members", "collaboration"); b.HasKey(x => new { x.ConversationId, x.UserId });
        b.Property(x => x.DisplayName).HasMaxLength(200).IsRequired(); b.Property(x => x.Role).HasMaxLength(20).IsRequired();
        b.HasOne(x => x.Conversation).WithMany(x => x.Members).HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => x.UserId);
        b.ToTable(t => t.HasCheckConstraint("ck_conversation_members_role", "\"Role\" IN ('OWNER','MEMBER')"));
    }
}

internal sealed class ConversationMessageConfiguration : IEntityTypeConfiguration<ConversationMessage>
{
    public void Configure(EntityTypeBuilder<ConversationMessage> b)
    {
        b.ToTable("messages", "collaboration"); b.HasKey(x => x.Id);
        b.Property(x => x.AuthorDisplayName).HasMaxLength(200).IsRequired(); b.Property(x => x.Type).HasMaxLength(30).IsRequired();
        b.Property(x => x.Body).HasMaxLength(10_000); b.Property(x => x.StickerCode).HasMaxLength(80); b.Property(x => x.Version).IsConcurrencyToken();
        b.HasOne(x => x.Conversation).WithMany(x => x.Messages).HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.ConversationId, x.CreatedAtUtc });
        b.HasIndex(x => x.OfficialLetterId);
        b.ToTable(t => t.HasCheckConstraint("ck_messages_type", "\"Type\" IN ('TEXT','STICKER','SYSTEM','LETTER_REFERENCE','CALL_EVENT')"));
        b.ToTable(t => t.HasCheckConstraint("ck_messages_content", "\"DeletedAtUtc\" IS NOT NULL OR \"Body\" IS NOT NULL OR \"StickerCode\" IS NOT NULL OR \"OfficialLetterId\" IS NOT NULL"));
    }
}

internal sealed class MessageReactionConfiguration : IEntityTypeConfiguration<MessageReaction>
{
    public void Configure(EntityTypeBuilder<MessageReaction> b)
    {
        b.ToTable("message_reactions", "collaboration"); b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(40).IsRequired();
        b.HasIndex(x => new { x.MessageId, x.UserId, x.Code }).IsUnique();
        b.HasOne(x => x.Message).WithMany(x => x.Reactions).HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class OfficialLetterConfiguration : IEntityTypeConfiguration<OfficialLetter>
{
    public void Configure(EntityTypeBuilder<OfficialLetter> b)
    {
        b.ToTable("official_letters", "collaboration"); b.HasKey(x => x.Id);
        b.Property(x => x.Number).HasMaxLength(40).IsRequired(); b.HasIndex(x => x.Number).IsUnique();
        b.Property(x => x.Subject).HasMaxLength(300).IsRequired(); b.Property(x => x.Body).HasMaxLength(40_000).IsRequired();
        b.Property(x => x.Classification).HasMaxLength(30).IsRequired(); b.Property(x => x.Status).HasMaxLength(20).IsRequired();
        b.Property(x => x.SenderDisplayName).HasMaxLength(200).IsRequired(); b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => new { x.Status, x.CreatedAtUtc });
        b.ToTable(t => t.HasCheckConstraint("ck_official_letters_status", "\"Status\" IN ('DRAFT','ISSUED','ACKNOWLEDGED','CLOSED')"));
        b.ToTable(t => t.HasCheckConstraint("ck_official_letters_classification", "\"Classification\" IN ('NORMAL','CONFIDENTIAL','HIGHLY_CONFIDENTIAL')"));
    }
}

internal sealed class OfficialLetterRecipientConfiguration : IEntityTypeConfiguration<OfficialLetterRecipient>
{
    public void Configure(EntityTypeBuilder<OfficialLetterRecipient> b)
    {
        b.ToTable("official_letter_recipients", "collaboration"); b.HasKey(x => x.Id);
        b.Property(x => x.DisplayName).HasMaxLength(200).IsRequired(); b.Property(x => x.Type).HasMaxLength(10).IsRequired();
        b.HasIndex(x => new { x.OfficialLetterId, x.UserId, x.Type }).IsUnique(); b.HasIndex(x => x.UserId);
        b.HasOne(x => x.OfficialLetter).WithMany(x => x.Recipients).HasForeignKey(x => x.OfficialLetterId).OnDelete(DeleteBehavior.Cascade);
        b.ToTable(t => t.HasCheckConstraint("ck_official_letter_recipients_type", "\"Type\" IN ('TO','CC')"));
    }
}

internal sealed class OfficialLetterEventConfiguration : IEntityTypeConfiguration<OfficialLetterEvent>
{
    public void Configure(EntityTypeBuilder<OfficialLetterEvent> b)
    {
        b.ToTable("official_letter_events", "collaboration"); b.HasKey(x => x.Id);
        b.Property(x => x.EventType).HasMaxLength(40).IsRequired(); b.Property(x => x.ActorDisplayName).HasMaxLength(200).IsRequired();
        b.Property(x => x.Note).HasMaxLength(1000); b.HasIndex(x => new { x.OfficialLetterId, x.OccurredAtUtc });
        b.HasOne(x => x.OfficialLetter).WithMany(x => x.Events).HasForeignKey(x => x.OfficialLetterId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CollaborationAttachmentConfiguration : IEntityTypeConfiguration<CollaborationAttachment>
{
    public void Configure(EntityTypeBuilder<CollaborationAttachment> b)
    {
        b.ToTable("attachments", "collaboration"); b.HasKey(x => x.Id);
        b.Property(x => x.UploadedByDisplayName).HasMaxLength(200).IsRequired(); b.Property(x => x.ObjectKey).HasMaxLength(600).IsRequired();
        b.Property(x => x.OriginalFileName).HasMaxLength(255).IsRequired(); b.Property(x => x.ContentType).HasMaxLength(120).IsRequired();
        b.Property(x => x.Sha256).HasMaxLength(64).IsRequired(); b.Property(x => x.ValidationStatus).HasMaxLength(40).IsRequired();
        b.HasIndex(x => x.ObjectKey).IsUnique(); b.HasIndex(x => x.MessageId); b.HasIndex(x => x.OfficialLetterId);
        b.HasOne(x => x.Message).WithMany(x => x.Attachments).HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.OfficialLetter).WithMany(x => x.Attachments).HasForeignKey(x => x.OfficialLetterId).OnDelete(DeleteBehavior.Cascade);
        b.ToTable(t => t.HasCheckConstraint("ck_collaboration_attachments_owner", "num_nonnulls(\"MessageId\", \"OfficialLetterId\") = 1"));
        b.ToTable(t => t.HasCheckConstraint("ck_collaboration_attachments_size", "\"Size\" > 0 AND \"Size\" <= 26214400"));
    }
}

internal sealed class CallSessionConfiguration : IEntityTypeConfiguration<CallSession>
{
    public void Configure(EntityTypeBuilder<CallSession> b)
    {
        b.ToTable("call_sessions", "collaboration"); b.HasKey(x => x.Id);
        b.Property(x => x.Provider).HasMaxLength(30).IsRequired(); b.Property(x => x.RoomName).HasMaxLength(180).IsRequired();
        b.Property(x => x.Status).HasMaxLength(20).IsRequired(); b.Property(x => x.HostDisplayName).HasMaxLength(200).IsRequired();
        b.Property(x => x.Version).IsConcurrencyToken(); b.HasIndex(x => x.RoomName).IsUnique();
        b.HasIndex(x => new { x.ConversationId, x.Status });
        b.HasIndex(x => x.ConversationId)
            .HasDatabaseName("ux_call_sessions_one_active_per_conversation")
            .IsUnique()
            .HasFilter("\"Status\" = 'ACTIVE'");
        b.HasOne(x => x.Conversation).WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
        b.ToTable(t => t.HasCheckConstraint("ck_call_sessions_status", "\"Status\" IN ('ACTIVE','ENDED')"));
    }
}
