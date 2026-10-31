START TRANSACTION;
ALTER TABLE posts ADD "DiagnosisStatus" character varying NOT NULL DEFAULT 'not_stated';

ALTER TABLE facilities ADD "Source" character varying;

ALTER TABLE facilities ADD "LastVerifiedAt" timestamp with time zone;

ALTER TABLE users ADD "Bio" character varying;

ALTER TABLE users ADD "ProfileVisibility" character varying NOT NULL DEFAULT 'public';

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

CREATE TABLE community_memberships (
    "CommunityId" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "Status" character varying(24) NOT NULL,
    "JoinedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_community_memberships" PRIMARY KEY ("CommunityId", "UserId"),
    CONSTRAINT "FK_community_memberships_communities_CommunityId" FOREIGN KEY ("CommunityId") REFERENCES communities ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_community_memberships_users_UserId" FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE
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
    CONSTRAINT "FK_user_blocks_users_BlockerId" FOREIGN KEY ("BlockerId") REFERENCES users ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_user_blocks_users_BlockedId" FOREIGN KEY ("BlockedId") REFERENCES users ("Id") ON DELETE CASCADE
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
    CONSTRAINT "FK_moderation_appeals_users_UserId" FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_moderation_appeals_moderation_actions_ActionId" FOREIGN KEY ("ActionId") REFERENCES moderation_actions ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_moderation_appeals_users_ReviewedBy" FOREIGN KEY ("ReviewedBy") REFERENCES users ("Id") ON DELETE SET NULL
);

CREATE UNIQUE INDEX "IX_communities_Slug" ON communities ("Slug");

CREATE INDEX "IX_communities_Status_Name" ON communities ("Status", "Name");

CREATE INDEX "IX_communities_ConditionId" ON communities ("ConditionId");

CREATE INDEX "IX_community_memberships_UserId_Status" ON community_memberships ("UserId", "Status");

CREATE INDEX "IX_provider_reviews_FacilityId_Status_CreatedAt" ON provider_reviews ("FacilityId", "Status", "CreatedAt");

CREATE UNIQUE INDEX "IX_provider_reviews_UserId_FacilityId" ON provider_reviews ("UserId", "FacilityId");

CREATE INDEX "IX_user_blocks_BlockedId" ON user_blocks ("BlockedId");

CREATE INDEX "IX_moderation_appeals_Status_CreatedAt" ON moderation_appeals ("Status", "CreatedAt");

CREATE UNIQUE INDEX "IX_moderation_appeals_UserId_ActionId" ON moderation_appeals ("UserId", "ActionId");

CREATE INDEX "IX_moderation_appeals_ActionId" ON moderation_appeals ("ActionId");

CREATE INDEX "IX_moderation_appeals_ReviewedBy" ON moderation_appeals ("ReviewedBy");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261029090000_AddProductCommunitiesAndSafety', '10.0.13');

COMMIT;

