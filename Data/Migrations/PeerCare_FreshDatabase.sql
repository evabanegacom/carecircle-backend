CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;
CREATE TABLE audit_logs (
    "Id" uuid NOT NULL,
    "ActorId" uuid,
    "Action" text NOT NULL,
    "SubjectType" text NOT NULL,
    "SubjectId" uuid,
    "IpHash" text,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_audit_logs" PRIMARY KEY ("Id")
);

CREATE TABLE conditions (
    "Id" uuid NOT NULL,
    "Name" character varying(120) NOT NULL,
    "Slug" character varying(140) NOT NULL,
    "Category" text NOT NULL,
    "ParentId" uuid,
    "Active" boolean NOT NULL,
    CONSTRAINT "PK_conditions" PRIMARY KEY ("Id")
);

CREATE TABLE consents (
    "Id" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "Version" text NOT NULL,
    "AcceptedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_consents" PRIMARY KEY ("Id")
);

CREATE TABLE facilities (
    "Id" uuid NOT NULL,
    "Name" character varying(180) NOT NULL,
    "Type" text NOT NULL,
    "Address" text,
    "State" text NOT NULL,
    "City" text NOT NULL,
    "Latitude" double precision,
    "Longitude" double precision,
    "Status" character varying(24) NOT NULL,
    "CreatedBy" uuid,
    CONSTRAINT "PK_facilities" PRIMARY KEY ("Id")
);

CREATE TABLE moderation_actions (
    "Id" uuid NOT NULL,
    "ModeratorId" uuid NOT NULL,
    "TargetType" text NOT NULL,
    "TargetId" uuid NOT NULL,
    "Action" text NOT NULL,
    "Reason" text NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_moderation_actions" PRIMARY KEY ("Id")
);

CREATE TABLE otp_challenges (
    "Id" uuid NOT NULL,
    "ContactType" text NOT NULL,
    "ContactBlindIndex" text NOT NULL,
    "Contact" text NOT NULL,
    "CodeHash" text NOT NULL,
    "FailedAttempts" integer NOT NULL,
    "ExpiresAt" timestamp with time zone NOT NULL,
    "LockedUntil" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_otp_challenges" PRIMARY KEY ("Id")
);

CREATE TABLE reactions (
    "Id" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "TargetType" text NOT NULL,
    "TargetId" uuid NOT NULL,
    "Kind" text NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_reactions" PRIMARY KEY ("Id")
);

CREATE TABLE reports (
    "Id" uuid NOT NULL,
    "ReporterId" uuid NOT NULL,
    "TargetType" text NOT NULL,
    "TargetId" uuid NOT NULL,
    "Reason" character varying(32) NOT NULL,
    "Detail" text,
    "Status" character varying(24) NOT NULL,
    "HandledBy" uuid,
    "HandledAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_reports" PRIMARY KEY ("Id")
);

CREATE TABLE roles (
    "Id" uuid NOT NULL,
    "Name" character varying(256),
    "NormalizedName" character varying(256),
    "ConcurrencyStamp" text,
    CONSTRAINT "PK_roles" PRIMARY KEY ("Id")
);

CREATE TABLE treatments (
    "Id" uuid NOT NULL,
    "Name" character varying(160) NOT NULL,
    "Kind" text NOT NULL,
    "Approved" boolean NOT NULL,
    CONSTRAINT "PK_treatments" PRIMARY KEY ("Id")
);

CREATE TABLE users (
    "Id" uuid NOT NULL,
    "DisplayName" character varying(80) NOT NULL,
    "State" text,
    "City" text,
    "Status" text NOT NULL,
    "PhoneBlindIndex" character varying(64),
    "ConsentVersion" text NOT NULL,
    "ConsentedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "DeleteRequestedAt" timestamp with time zone,
    "InAppNotifications" boolean NOT NULL,
    "EmailNotifications" boolean NOT NULL,
    "UserName" character varying(256),
    "NormalizedUserName" character varying(256),
    "Email" character varying(256),
    "NormalizedEmail" character varying(256),
    "EmailConfirmed" boolean NOT NULL,
    "PasswordHash" text,
    "SecurityStamp" text,
    "ConcurrencyStamp" text,
    "PhoneNumber" text,
    "PhoneNumberConfirmed" boolean NOT NULL,
    "TwoFactorEnabled" boolean NOT NULL,
    "LockoutEnd" timestamp with time zone,
    "LockoutEnabled" boolean NOT NULL,
    "AccessFailedCount" integer NOT NULL,
    CONSTRAINT "PK_users" PRIMARY KEY ("Id")
);

