using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PeerCare.Domain;

namespace PeerCare.Data;

public static class PeerCareSeeder
{
    private static readonly (string Name, string Slug)[] ConditionSeeds =
    [
        ("Asthma", "asthma"), ("Eczema", "eczema"), ("Malaria", "malaria"),
        ("Sickle cell", "sickle-cell"), ("Speech delay", "speech-delay"),
        ("Epilepsy", "epilepsy"), ("Other", "other")
    ];

    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration, IHostEnvironment environment)
    {
        var db = services.GetRequiredService<PeerCareDbContext>();
        foreach (var name in new[] { "Member", "Professional", "Moderator", "Admin" })
        {
            var roleManager = services.GetRequiredService<RoleManager<PeerRole>>();
            if (!await roleManager.RoleExistsAsync(name))
                await roleManager.CreateAsync(new PeerRole { Name = name, NormalizedName = name.ToUpperInvariant() });
        }
        var conditions = await db.Conditions.ToDictionaryAsync(x => x.Slug);
        foreach (var (name, slug) in ConditionSeeds)
        {
            if (!conditions.ContainsKey(slug))
            {
                var item = new Condition { Name = name, Slug = slug, Category = "child-health" };
                db.Conditions.Add(item);
                conditions[slug] = item;
            }
        }
        foreach (var name in new[] { "Care plan", "Moisturiser", "Appointment notes", "Speech and language assessment", "Inhaler technique", "Other" })
            if (!await db.Treatments.AnyAsync(x => x.Name == name)) db.Treatments.Add(new Treatment { Name = name, Kind = "other", Approved = true });
        await db.SaveChangesAsync();

        foreach (var condition in await db.Conditions.Where(c => c.Active).ToListAsync())
        {
            var slug = $"condition-{condition.Slug}";
            if (!await db.Communities.AnyAsync(c => c.Slug == slug))
                db.Communities.Add(new Community
                {
                    Name = $"{condition.Name} community",
                    Slug = slug,
                    ConditionId = condition.Id,
                    Description = $"A place to explore shared experiences and ask questions related to {condition.Name}. Community labels do not confirm a diagnosis.",
                    Guidelines = "Share personal experiences with care. Do not ask members to diagnose or prescribe. Avoid identifying details, medication doses, and urgent-care requests. Talk with a qualified health professional about personal medical decisions."
                });
        }
        foreach (var (name, slug, description) in new[]
        {
            ("Parents & caregivers", "parents-caregivers", "A space for people caring for children and family members through healthcare journeys."),
            ("Finding care", "finding-care", "Share experiences navigating referrals, facilities, and conversations with care professionals."),
            ("Recovery & wellbeing", "recovery-wellbeing", "A supportive topic space for recovery experiences and everyday wellbeing.")
        })
            if (!await db.Communities.AnyAsync(c => c.Slug == slug))
                db.Communities.Add(new Community
                {
                    Name = name, Slug = slug, Description = description,
                    Guidelines = "Share personal experiences with care. Do not ask members to diagnose or prescribe. Avoid identifying details, medication doses, and urgent-care requests. Talk with a qualified health professional about personal medical decisions."
                });
        await db.SaveChangesAsync();

        var bootstrapAdmin = configuration["Bootstrap:AdminEmail"];
        if (environment.IsDevelopment() && !string.IsNullOrWhiteSpace(bootstrapAdmin))
        {
            var normalizer = services.GetRequiredService<ILookupNormalizer>();
            var admin = await db.Users.SingleOrDefaultAsync(u => u.NormalizedEmail == normalizer.NormalizeEmail(bootstrapAdmin));
            if (admin is not null)
            {
                var manager = services.GetRequiredService<UserManager<PeerUser>>();
                if (!await manager.IsInRoleAsync(admin, "Admin")) await manager.AddToRoleAsync(admin, "Admin");
            }
        }

        if (!environment.IsDevelopment() || !configuration.GetValue("SeedData:EnableDemoContent", false) || await db.Posts.AnyAsync()) return;
        var seedUser = new PeerUser { UserName = "community-stories", DisplayName = "Adaeze O.", EmailConfirmed = true, ConsentVersion = "demo", ConsentedAt = DateTimeOffset.UtcNow };
        var managerForSeed = services.GetRequiredService<UserManager<PeerUser>>();
        var created = await managerForSeed.CreateAsync(seedUser);
        if (!created.Succeeded) return;
        await managerForSeed.AddToRoleAsync(seedUser, "Member");
        var questionUser = new PeerUser { UserName = "community-question", DisplayName = "Tomi's mum", EmailConfirmed = true, ConsentVersion = "demo", ConsentedAt = DateTimeOffset.UtcNow };
        if (!(await managerForSeed.CreateAsync(questionUser)).Succeeded) questionUser = seedUser;
        else await managerForSeed.AddToRoleAsync(questionUser, "Member");
        var bySlug = await db.Conditions.ToDictionaryAsync(x => x.Slug);
        var hospital = new Facility { Name = "Lagos University Teaching Hospital", City = "Idi-Araba", State = "Lagos", Status = "verified" };
        var secondHospital = new Facility { Name = "Reddington Hospital", City = "Lekki", State = "Lagos", Status = "verified" };
        db.Facilities.AddRange(hospital, secondHospital);
        var eczema = MakePost(seedUser, bySlug["eczema"], "resolution", "Our little one is sleeping through the night again", "Toddler", "child",
            "Itchy patches kept waking our daughter. A patient at LUTH helped us prepare questions for her doctor. With a care plan from her clinician and a gentler routine, her skin gradually settled.", "helped", "Care plan", "Adaeze O.");
        var asthma = MakePost(seedUser, bySlug["asthma"], "resolution", "Finding asthma support at school", "School age", "child",
            "We spoke with our son's doctor and school together, and made a plan everyone understood. He has been back with his friends and feeling more confident.", "improving", "Inhaler technique", "Anonymous");
        var question = MakePost(questionUser, bySlug["speech-delay"], "question", "Has anyone found speech support in Ikeja?", "Toddler", "child",
            "We are looking for experiences with getting a speech and language assessment for our toddler. A referral or a place to start would mean a lot.", "unknown", "", "Tomi's mum");
        question.HelpNeeded = "A referral";
        db.Posts.AddRange(eczema, asthma, question);
        await db.SaveChangesAsync();
        db.PostFacilities.AddRange(
            new PostFacility { PostId = eczema.Id, FacilityId = hospital.Id, Result = "helped_us" },
            new PostFacility { PostId = asthma.Id, FacilityId = secondHospital.Id, Result = "helped_us" });
        var carePlan = await db.Treatments.SingleAsync(x => x.Name == "Care plan");
        var inhalerTechnique = await db.Treatments.SingleAsync(x => x.Name == "Inhaler technique");
        db.PostTreatments.AddRange(
            new PostTreatment { PostId = eczema.Id, TreatmentId = carePlan.Id, Outcome = "helped" },
            new PostTreatment { PostId = asthma.Id, TreatmentId = inhalerTechnique.Id, Outcome = "helped" });
        await db.SaveChangesAsync();
    }

    private static PeerPost MakePost(PeerUser user, Condition condition, string kind, string title, string age, string about, string story, string outcome, string treatment, string author)
    {
        var post = new PeerPost
        {
            UserId = user.Id, Kind = kind, ConditionId = condition.Id, AgeGroup = age, About = about, Title = title,
            Story = story, Outcome = outcome, City = "Lagos", State = "Lagos", IsAnonymous = author == "Anonymous",
            Status = "published", PublishedAt = DateTimeOffset.UtcNow.AddDays(-2), CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
            AutoFlagsJson = "[]"
        };
        return post;
    }
}
