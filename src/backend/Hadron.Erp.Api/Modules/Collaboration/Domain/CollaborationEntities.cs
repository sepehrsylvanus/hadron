namespace Hadron.Erp.Api.Modules.Collaboration.Domain;

public static class ConversationTypes
{
    public const string Channel = "CHANNEL";
    public const string Direct = "DIRECT";
}

public static class ConversationMemberRoles
{
    public const string Owner = "OWNER";
    public const string Member = "MEMBER";
}

public static class MessageTypes
{
    public const string Text = "TEXT";
    public const string Sticker = "STICKER";
    public const string System = "SYSTEM";
    public const string LetterReference = "LETTER_REFERENCE";
    public const string CallEvent = "CALL_EVENT";
}

public static class LetterStatuses
{
    public const string Draft = "DRAFT";
    public const string Issued = "ISSUED";
    public const string Acknowledged = "ACKNOWLEDGED";
    public const string Closed = "CLOSED";
}

public static class LetterClassifications
{
    public const string Normal = "NORMAL";
    public const string Confidential = "CONFIDENTIAL";
    public const string HighlyConfidential = "HIGHLY_CONFIDENTIAL";
}

public static class LetterRecipientTypes
{
    public const string To = "TO";
    public const string Cc = "CC";
}

public static class CallStatuses
{
    public const string Active = "ACTIVE";
    public const string Ended = "ENDED";
}

public sealed class Conversation
{
    private Conversation() { }