CREATE TABLE facility_issues (
    "Id" uuid NOT NULL,
    "FacilityId" uuid NOT NULL,
    "ReporterId" uuid NOT NULL,
    "Detail" character varying(2000) NOT NULL,
    "Status" text NOT NULL,
    "HandledBy" uuid,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_facility_issues" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_facility_issues_facilities_FacilityId" FOREIGN KEY ("FacilityId") REFERENCES facilities ("Id") ON DELETE CASCADE
);

CREATE TABLE practitioners (
    "Id" uuid NOT NULL,
    "FacilityId" uuid NOT NULL,
    "Name" character varying(140) NOT NULL,
    "Specialty" text,
    "Status" character varying(24) NOT NULL,
    "ReportCount" integer NOT NULL,
    CONSTRAINT "PK_practitioners" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_practitioners_facilities_FacilityId" FOREIGN KEY ("FacilityId") REFERENCES facilities ("Id") ON DELETE CASCADE
);

CREATE TABLE role_claims (
    "Id" integer GENERATED BY DEFAULT AS IDENTITY,
    "RoleId" uuid NOT NULL,
    "ClaimType" text,
    "ClaimValue" text,
    CONSTRAINT "PK_role_claims" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_role_claims_roles_RoleId" FOREIGN KEY ("RoleId") REFERENCES roles ("Id") ON DELETE CASCADE
);

CREATE TABLE notifications (
    "Id" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "Kind" text NOT NULL,
    "PayloadJson" jsonb NOT NULL,
    "ReadAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_notifications" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_notifications_users_UserId" FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE
);

CREATE TABLE posts (
    "Id" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "Kind" character varying(24) NOT NULL,
    "ConditionId" uuid NOT NULL,
    "AgeGroup" character varying(40) NOT NULL,
    "About" character varying(24) NOT NULL,
    "Title" character varying(180) NOT NULL,
    "Symptoms" text NOT NULL,
    "DurationText" text NOT NULL,
    "Story" text NOT NULL,
    "Outcome" character varying(24) NOT NULL,
    "HelpNeeded" text,
    "WhatFailed" text,
    "WhatWorked" text,
    "TimeToResolve" text,
    "IsAnonymous" boolean NOT NULL,
    "State" character varying(80) NOT NULL,
    "City" character varying(100) NOT NULL,
    "Status" character varying(24) NOT NULL,
    "EmergencyFlag" boolean NOT NULL,
    "FlagsCount" integer NOT NULL,
    "AutoFlagsJson" jsonb NOT NULL,
    "PublishedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "SearchVector" tsvector GENERATED ALWAYS AS (setweight(to_tsvector('english', coalesce("Title", '')), 'A') || setweight(to_tsvector('english', coalesce("Symptoms", '')), 'B') || setweight(to_tsvector('english', coalesce("Story", '')), 'C') || setweight(to_tsvector('english', coalesce("WhatFailed", '') || ' ' || coalesce("WhatWorked", '') || ' ' || coalesce("HelpNeeded", '')), 'D')) STORED NOT NULL,
    CONSTRAINT "PK_posts" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_posts_conditions_ConditionId" FOREIGN KEY ("ConditionId") REFERENCES conditions ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_posts_users_UserId" FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE
);

CREATE TABLE professional_verifications (
    "Id" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "UserId1" uuid,
    "Profession" text NOT NULL,
    "LicenceNumber" text NOT NULL,
    "IssuingBody" text NOT NULL,
    "Status" character varying(24) NOT NULL,
    "ReviewedBy" uuid,
    "ReviewedAt" timestamp with time zone,
    "RejectReason" text,
    CONSTRAINT "PK_professional_verifications" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_professional_verifications_users_UserId" FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_professional_verifications_users_UserId1" FOREIGN KEY ("UserId1") REFERENCES users ("Id")
);

CREATE TABLE user_claims (
    "Id" integer GENERATED BY DEFAULT AS IDENTITY,
    "UserId" uuid NOT NULL,
    "ClaimType" text,
    "ClaimValue" text,
    CONSTRAINT "PK_user_claims" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_user_claims_users_UserId" FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE
);

CREATE TABLE user_conditions (
    "UserId" uuid NOT NULL,
    "ConditionId" uuid NOT NULL,
    CONSTRAINT "PK_user_conditions" PRIMARY KEY ("UserId", "ConditionId"),
    CONSTRAINT "FK_user_conditions_conditions_ConditionId" FOREIGN KEY ("ConditionId") REFERENCES conditions ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_user_conditions_users_UserId" FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE
);

