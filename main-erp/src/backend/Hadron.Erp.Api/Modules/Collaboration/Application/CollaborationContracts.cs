namespace Hadron.Erp.Api.Modules.Collaboration.Application;

public sealed record CollaborationActor(Guid UserId, string DisplayName, bool IsSystemAdministrator);
public sealed record CollaborationPage<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
public sealed record DirectoryUserResponse(Guid Id, string UserName, string FullName, string OrganizationalUnit,
    string PhotoUrl);
public sealed record ConversationMemberResponse(Guid UserId, string DisplayName, string Role, DateTime JoinedAtUtc,
    DateTime? LastReadAtUtc);
public sealed record ConversationSummaryResponse(Guid Id, string Type, string Name, string? Description,
    bool IsPrivate, bool IsArchived, int MemberCount, int UnreadCount, DateTime? LastMessageAtUtc,
    string? LastMessagePreview, long Version);
public sealed record ConversationResponse(Guid Id, string Type, string Name, string? Description, bool IsPrivate,
    bool IsArchived, Guid CreatedByUserId, string CreatedByDisplayName, DateTime CreatedAtUtc, DateTime UpdatedAtUtc,
    long Version, IReadOnlyList<ConversationMemberResponse> Members, CallSessionResponse? ActiveCall);
public sealed record AttachmentResponse(Guid Id, string FileName, string ContentType, long Size, string Sha256,
    string ValidationStatus, string DownloadUrl, Guid UploadedByUserId, string UploadedByDisplayName, DateTime CreatedAtUtc);
public sealed record ReactionResponse(string Code, int Count, bool ReactedByCurrentUser);
public sealed record MessageResponse(Guid Id, Guid ConversationId, Guid AuthorUserId, string AuthorDisplayName,
    string Type, string? Body, string? StickerCode, Guid? ReplyToMessageId, Guid? OfficialLetterId,
    DateTime CreatedAtUtc, DateTime? EditedAtUtc, DateTime? DeletedAtUtc, long Version,
    IReadOnlyList<ReactionResponse> Reactions, IReadOnlyList<AttachmentResponse> Attachments);

public sealed record CreateChannelRequest(string Name, string? Description, bool IsPrivate,
    IReadOnlyList<Guid>? MemberUserIds);
public sealed record CreateDirectConversationRequest(Guid OtherUserId);
public sealed record UpdateConversationRequest(string Name, string? Description, bool IsPrivate, long ExpectedVersion,
    IReadOnlyList<Guid>? MemberUserIds);
public sealed record ArchiveConversationRequest(long ExpectedVersion);
public sealed record SendMessageRequest(string Type, string? Body, string? StickerCode, Guid? ReplyToMessageId);
public sealed record EditMessageRequest(string Body, long ExpectedVersion);
public sealed record DeleteMessageRequest(long ExpectedVersion);
public sealed record ToggleReactionRequest(string Code);
public sealed record ResolveCollaborationJalaliDateRequest(string JalaliDate);

public sealed record LetterRecipientInput(Guid UserId, string Type);
public sealed record CreateOfficialLetterRequest(string Subject, string Body, string Classification, DateOnly? DueDate,
    IReadOnlyList<LetterRecipientInput> Recipients);
public sealed record UpdateOfficialLetterRequest(string Subject, string Body, string Classification, DateOnly? DueDate,
    long ExpectedVersion, IReadOnlyList<LetterRecipientInput> Recipients);
public sealed record LetterTransitionRequest(long ExpectedVersion, string? Note);
public sealed record OfficialLetterRecipientResponse(Guid UserId, string DisplayName, string Type);
public sealed record OfficialLetterEventResponse(Guid Id, string EventType, Guid ActorUserId, string ActorDisplayName,
    string? Note, DateTime OccurredAtUtc);
public sealed record OfficialLetterSummaryResponse(Guid Id, string Number, string Subject, string Classification,
    string Status, Guid SenderUserId, string SenderDisplayName, DateOnly? DueDate, DateTime? IssuedAtUtc,
    DateTime CreatedAtUtc, long Version);
public sealed record OfficialLetterResponse(Guid Id, string Number, string Subject, string Body, string Classification,
    string Status, Guid SenderUserId, string SenderDisplayName, DateOnly? DueDate, DateTime? IssuedAtUtc,
    DateTime? AcknowledgedAtUtc, DateTime? ClosedAtUtc, DateTime CreatedAtUtc, DateTime UpdatedAtUtc, long Version,
    IReadOnlyList<OfficialLetterRecipientResponse> Recipients, IReadOnlyList<OfficialLetterEventResponse> Events,
    IReadOnlyList<AttachmentResponse> Attachments);

public sealed record CallConfigurationResponse(bool Available, string? ServerUrl, string Provider, string? Reason);
public sealed record StartCallRequest(bool VideoEnabled);
public sealed record EndCallRequest(long ExpectedVersion);
public sealed record CallSessionResponse(Guid Id, Guid ConversationId, string Provider, string RoomName, string Status,
    Guid HostUserId, string HostDisplayName, bool VideoEnabled, DateTime StartedAtUtc, DateTime? EndedAtUtc, long Version);
public sealed record CallJoinResponse(
    CallSessionResponse Call,
    string ServerUrl,
    string AccessToken,
    bool CreatedNew);

public sealed record CollaborationNotificationItemResponse(string Kind, Guid EntityId, Guid? ConversationId,
    string Title, string? Preview, string? ActorDisplayName, DateTime OccurredAtUtc, int Count,
    bool RequiresAction, bool VideoEnabled, string TargetUrl);
public sealed record CollaborationNotificationSummaryResponse(int TotalCount, int UnreadMessageCount,
    int PendingLetterCount, int ActiveCallCount, DateTime GeneratedAtUtc,
    IReadOnlyList<CollaborationNotificationItemResponse> Items);

public sealed class CollaborationValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Collaboration validation failed.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

public sealed class CollaborationNotFoundException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
