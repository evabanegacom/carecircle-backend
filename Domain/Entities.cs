using Microsoft.AspNetCore.Identity;
using NpgsqlTypes;

namespace PeerCare.Domain;

public sealed class PeerUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = "";
    public string? NormalizedDisplayName { get; set; }
    public string? Bio { get; set; }
    public string ProfileVisibility { get; set; } = "public";
    public string? State { get; set; }
    public string? City { get; set; }
    public string Status { get; set; } = "active";
    public string? PhoneBlindIndex { get; set; }
    public string ConsentVersion { get; set; } = "";
    public DateTimeOffset? ConsentedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeleteRequestedAt { get; set; }
    public bool InAppNotifications { get; set; } = true;
    public bool EmailNotifications { get; set; }
    public ICollection<PeerPost> Posts { get; set; } = new List<PeerPost>();
}

public sealed class PeerRole : IdentityRole<Guid> { }

public sealed class ConsentRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Version { get; set; } = "";
    public DateTimeOffset AcceptedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class UserCondition
{
    public Guid UserId { get; set; }
    public Guid ConditionId { get; set; }
}

public sealed class Condition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Category { get; set; } = "child-health";
    public Guid? ParentId { get; set; }
    public bool Active { get; set; } = true;
}

public sealed class Treatment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "other";
    public bool Approved { get; set; }
}

public sealed class PeerPost
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public PeerUser? User { get; set; }
    public string Kind { get; set; } = "experience";
    public Guid ConditionId { get; set; }
    public Condition? Condition { get; set; }
    public string AgeGroup { get; set; } = "";
    public string About { get; set; } = "child";
    public string Title { get; set; } = "";
    public string Symptoms { get; set; } = "";
    public string DurationText { get; set; } = "";
    public string Story { get; set; } = "";
    public string Outcome { get; set; } = "unknown";
    public string DiagnosisStatus { get; set; } = "not_stated";
    public string? HelpNeeded { get; set; }
    public string? WhatFailed { get; set; }
    public string? WhatWorked { get; set; }
    public string? TimeToResolve { get; set; }
    public bool IsAnonymous { get; set; }
    public string State { get; set; } = "Lagos";
    public string City { get; set; } = "Lagos";
    public string Status { get; set; } = "pending";
    public bool EmergencyFlag { get; set; }
    public int FlagsCount { get; set; }
    public string AutoFlagsJson { get; set; } = "[]";
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public NpgsqlTsVector SearchVector { get; set; } = null!;
    public ICollection<PostTreatment> Treatments { get; set; } = new List<PostTreatment>();
    public ICollection<PostFacility> Facilities { get; set; } = new List<PostFacility>();
    public ICollection<PeerReply> Replies { get; set; } = new List<PeerReply>();
}

public sealed class PostTreatment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PostId { get; set; }
    public PeerPost? Post { get; set; }
    public Guid TreatmentId { get; set; }
    public Treatment? Treatment { get; set; }
    public string Outcome { get; set; } = "unsure";
    public string? Note { get; set; }
}

public sealed class Facility
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Type { get; set; } = "hospital";
    public string? Address { get; set; }
    public string State { get; set; } = "Lagos";
    public string City { get; set; } = "Lagos";
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string Status { get; set; } = "unverified";
    public string? Source { get; set; }
    public DateTimeOffset? LastVerifiedAt { get; set; }
    public Guid? CreatedBy { get; set; }
}