CREATE TABLE user_logins (
    "LoginProvider" text NOT NULL,
    "ProviderKey" text NOT NULL,
    "ProviderDisplayName" text,
    "UserId" uuid NOT NULL,
    CONSTRAINT "PK_user_logins" PRIMARY KEY ("LoginProvider", "ProviderKey"),
    CONSTRAINT "FK_user_logins_users_UserId" FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE
);

CREATE TABLE user_roles (
    "UserId" uuid NOT NULL,
    "RoleId" uuid NOT NULL,
    CONSTRAINT "PK_user_roles" PRIMARY KEY ("UserId", "RoleId"),
    CONSTRAINT "FK_user_roles_roles_RoleId" FOREIGN KEY ("RoleId") REFERENCES roles ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_user_roles_users_UserId" FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE
);

CREATE TABLE user_tokens (
    "UserId" uuid NOT NULL,
    "LoginProvider" text NOT NULL,
    "Name" text NOT NULL,
    "Value" text,
    CONSTRAINT "PK_user_tokens" PRIMARY KEY ("UserId", "LoginProvider", "Name"),
    CONSTRAINT "FK_user_tokens_users_UserId" FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE
);

CREATE TABLE post_facilities (
    "Id" uuid NOT NULL,
    "PostId" uuid NOT NULL,
    "FacilityId" uuid NOT NULL,
    "PractitionerId" uuid,
    "Result" character varying(24) NOT NULL,
    CONSTRAINT "PK_post_facilities" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_post_facilities_facilities_FacilityId" FOREIGN KEY ("FacilityId") REFERENCES facilities ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_post_facilities_posts_PostId" FOREIGN KEY ("PostId") REFERENCES posts ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_post_facilities_practitioners_PractitionerId" FOREIGN KEY ("PractitionerId") REFERENCES practitioners ("Id") ON DELETE SET NULL
);

CREATE TABLE post_treatments (
    "Id" uuid NOT NULL,
    "PostId" uuid NOT NULL,
    "TreatmentId" uuid NOT NULL,
    "Outcome" character varying(24) NOT NULL,
    "Note" text,
    CONSTRAINT "PK_post_treatments" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_post_treatments_posts_PostId" FOREIGN KEY ("PostId") REFERENCES posts ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_post_treatments_treatments_TreatmentId" FOREIGN KEY ("TreatmentId") REFERENCES treatments ("Id") ON DELETE RESTRICT
);

CREATE TABLE replies (
    "Id" uuid NOT NULL,
    "PostId" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "ParentId" uuid,
    "Body" character varying(4000) NOT NULL,
    "IsProfessionalNote" boolean NOT NULL,
    "Status" text NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_replies" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_replies_posts_PostId" FOREIGN KEY ("PostId") REFERENCES posts ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_replies_users_UserId" FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE
);

CREATE TABLE saves (
    "UserId" uuid NOT NULL,
    "PostId" uuid NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_saves" PRIMARY KEY ("UserId", "PostId"),
    CONSTRAINT "FK_saves_posts_PostId" FOREIGN KEY ("PostId") REFERENCES posts ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_saves_users_UserId" FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE
);

CREATE INDEX "IX_audit_logs_ActorId_CreatedAt" ON audit_logs ("ActorId", "CreatedAt");

CREATE UNIQUE INDEX "IX_conditions_Slug" ON conditions ("Slug");

CREATE UNIQUE INDEX "IX_consents_UserId_Version" ON consents ("UserId", "Version");

CREATE INDEX "IX_facilities_Name_City" ON facilities ("Name", "City");

CREATE INDEX "IX_facility_issues_FacilityId" ON facility_issues ("FacilityId");

CREATE INDEX "IX_facility_issues_Status_CreatedAt" ON facility_issues ("Status", "CreatedAt");

CREATE INDEX "IX_moderation_actions_TargetType_TargetId_CreatedAt" ON moderation_actions ("TargetType", "TargetId", "CreatedAt");

CREATE INDEX "IX_notifications_UserId_ReadAt_CreatedAt" ON notifications ("UserId", "ReadAt", "CreatedAt");

CREATE INDEX "IX_otp_challenges_ContactType_ContactBlindIndex_CreatedAt" ON otp_challenges ("ContactType", "ContactBlindIndex", "CreatedAt");

