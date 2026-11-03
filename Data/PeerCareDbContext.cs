using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PeerCare.Domain;
using PeerCare.Services;

namespace PeerCare.Data;

public sealed class PeerCareDbContext(DbContextOptions<PeerCareDbContext> options, ContactCrypto crypto)
    : IdentityDbContext<PeerUser, PeerRole, Guid>(options)
{
    public DbSet<PeerPost> Posts => Set<PeerPost>();
    public DbSet<PeerReply> Replies => Set<PeerReply>();
    public DbSet<Condition> Conditions => Set<Condition>();
    public DbSet<Treatment> Treatments => Set<Treatment>();
    public DbSet<PostTreatment> PostTreatments => Set<PostTreatment>();
    public DbSet<Facility> Facilities => Set<Facility>();
    public DbSet<Practitioner> Practitioners => Set<Practitioner>();
    public DbSet<PostFacility> PostFacilities => Set<PostFacility>();
    public DbSet<PeerReaction> Reactions => Set<PeerReaction>();
    public DbSet<PeerSave> Saves => Set<PeerSave>();
    public DbSet<PeerReport> Reports => Set<PeerReport>();
    public DbSet<ModerationAction> ModerationActions => Set<ModerationAction>();
    public DbSet<PeerNotification> Notifications => Set<PeerNotification>();
    public DbSet<PeerConsent> Consents => Set<PeerConsent>();
    public DbSet<PeerAuditLog> AuditLogs => Set<PeerAuditLog>();
    public DbSet<ProfessionalVerification> ProfessionalVerifications => Set<ProfessionalVerification>();
    public DbSet<OtpChallenge> OtpChallenges => Set<OtpChallenge>();
    public DbSet<UserCondition> UserConditions => Set<UserCondition>();
    public DbSet<FacilityIssue> FacilityIssues => Set<FacilityIssue>();
    public DbSet<HealthEntry> HealthEntries => Set<HealthEntry>();
    public DbSet<Community> Communities => Set<Community>();
    public DbSet<CommunityMembership> CommunityMemberships => Set<CommunityMembership>();
    public DbSet<ProviderReview> ProviderReviews => Set<ProviderReview>();
    public DbSet<UserBlock> UserBlocks => Set<UserBlock>();
    public DbSet<ModerationAppeal> ModerationAppeals => Set<ModerationAppeal>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var emailConverter = new ValueConverter<string?, string?>(v => v == null ? null : crypto.ProtectEmail(v), v => v == null ? null : crypto.UnprotectEmail(v));
        var phoneConverter = new ValueConverter<string?, string?>(v => v == null ? null : crypto.ProtectPhone(v), v => v == null ? null : crypto.UnprotectPhone(v));
        var challengeContactConverter = new ValueConverter<string, string>(v => crypto.ProtectEmail(v), v => crypto.UnprotectEmail(v));
        var licenceConverter = new ValueConverter<string, string>(v => crypto.ProtectLicence(v), v => crypto.UnprotectLicence(v));
        modelBuilder.Entity<PeerUser>(entity =>
        {
            entity.Property(u => u.Email).HasConversion(emailConverter);
            entity.Property(u => u.PhoneNumber).HasConversion(phoneConverter);
            entity.Property(u => u.DisplayName).HasMaxLength(80);
            entity.Property(u => u.NormalizedDisplayName).HasMaxLength(80);
            entity.HasIndex(u => u.NormalizedDisplayName).IsUnique()
                .HasFilter("\"NormalizedDisplayName\" IS NOT NULL");
            entity.Property(u => u.Bio).HasMaxLength(500);
            entity.Property(u => u.ProfileVisibility).HasMaxLength(24).HasDefaultValue("public");
            entity.Property(u => u.PhoneBlindIndex).HasMaxLength(64);
            entity.HasIndex(u => u.PhoneBlindIndex).IsUnique().HasFilter("\"PhoneBlindIndex\" IS NOT NULL");
            entity.HasIndex(u => u.NormalizedEmail).IsUnique().HasFilter("\"NormalizedEmail\" IS NOT NULL");
            entity.HasIndex(u => u.Status);
        });
        modelBuilder.Entity<PeerRole>().ToTable("roles");
        modelBuilder.Entity<PeerUser>().ToTable("users");
        modelBuilder.Entity<Microsoft.AspNetCore.Identity.IdentityUserRole<Guid>>().ToTable("user_roles");
        modelBuilder.Entity<Microsoft.AspNetCore.Identity.IdentityUserClaim<Guid>>().ToTable("user_claims");
        modelBuilder.Entity<Microsoft.AspNetCore.Identity.IdentityUserLogin<Guid>>().ToTable("user_logins");
        modelBuilder.Entity<Microsoft.AspNetCore.Identity.IdentityRoleClaim<Guid>>().ToTable("role_claims");
        modelBuilder.Entity<Microsoft.AspNetCore.Identity.IdentityUserToken<Guid>>().ToTable("user_tokens");

        modelBuilder.Entity<Condition>(entity =>
        {
            entity.ToTable("conditions");
            entity.Property(c => c.Name).HasMaxLength(120);
            entity.Property(c => c.Slug).HasMaxLength(140);
            entity.HasIndex(c => c.Slug).IsUnique();
        });
        modelBuilder.Entity<Treatment>(entity =>
        {
            entity.ToTable("treatments");
            entity.Property(t => t.Name).HasMaxLength(160);
            entity.HasIndex(t => t.Name).IsUnique();
        });
        modelBuilder.Entity<PeerPost>(entity =>
        {
            entity.ToTable("posts");
            entity.Property(p => p.Kind).HasMaxLength(24);
            entity.Property(p => p.AgeGroup).HasMaxLength(40);
            entity.Property(p => p.About).HasMaxLength(24);
            entity.Property(p => p.Title).HasMaxLength(180);
            entity.Property(p => p.Outcome).HasMaxLength(24);
            entity.Property(p => p.DiagnosisStatus).HasMaxLength(24).HasDefaultValue("not_stated");
            entity.Property(p => p.State).HasMaxLength(80);
            entity.Property(p => p.City).HasMaxLength(100);
            entity.Property(p => p.Status).HasMaxLength(24);
            if (Database.IsNpgsql())
            {
                entity.Property(p => p.AutoFlagsJson).HasColumnType("jsonb");
                entity.Property(p => p.SearchVector).HasColumnType("tsvector")
                    .HasComputedColumnSql("setweight(to_tsvector('english', coalesce(\"Title\", '')), 'A') || setweight(to_tsvector('english', coalesce(\"Symptoms\", '')), 'B') || setweight(to_tsvector('english', coalesce(\"Story\", '')), 'C') || setweight(to_tsvector('english', coalesce(\"WhatFailed\", '') || ' ' || coalesce(\"WhatWorked\", '') || ' ' || coalesce(\"HelpNeeded\", '')), 'D')", stored: true);
                entity.HasIndex(p => p.SearchVector).HasMethod("GIN");
            }
            else entity.Ignore(p => p.SearchVector);
            entity.HasIndex(p => new { p.ConditionId, p.Status, p.PublishedAt });
            entity.HasIndex(p => new { p.State, p.City });
            entity.HasIndex(p => new { p.UserId, p.CreatedAt });
            entity.HasOne(p => p.User).WithMany(u => u.Posts).HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(p => p.Condition).WithMany().HasForeignKey(p => p.ConditionId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<PostTreatment>(entity =>
        {
            entity.ToTable("post_treatments");
            entity.Property(t => t.Outcome).HasMaxLength(24);
            entity.HasIndex(t => t.TreatmentId);
            entity.HasOne(t => t.Post).WithMany(p => p.Treatments).HasForeignKey(t => t.PostId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(t => t.Treatment).WithMany().HasForeignKey(t => t.TreatmentId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<Facility>(entity =>
        {
            entity.ToTable("facilities");
            entity.Property(f => f.Name).HasMaxLength(180);
            entity.Property(f => f.Status).HasMaxLength(24);
            entity.Property(f => f.Source).HasMaxLength(300);
            entity.HasIndex(f => new { f.Name, f.City });
        });
        modelBuilder.Entity<Practitioner>(entity =>
        {
            entity.ToTable("practitioners");
            entity.Property(p => p.Name).HasMaxLength(140);
            entity.Property(p => p.Status).HasMaxLength(24);
            entity.HasOne(p => p.Facility).WithMany().HasForeignKey(p => p.FacilityId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<PostFacility>(entity =>
        {
            entity.ToTable("post_facilities");
            entity.Property(f => f.Result).HasMaxLength(24);
            entity.HasIndex(f => f.FacilityId);
            entity.HasOne(f => f.Post).WithMany(p => p.Facilities).HasForeignKey(f => f.PostId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(f => f.Facility).WithMany().HasForeignKey(f => f.FacilityId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(f => f.Practitioner).WithMany().HasForeignKey(f => f.PractitionerId).OnDelete(DeleteBehavior.SetNull);
        });
        modelBuilder.Entity<PeerReply>(entity =>
        {
            entity.ToTable("replies");
            entity.Property(r => r.Body).HasMaxLength(4000);
            entity.HasIndex(r => new { r.PostId, r.Status, r.CreatedAt });
            entity.HasOne(r => r.Post).WithMany(p => p.Replies).HasForeignKey(r => r.PostId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(r => r.User).WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<PeerReaction>(entity =>
        {
            entity.ToTable("reactions");
            entity.HasIndex(r => new { r.UserId, r.TargetType, r.TargetId, r.Kind }).IsUnique();
            entity.HasIndex(r => new { r.TargetType, r.TargetId, r.Kind });
        });
        modelBuilder.Entity<PeerSave>(entity =>
        {
            entity.ToTable("saves");
            entity.HasKey(s => new { s.UserId, s.PostId });
            entity.HasOne<PeerUser>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<PeerPost>().WithMany().HasForeignKey(s => s.PostId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<PeerReport>(entity =>
        {
            entity.ToTable("reports");
            entity.Property(r => r.Status).HasMaxLength(24);
            entity.Property(r => r.Reason).HasMaxLength(32);
            entity.HasIndex(r => new { r.Status, r.CreatedAt });
            entity.HasIndex(r => new { r.TargetType, r.TargetId });
        });
        modelBuilder.Entity<ModerationAction>(entity =>
        {
            entity.ToTable("moderation_actions");
            entity.HasIndex(a => new { a.TargetType, a.TargetId, a.CreatedAt });
        });
        modelBuilder.Entity<PeerNotification>(entity =>
        {
            entity.ToTable("notifications");
            if (Database.IsNpgsql()) entity.Property(n => n.PayloadJson).HasColumnType("jsonb");
            entity.HasIndex(n => new { n.UserId, n.ReadAt, n.CreatedAt });
            entity.HasOne<PeerUser>().WithMany().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<PeerConsent>(entity =>
        {
            entity.ToTable("consents");
            entity.HasIndex(c => new { c.UserId, c.Version }).IsUnique();
        });
        modelBuilder.Entity<PeerAuditLog>(entity =>
        {
            entity.ToTable("audit_logs");
            entity.HasIndex(a => new { a.ActorId, a.CreatedAt });
        });
        modelBuilder.Entity<ProfessionalVerification>(entity =>
        {
            entity.ToTable("professional_verifications");
            entity.Property(v => v.LicenceNumber).HasConversion(licenceConverter);
            entity.Property(v => v.Status).HasMaxLength(24);
            entity.HasIndex(v => new { v.UserId, v.Status });
            entity.HasOne(v => v.User).WithMany().HasForeignKey(v => v.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<OtpChallenge>(entity =>
        {
            entity.ToTable("otp_challenges");
            entity.Property(c => c.Contact).HasConversion(challengeContactConverter);
            entity.HasIndex(c => new { c.ContactType, c.ContactBlindIndex, c.CreatedAt });
        });
        modelBuilder.Entity<UserCondition>(entity =>
        {
            entity.ToTable("user_conditions");
            entity.HasKey(x => new { x.UserId, x.ConditionId });
            entity.HasOne<PeerUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Condition>().WithMany().HasForeignKey(x => x.ConditionId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<FacilityIssue>(entity =>
        {
            entity.ToTable("facility_issues");
            entity.Property(i => i.Detail).HasMaxLength(2000);
            entity.HasIndex(i => new { i.Status, i.CreatedAt });
            entity.HasOne(i => i.Facility).WithMany().HasForeignKey(i => i.FacilityId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<HealthEntry>(entity =>
        {
            entity.ToTable("health_entries");
            entity.Property(e => e.EntryType).HasMaxLength(32);
            entity.Property(e => e.Title).HasMaxLength(160);
            entity.Property(e => e.Description).HasMaxLength(5000);
            entity.HasIndex(e => new { e.UserId, e.OccurredAt });
            entity.HasOne<PeerUser>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<Community>(entity =>
        {
            entity.ToTable("communities");
            entity.Property(c => c.Name).HasMaxLength(120);
            entity.Property(c => c.Slug).HasMaxLength(140);
            entity.Property(c => c.Description).HasMaxLength(1000);
            entity.Property(c => c.Guidelines).HasMaxLength(3000);
            entity.Property(c => c.Status).HasMaxLength(24);
            entity.HasIndex(c => c.Slug).IsUnique();
            entity.HasIndex(c => new { c.Status, c.Name });
            entity.HasOne(c => c.Condition).WithMany().HasForeignKey(c => c.ConditionId).OnDelete(DeleteBehavior.SetNull);
        });
        modelBuilder.Entity<CommunityMembership>(entity =>
        {
            entity.ToTable("community_memberships");
            entity.HasKey(m => new { m.CommunityId, m.UserId });
            entity.Property(m => m.Status).HasMaxLength(24);
            entity.HasIndex(m => new { m.UserId, m.Status });
            entity.HasOne<Community>().WithMany().HasForeignKey(m => m.CommunityId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<PeerUser>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ProviderReview>(entity =>
        {
            entity.ToTable("provider_reviews");
            entity.Property(r => r.ReviewText).HasMaxLength(3000);
            entity.Property(r => r.Status).HasMaxLength(24);
            entity.HasIndex(r => new { r.FacilityId, r.Status, r.CreatedAt });
            entity.HasIndex(r => new { r.UserId, r.FacilityId }).IsUnique();
            entity.HasOne(r => r.Facility).WithMany().HasForeignKey(r => r.FacilityId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(r => r.User).WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<UserBlock>(entity =>
        {
            entity.ToTable("user_blocks");
            entity.HasKey(b => new { b.BlockerId, b.BlockedId, b.Kind });
            entity.Property(b => b.Kind).HasMaxLength(12);
            entity.HasOne<PeerUser>().WithMany().HasForeignKey(b => b.BlockerId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<PeerUser>().WithMany().HasForeignKey(b => b.BlockedId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ModerationAppeal>(entity =>
        {
            entity.ToTable("moderation_appeals");
            entity.Property(a => a.Reason).HasMaxLength(2000);
            entity.Property(a => a.Status).HasMaxLength(24);
            entity.Property(a => a.DecisionNote).HasMaxLength(2000);
            entity.HasIndex(a => new { a.Status, a.CreatedAt });
            entity.HasIndex(a => new { a.UserId, a.ActionId }).IsUnique();
            entity.HasIndex(a => a.ReviewedBy);
            entity.HasOne(a => a.User).WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(a => a.Action).WithMany().HasForeignKey(a => a.ActionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<PeerUser>().WithMany().HasForeignKey(a => a.ReviewedBy).OnDelete(DeleteBehavior.SetNull);
        });
    }
}