    public Conversation(string type, string name, string? description, bool isPrivate,
        Guid createdByUserId, string createdByDisplayName)
    {
        Id = Guid.NewGuid();
        Type = type;
        Name = name.Trim();
        Description = Normalize(description);
        IsPrivate = isPrivate || type == ConversationTypes.Direct;
        CreatedByUserId = createdByUserId;
        CreatedByDisplayName = createdByDisplayName.Trim();
        CreatedAtUtc = UpdatedAtUtc = DateTime.UtcNow;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public bool IsPrivate { get; private set; }
    public bool IsArchived { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public string CreatedByDisplayName { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public long Version { get; private set; }
    public ICollection<ConversationMember> Members { get; private set; } = [];
    public ICollection<ConversationMessage> Messages { get; private set; } = [];

    public void Update(string name, string? description, bool isPrivate, long expectedVersion)
    {
        EnsureVersion(expectedVersion);
        Name = name.Trim();
        Description = Normalize(description);
        IsPrivate = isPrivate || Type == ConversationTypes.Direct;
        Touch();
    }

    public void Archive(long expectedVersion)
    {
        EnsureVersion(expectedVersion);
        IsArchived = true;
        Touch();
    }

    private void Touch() { Version++; UpdatedAtUtc = DateTime.UtcNow; }
    private void EnsureVersion(long expectedVersion)
    {
        if (Version != expectedVersion) throw new CollaborationConflictException(
            "CONVERSATION_VERSION_CONFLICT", "گفتگو توسط کاربر دیگری تغییر کرده است؛ صفحه را تازه‌سازی کنید.");
    }
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class ConversationMember
{
    private ConversationMember() { }
    public ConversationMember(Guid conversationId, Guid userId, string displayName, string role)
    {
        ConversationId = conversationId;
        UserId = userId;
        DisplayName = displayName.Trim();
        Role = role;
        JoinedAtUtc = DateTime.UtcNow;
    }

    public Guid ConversationId { get; private set; }
    public Conversation Conversation { get; private set; } = null!;
    public Guid UserId { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public string Role { get; private set; } = string.Empty;
    public DateTime JoinedAtUtc { get; private set; }
    public DateTime? LastReadAtUtc { get; private set; }

    public void MarkRead(DateTime atUtc) => LastReadAtUtc = atUtc;
}

public sealed class ConversationMessage
{
    private ConversationMessage() { }
    public ConversationMessage(Guid conversationId, Guid authorUserId, string authorDisplayName,
        string type, string? body, string? stickerCode, Guid? replyToMessageId, Guid? officialLetterId)
    {
        Id = Guid.NewGuid();
        ConversationId = conversationId;
        AuthorUserId = authorUserId;
        AuthorDisplayName = authorDisplayName.Trim();
        Type = type;
        Body = Normalize(body);
        StickerCode = Normalize(stickerCode);
        ReplyToMessageId = replyToMessageId;
        OfficialLetterId = officialLetterId;
        CreatedAtUtc = DateTime.UtcNow;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid ConversationId { get; private set; }
    public Conversation Conversation { get; private set; } = null!;
    public Guid AuthorUserId { get; private set; }
    public string AuthorDisplayName { get; private set; } = string.Empty;
    public string Type { get; private set; } = string.Empty;
    public string? Body { get; private set; }
    public string? StickerCode { get; private set; }
    public Guid? ReplyToMessageId { get; private set; }
    public Guid? OfficialLetterId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? EditedAtUtc { get; private set; }
    public DateTime? DeletedAtUtc { get; private set; }
    public long Version { get; private set; }
    public ICollection<MessageReaction> Reactions { get; private set; } = [];
    public ICollection<CollaborationAttachment> Attachments { get; private set; } = [];

    public void Edit(string body, long expectedVersion)
    {
        EnsureMutable(expectedVersion);
        if (Type != MessageTypes.Text) throw new CollaborationConflictException(
            "MESSAGE_TYPE_IMMUTABLE", "فقط پیام متنی قابل ویرایش است.");
        Body = body.Trim();
        EditedAtUtc = DateTime.UtcNow;
        Version++;
    }

    public void Delete(long expectedVersion)
    {
        EnsureMutable(expectedVersion);
        DeletedAtUtc = DateTime.UtcNow;
        Body = null;
        StickerCode = null;
        Version++;
    }

    private void EnsureMutable(long expectedVersion)
    {
        if (Version != expectedVersion) throw new CollaborationConflictException(
            "MESSAGE_VERSION_CONFLICT", "پیام تغییر کرده است؛ گفتگو را تازه‌سازی کنید.");
        if (DeletedAtUtc.HasValue) throw new CollaborationConflictException(
            "MESSAGE_ALREADY_DELETED", "این پیام قبلاً حذف شده است.");
        if (Type is MessageTypes.System or MessageTypes.LetterReference or MessageTypes.CallEvent)
            throw new CollaborationConflictException("MESSAGE_IMMUTABLE", "این رویداد سیستمی قابل تغییر نیست.");
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class MessageReaction
{
    private MessageReaction() { }
    public MessageReaction(Guid messageId, Guid userId, string code)
    {
        Id = Guid.NewGuid(); MessageId = messageId; UserId = userId; Code = code; CreatedAtUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid MessageId { get; private set; }
    public ConversationMessage Message { get; private set; } = null!;
    public Guid UserId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
}

public sealed class OfficialLetter
{
    private OfficialLetter() { }
    public OfficialLetter(string number, string subject, string body, string classification,
        Guid senderUserId, string senderDisplayName, DateOnly? dueDate)
    {
        Id = Guid.NewGuid(); Number = number; Subject = subject.Trim(); Body = body.Trim();
        Classification = classification; Status = LetterStatuses.Draft; SenderUserId = senderUserId;
        SenderDisplayName = senderDisplayName.Trim(); DueDate = dueDate;
        CreatedAtUtc = UpdatedAtUtc = DateTime.UtcNow; Version = 1;
    }

    public Guid Id { get; private set; }
    public string Number { get; private set; } = string.Empty;
    public string Subject { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;
    public string Classification { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public Guid SenderUserId { get; private set; }
    public string SenderDisplayName { get; private set; } = string.Empty;
    public DateOnly? DueDate { get; private set; }
    public DateTime? IssuedAtUtc { get; private set; }
    public DateTime? AcknowledgedAtUtc { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public long Version { get; private set; }
    public ICollection<OfficialLetterRecipient> Recipients { get; private set; } = [];
    public ICollection<OfficialLetterEvent> Events { get; private set; } = [];
    public ICollection<CollaborationAttachment> Attachments { get; private set; } = [];

    public void Update(string subject, string body, string classification, DateOnly? dueDate, long expectedVersion)
    {
        EnsureDraft(expectedVersion);
        Subject = subject.Trim(); Body = body.Trim(); Classification = classification; DueDate = dueDate; Touch();
    }

    public void Issue(long expectedVersion)
    {
        EnsureDraft(expectedVersion);
        if (Recipients.All(x => x.Type != LetterRecipientTypes.To)) throw new CollaborationConflictException(
            "LETTER_RECIPIENT_REQUIRED", "برای صدور نامه حداقل یک گیرنده اصلی لازم است.");
        Status = LetterStatuses.Issued; IssuedAtUtc = DateTime.UtcNow; Touch();
    }

    public void Acknowledge(long expectedVersion)
    {
        EnsureVersion(expectedVersion);
        if (Status != LetterStatuses.Issued) throw new CollaborationConflictException(
            "LETTER_NOT_ISSUED", "فقط نامه صادرشده قابل اعلام دریافت است.");
        Status = LetterStatuses.Acknowledged; AcknowledgedAtUtc = DateTime.UtcNow; Touch();
    }

    public void Close(long expectedVersion)
    {
        EnsureVersion(expectedVersion);
        if (Status is not (LetterStatuses.Issued or LetterStatuses.Acknowledged))
            throw new CollaborationConflictException("LETTER_NOT_CLOSABLE", "وضعیت نامه برای بستن معتبر نیست.");
        Status = LetterStatuses.Closed; ClosedAtUtc = DateTime.UtcNow; Touch();
    }

    private void EnsureDraft(long expectedVersion)
    {
        EnsureVersion(expectedVersion);
        if (Status != LetterStatuses.Draft) throw new CollaborationConflictException(
            "ISSUED_LETTER_IMMUTABLE", "نامه صادرشده تغییرناپذیر است؛ برای اصلاح، نامه جدید ایجاد کنید.");
    }
    private void EnsureVersion(long expectedVersion)
    {
        if (Version != expectedVersion) throw new CollaborationConflictException(
            "LETTER_VERSION_CONFLICT", "نامه توسط کاربر دیگری تغییر کرده است؛ صفحه را تازه‌سازی کنید.");
    }
    private void Touch() { Version++; UpdatedAtUtc = DateTime.UtcNow; }
}

public sealed class OfficialLetterRecipient
{
    private OfficialLetterRecipient() { }
    public OfficialLetterRecipient(Guid officialLetterId, Guid userId, string displayName, string type)
    { Id = Guid.NewGuid(); OfficialLetterId = officialLetterId; UserId = userId; DisplayName = displayName.Trim(); Type = type; }
    public Guid Id { get; private set; }
    public Guid OfficialLetterId { get; private set; }
    public OfficialLetter OfficialLetter { get; private set; } = null!;
    public Guid UserId { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public string Type { get; private set; } = string.Empty;
}

public sealed class OfficialLetterEvent
{
    private OfficialLetterEvent() { }
    public OfficialLetterEvent(Guid officialLetterId, string eventType, Guid actorUserId,
        string actorDisplayName, string? note)
    { Id = Guid.NewGuid(); OfficialLetterId = officialLetterId; EventType = eventType; ActorUserId = actorUserId;
      ActorDisplayName = actorDisplayName.Trim(); Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(); OccurredAtUtc = DateTime.UtcNow; }
    public Guid Id { get; private set; }
    public Guid OfficialLetterId { get; private set; }
    public OfficialLetter OfficialLetter { get; private set; } = null!;
    public string EventType { get; private set; } = string.Empty;
    public Guid ActorUserId { get; private set; }
    public string ActorDisplayName { get; private set; } = string.Empty;
    public string? Note { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
}

public sealed class CollaborationAttachment
{
    private CollaborationAttachment() { }
    public CollaborationAttachment(Guid? messageId, Guid? officialLetterId, Guid uploadedByUserId,
        string uploadedByDisplayName, string objectKey, string originalFileName, string contentType,
        long size, string sha256, string validationStatus)
    {
        Id = Guid.NewGuid(); MessageId = messageId; OfficialLetterId = officialLetterId;
        UploadedByUserId = uploadedByUserId; UploadedByDisplayName = uploadedByDisplayName.Trim();
        ObjectKey = objectKey; OriginalFileName = originalFileName; ContentType = contentType;
        Size = size; Sha256 = sha256; ValidationStatus = validationStatus; CreatedAtUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid? MessageId { get; private set; }
    public ConversationMessage? Message { get; private set; }
    public Guid? OfficialLetterId { get; private set; }
    public OfficialLetter? OfficialLetter { get; private set; }
    public Guid UploadedByUserId { get; private set; }
    public string UploadedByDisplayName { get; private set; } = string.Empty;
    public string ObjectKey { get; private set; } = string.Empty;
    public string OriginalFileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long Size { get; private set; }
    public string Sha256 { get; private set; } = string.Empty;
    public string ValidationStatus { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
}

public sealed class CallSession
{
    private CallSession() { }
    public CallSession(Guid conversationId, string roomName, Guid hostUserId, string hostDisplayName,
        bool videoEnabled)
    {
        Id = Guid.NewGuid(); ConversationId = conversationId; RoomName = roomName;
        HostUserId = hostUserId; HostDisplayName = hostDisplayName.Trim(); VideoEnabled = videoEnabled;
        Provider = "LIVEKIT"; Status = CallStatuses.Active; StartedAtUtc = DateTime.UtcNow; Version = 1;
    }
    public Guid Id { get; private set; }
    public Guid ConversationId { get; private set; }
    public Conversation Conversation { get; private set; } = null!;
    public string Provider { get; private set; } = string.Empty;
    public string RoomName { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public Guid HostUserId { get; private set; }
    public string HostDisplayName { get; private set; } = string.Empty;
    public bool VideoEnabled { get; private set; }
    public DateTime StartedAtUtc { get; private set; }
    public DateTime? EndedAtUtc { get; private set; }
    public long Version { get; private set; }
    public void End(long expectedVersion)
    {
        if (Version != expectedVersion) throw new CollaborationConflictException(
            "CALL_VERSION_CONFLICT", "وضعیت تماس تغییر کرده است؛ گفتگو را تازه‌سازی کنید.");
        if (Status == CallStatuses.Ended) return;
        Status = CallStatuses.Ended; EndedAtUtc = DateTime.UtcNow; Version++;
    }
}

public sealed class CollaborationConflictException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