CREATE INDEX "IX_post_facilities_FacilityId" ON post_facilities ("FacilityId");

CREATE INDEX "IX_post_facilities_PostId" ON post_facilities ("PostId");

CREATE INDEX "IX_post_facilities_PractitionerId" ON post_facilities ("PractitionerId");

CREATE INDEX "IX_post_treatments_PostId" ON post_treatments ("PostId");

CREATE INDEX "IX_post_treatments_TreatmentId" ON post_treatments ("TreatmentId");

CREATE INDEX "IX_posts_ConditionId_Status_PublishedAt" ON posts ("ConditionId", "Status", "PublishedAt");

CREATE INDEX "IX_posts_SearchVector" ON posts USING GIN ("SearchVector");

CREATE INDEX "IX_posts_State_City" ON posts ("State", "City");

CREATE INDEX "IX_posts_UserId_CreatedAt" ON posts ("UserId", "CreatedAt");

CREATE INDEX "IX_practitioners_FacilityId" ON practitioners ("FacilityId");

CREATE INDEX "IX_professional_verifications_UserId_Status" ON professional_verifications ("UserId", "Status");

CREATE INDEX "IX_professional_verifications_UserId1" ON professional_verifications ("UserId1");

CREATE INDEX "IX_reactions_TargetType_TargetId_Kind" ON reactions ("TargetType", "TargetId", "Kind");

CREATE UNIQUE INDEX "IX_reactions_UserId_TargetType_TargetId_Kind" ON reactions ("UserId", "TargetType", "TargetId", "Kind");

CREATE INDEX "IX_replies_PostId_Status_CreatedAt" ON replies ("PostId", "Status", "CreatedAt");

CREATE INDEX "IX_replies_UserId" ON replies ("UserId");

CREATE INDEX "IX_reports_Status_CreatedAt" ON reports ("Status", "CreatedAt");

CREATE INDEX "IX_reports_TargetType_TargetId" ON reports ("TargetType", "TargetId");

CREATE INDEX "IX_role_claims_RoleId" ON role_claims ("RoleId");

CREATE UNIQUE INDEX "RoleNameIndex" ON roles ("NormalizedName");

CREATE INDEX "IX_saves_PostId" ON saves ("PostId");

CREATE UNIQUE INDEX "IX_treatments_Name" ON treatments ("Name");

CREATE INDEX "IX_user_claims_UserId" ON user_claims ("UserId");

CREATE INDEX "IX_user_conditions_ConditionId" ON user_conditions ("ConditionId");

CREATE INDEX "IX_user_logins_UserId" ON user_logins ("UserId");

CREATE INDEX "IX_user_roles_RoleId" ON user_roles ("RoleId");

CREATE UNIQUE INDEX "EmailIndex" ON users ("NormalizedEmail") WHERE "NormalizedEmail" IS NOT NULL;

CREATE UNIQUE INDEX "IX_users_PhoneBlindIndex" ON users ("PhoneBlindIndex") WHERE "PhoneBlindIndex" IS NOT NULL;

CREATE INDEX "IX_users_Status" ON users ("Status");

CREATE UNIQUE INDEX "UserNameIndex" ON users ("NormalizedUserName");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261022073609_InitialCreate', '10.0.13');

COMMIT;

START TRANSACTION;
CREATE TABLE health_entries (
    "Id" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "EntryType" character varying(32) NOT NULL,
    "Title" character varying(160),
    "Description" character varying(5000) NOT NULL,
    "OccurredAt" timestamp with time zone,
    "Severity" integer,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "DeletedAt" timestamp with time zone,
    CONSTRAINT "PK_health_entries" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_health_entries_users_UserId" FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE
);

CREATE INDEX "IX_health_entries_UserId_OccurredAt" ON health_entries ("UserId", "OccurredAt");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261028185712_AddHealthEntries', '10.0.13');

COMMIT;

START TRANSACTION;
ALTER TABLE professional_verifications DROP CONSTRAINT "FK_professional_verifications_users_UserId1";

DROP INDEX "IX_professional_verifications_UserId1";

ALTER TABLE users ADD "Bio" character varying(500);

ALTER TABLE users ADD "ProfileVisibility" character varying(24) NOT NULL DEFAULT '';

ALTER TABLE posts ADD "DiagnosisStatus" character varying(24) NOT NULL DEFAULT '';

ALTER TABLE facilities ADD "LastVerifiedAt" timestamp with time zone;