public sealed class Community
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Description { get; set; } = "";
    public string Guidelines { get; set; } = "";
    public string Status { get; set; } = "active";
    public Guid? ConditionId { get; set; }
    public Condition? Condition { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class CommunityMembership
{
    public Guid CommunityId { get; set; }
    public Guid UserId { get; set; }
    public string Status { get; set; } = "joined";
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ProviderReview
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FacilityId { get; set; }
    public Facility? Facility { get; set; }
    public Guid UserId { get; set; }
    public PeerUser? User { get; set; }
    public int? Rating { get; set; }
    public string ReviewText { get; set; } = "";
    public DateOnly? VisitDate { get; set; }
    public string Status { get; set; } = "pending";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class UserBlock
{
    public Guid BlockerId { get; set; }
    public Guid BlockedId { get; set; }
    public string Kind { get; set; } = "block";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ModerationAppeal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid ActionId { get; set; }
    public ModerationAction? Action { get; set; }
    public PeerUser? User { get; set; }
    public string Reason { get; set; } = "";
    public string Status { get; set; } = "open";
    public string? DecisionNote { get; set; }
    public Guid? ReviewedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReviewedAt { get; set; }
}

public sealed class Practitioner
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FacilityId { get; set; }
    public Facility? Facility { get; set; }
    public string Name { get; set; } = "";
    public string? Specialty { get; set; }
    public string Status { get; set; } = "hidden";
    public int ReportCount { get; set; }
}

public sealed class PostFacility
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PostId { get; set; }
    public PeerPost? Post { get; set; }
    public Guid FacilityId { get; set; }
    public Facility? Facility { get; set; }
    public Guid? PractitionerId { get; set; }
    public Practitioner? Practitioner { get; set; }
    public string Result { get; set; } = "helped_us";
}

public sealed class PeerReply
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PostId { get; set; }
    public PeerPost? Post { get; set; }
    public Guid UserId { get; set; }
    public PeerUser? User { get; set; }
    public Guid? ParentId { get; set; }
    public string Body { get; set; } = "";
    public bool IsProfessionalNote { get; set; }
    public string Status { get; set; } = "published";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PeerReaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string TargetType { get; set; } = "post";
    public Guid TargetId { get; set; }
    public string Kind { get; set; } = "helped_me_too";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PeerSave
{
    public Guid UserId { get; set; }
    public Guid PostId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PeerReport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ReporterId { get; set; }
    public string TargetType { get; set; } = "post";
    public Guid TargetId { get; set; }
    public string Reason { get; set; } = "other";
    public string? Detail { get; set; }
    public string Status { get; set; } = "open";
    public Guid? HandledBy { get; set; }
    public DateTimeOffset? HandledAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ModerationAction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ModeratorId { get; set; }
    public string TargetType { get; set; } = "post";
    public Guid TargetId { get; set; }
    public string Action { get; set; } = "approve";
    public string Reason { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PeerNotification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Kind { get; set; } = "reply";
    public string PayloadJson { get; set; } = "{}";
    public DateTimeOffset? ReadAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PeerConsent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Version { get; set; } = "";
    public DateTimeOffset AcceptedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PeerAuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ActorId { get; set; }
    public string Action { get; set; } = "";
    public string SubjectType { get; set; } = "";
    public Guid? SubjectId { get; set; }
    public string? IpHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ProfessionalVerification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public PeerUser? User { get; set; }
    public string Profession { get; set; } = "";
    public string LicenceNumber { get; set; } = "";
    public string IssuingBody { get; set; } = "";
    public string Status { get; set; } = "pending";
    public Guid? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? RejectReason { get; set; }
}

public sealed class OtpChallenge
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ContactType { get; set; } = "email";
    public string ContactBlindIndex { get; set; } = "";
    public string Contact { get; set; } = "";
    public string CodeHash { get; set; } = "";
    public int FailedAttempts { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class FacilityIssue
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FacilityId { get; set; }
    public Facility? Facility { get; set; }
    public Guid ReporterId { get; set; }
    public string Detail { get; set; } = "";
    public string Status { get; set; } = "open";
    public Guid? HandledBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class HealthEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string EntryType { get; set; } = "general_note";
    public string? Title { get; set; }
    public string Description { get; set; } = "";
    public DateTimeOffset? OccurredAt { get; set; }
    public int? Severity { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }
}
