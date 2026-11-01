namespace PeerCare.Contracts;

public sealed record OtpStartRequest(string Contact, string? ContactType);
public sealed record OtpVerifyRequest(string Contact, string Code, string? ContactType);
public sealed record OnboardingRequest(string DisplayName, string? State, string? City, IReadOnlyList<Guid>? Conditions);
public sealed record ConsentRequest(bool Accepted, bool EmailNotifications, bool InAppNotifications);
public sealed record SettingsRequest(string DisplayName, string? State, string? City, bool EmailNotifications, bool InAppNotifications, string? Bio = null, string? ProfileVisibility = null);
public sealed record TreatmentInput(string Name, string Kind, string Outcome, string? Note);
public sealed record FacilityInput(string Name, string? City, string? State, string? Practitioner, string Result);
public sealed record CreatePostRequest(
    string Kind,
    Guid ConditionId,
    string AgeGroup,
    string About,
    string Title,
    string Symptoms,
    string DurationText,
    string Story,
    string Outcome,
    string? HelpNeeded,
    string? WhatFailed,
    string? WhatWorked,
    string? TimeToResolve,
    bool IsAnonymous,
    string? State,
    string? City,
    IReadOnlyList<TreatmentInput>? Treatments,
    IReadOnlyList<FacilityInput>? Facilities,
    string? DiagnosisStatus = null);
public sealed record EditPostRequest(string Title, string Symptoms, string DurationText, string Story, string Outcome, string? WhatFailed, string? WhatWorked, string? DiagnosisStatus = null);
public sealed record ReplyRequest(string Body, Guid? ParentId, bool ProfessionalNote);
public sealed record ReportRequest(string TargetType, Guid TargetId, string Reason, string? Detail);
public sealed record ReactionRequest(string Kind);
public sealed record ProfessionalVerificationRequest(string Profession, string LicenceNumber, string IssuingBody);
public sealed record AdminPostActionRequest(string Action, string Reason, string? CleanedStory, string? CleanedSymptoms);
public sealed record AdminReportActionRequest(string Status, string Note);
public sealed record AdminVerificationRequest(string Status, string? RejectReason);
public sealed record FacilityIssueRequest(string Detail);
public sealed record AdminSuspendRequest(string Reason, bool Suspended);
public sealed record HealthEntryRequest(string EntryType, string? Title, string Description, DateTimeOffset? OccurredAt, int? Severity);
public sealed record ProviderReviewRequest(int? Rating, string ReviewText, DateOnly? VisitDate);
public sealed record UserAppealRequest(Guid ActionId, string Reason);
public sealed record AdminAppealRequest(string Status, string? DecisionNote);
public sealed record AdminConditionRequest(string Name, string Category, bool Active = true);
public sealed record AdminCommunityRequest(string Name, string Slug, string Description, string Guidelines, string Status, Guid? ConditionId);
public sealed record FacilityVerificationRequest(string? Source, DateTimeOffset? LastVerifiedAt);
public sealed record ShareHealthEntriesRequest(Guid[] EntryIds, Guid ConditionId, string Title, bool Confirmed);