ALTER TABLE facilities ADD "Source" character varying(300);

CREATE TABLE communities (
    "Id" uuid NOT NULL,
    "Name" character varying(120) NOT NULL,
    "Slug" character varying(140) NOT NULL,
    "Description" character varying(1000) NOT NULL,
    "Guidelines" character varying(3000) NOT NULL,
    "Status" character varying(24) NOT NULL,
    "ConditionId" uuid,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_communities" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_communities_conditions_ConditionId" FOREIGN KEY ("ConditionId") REFERENCES conditions ("Id") ON DELETE SET NULL
);

CREATE TABLE moderation_appeals (
    "Id" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "ActionId" uuid NOT NULL,
    "Reason" character varying(2000) NOT NULL,
    "Status" character varying(24) NOT NULL,
    "DecisionNote" character varying(2000),
    "ReviewedBy" uuid,
    "CreatedAt" timestamp with time zone NOT NULL,
    "ReviewedAt" timestamp with time zone,
    CONSTRAINT "PK_moderation_appeals" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_moderation_appeals_moderation_actions_ActionId" FOREIGN KEY ("ActionId") REFERENCES moderation_actions ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_moderation_appeals_users_UserId" FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE
);

CREATE TABLE provider_reviews (
    "Id" uuid NOT NULL,
    "FacilityId" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "Rating" integer,
    "ReviewText" character varying(3000) NOT NULL,
    "VisitDate" date,
    "Status" character varying(24) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_provider_reviews" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_provider_reviews_facilities_FacilityId" FOREIGN KEY ("FacilityId") REFERENCES facilities ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_provider_reviews_users_UserId" FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE
);

CREATE TABLE user_blocks (
    "BlockerId" uuid NOT NULL,
    "BlockedId" uuid NOT NULL,
    "Kind" character varying(12) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_user_blocks" PRIMARY KEY ("BlockerId", "BlockedId", "Kind"),
    CONSTRAINT "FK_user_blocks_users_BlockedId" FOREIGN KEY ("BlockedId") REFERENCES users ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_user_blocks_users_BlockerId" FOREIGN KEY ("BlockerId") REFERENCES users ("Id") ON DELETE CASCADE
);

CREATE TABLE community_memberships (
    "CommunityId" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "Status" character varying(24) NOT NULL,
    "JoinedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_community_memberships" PRIMARY KEY ("CommunityId", "UserId"),
    CONSTRAINT "FK_community_memberships_communities_CommunityId" FOREIGN KEY ("CommunityId") REFERENCES communities ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_community_memberships_users_UserId" FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE
);

CREATE INDEX "IX_communities_ConditionId" ON communities ("ConditionId");

CREATE UNIQUE INDEX "IX_communities_Slug" ON communities ("Slug");

CREATE INDEX "IX_communities_Status_Name" ON communities ("Status", "Name");

CREATE INDEX "IX_community_memberships_UserId_Status" ON community_memberships ("UserId", "Status");

CREATE INDEX "IX_moderation_appeals_ActionId" ON moderation_appeals ("ActionId");

CREATE INDEX "IX_moderation_appeals_Status_CreatedAt" ON moderation_appeals ("Status", "CreatedAt");

CREATE UNIQUE INDEX "IX_moderation_appeals_UserId_ActionId" ON moderation_appeals ("UserId", "ActionId");

CREATE INDEX "IX_provider_reviews_FacilityId_Status_CreatedAt" ON provider_reviews ("FacilityId", "Status", "CreatedAt");

CREATE UNIQUE INDEX "IX_provider_reviews_UserId_FacilityId" ON provider_reviews ("UserId", "FacilityId");

CREATE INDEX "IX_user_blocks_BlockedId" ON user_blocks ("BlockedId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261028212717_SyncPeerCareModel', '10.0.13');

COMMIT;

START TRANSACTION;
ALTER TABLE users ALTER COLUMN "ProfileVisibility" SET DEFAULT 'public';

ALTER TABLE posts ALTER COLUMN "DiagnosisStatus" SET DEFAULT 'not_stated';

CREATE INDEX "IX_moderation_appeals_ReviewedBy" ON moderation_appeals ("ReviewedBy");

ALTER TABLE moderation_appeals ADD CONSTRAINT "FK_moderation_appeals_users_ReviewedBy" FOREIGN KEY ("ReviewedBy") REFERENCES users ("Id") ON DELETE SET NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261029090000_AddProductCommunitiesAndSafety', '10.0.13');

COMMIT;

