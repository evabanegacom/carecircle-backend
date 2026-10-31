using System.Net;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using PeerCare.Data;

namespace PeerCare.Api;

public static class SeoEndpoints
{
    public static void MapPeerCareSeo(this WebApplication app)
    {
        app.MapGet("/story/{id:guid}", async (Guid id, PeerCareDbContext db, IConfiguration config, CancellationToken ct) =>
        {
            var post = await db.Posts.AsNoTracking().Include(p => p.Condition).Include(p => p.User)
                .Include(p => p.Treatments).ThenInclude(t => t.Treatment)
                .Include(p => p.Facilities).ThenInclude(f => f.Facility)
                .SingleOrDefaultAsync(p => p.Id == id && p.Status == "published", ct);
            if (post is null) return Results.NotFound();
            var baseUrl = (config["PublicBaseUrl"] ?? "http://localhost:5173").TrimEnd('/');
            var canonical = WebUtility.HtmlEncode($"{baseUrl}/story/{post.Id}");
            var title = WebUtility.HtmlEncode(post.Title);
            var condition = WebUtility.HtmlEncode(post.Condition?.Name ?? "Health experience");
            var descriptionText = post.Story.Length > 240 ? post.Story[..240] + "…" : post.Story;
            var description = WebUtility.HtmlEncode(descriptionText);
            var author = WebUtility.HtmlEncode(post.IsAnonymous ? "Anonymous" : post.User?.DisplayName ?? "Community member");
            var body = WebUtility.HtmlEncode(post.Story);
            var treatments = string.Join("", post.Treatments.Select(t => $"<li><strong>{WebUtility.HtmlEncode(t.Treatment?.Name ?? "Treatment")}</strong> — {WebUtility.HtmlEncode(OutcomeLabel(t.Outcome))}</li>"));
            var treatmentsHtml = treatments.Length == 0 ? "" : $"<h2>Treatments tried</h2><ul>{treatments}</ul>";
            var facilities = string.Join("", post.Facilities.Where(f => f.Facility?.Status == "verified")
                .Select(f => $"<p class='muted'>Care location: {WebUtility.HtmlEncode(f.Facility!.Name)}, {WebUtility.HtmlEncode(f.Facility.City)}</p>"));
            var resolutionHtml = post.Kind == "resolution"
                ? $"<h2>What did not work</h2><p>{WebUtility.HtmlEncode(post.WhatFailed ?? "")}</p><h2>What finally helped</h2><p>{WebUtility.HtmlEncode(post.WhatWorked ?? "")}</p><p class='muted'>Time to improve: {WebUtility.HtmlEncode(post.TimeToResolve ?? "Not shared")}</p>"
                : "";
            var symptomsHtml = string.IsNullOrWhiteSpace(post.Symptoms) ? "" : $"<h2>Symptoms</h2><p>{WebUtility.HtmlEncode(post.Symptoms)}</p>";
            var emergency = post.EmergencyFlag ? "<div class='urgent'>If this may be an emergency, please go to the nearest hospital now.</div>" : "";
            var html = $$"""
                <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
                <title>{{title}} · PeerCare</title><meta name="description" content="{{description}}"><link rel="canonical" href="{{canonical}}">
                <meta property="og:type" content="article"><meta property="og:title" content="{{title}}"><meta property="og:description" content="{{description}}"><meta property="og:url" content="{{canonical}}">
                <meta name="theme-color" content="#faf8f5"><style>
                *{box-sizing:border-box}body{margin:0;background:#faf8f5;color:#1f2937;font:16px/1.65 system-ui,sans-serif}header{padding:17px max(22px,calc((100% - 960px)/2));border-bottom:1px solid #e5e7eb;background:#fff}a{color:#0f766e}main{max-width:720px;margin:40px auto;padding:0 22px}.tag{display:inline-block;padding:5px 10px;border-radius:99px;background:#ccfbf1;color:#115e59;font-size:13px}h1{font:600 38px/1.1 Georgia,serif;color:#1f3f39;margin:20px 0 12px}.meta,.muted{color:#6b7280;font-size:14px}.story{margin:26px 0;padding:25px;background:white;border:1px solid #e5e7eb;border-radius:12px;white-space:pre-wrap}.urgent{background:#b91c1c;color:#fff;padding:14px;border-radius:8px;margin:20px 0}.disclaimer{margin-top:28px;padding:16px;border-radius:10px;background:#f1f5f1;color:#55665c;font-size:14px}.back{display:inline-block;margin:0 0 8px}.action{display:inline-block;margin-top:22px;padding:12px 18px;background:#0f766e;color:white;border-radius:8px;text-decoration:none}
                </style></head><body><header><a href="{{baseUrl}}" style="font-weight:700;text-decoration:none">♡ PeerCare</a></header><main>
                {{emergency}}<span class="tag">{{condition}}</span><h1>{{title}}</h1><p class="meta">Shared by {{author}} · {{WebUtility.HtmlEncode(post.AgeGroup)}} · {{WebUtility.HtmlEncode(post.City)}} · {{WebUtility.HtmlEncode(post.State)}}</p>
                <article class="story">{{body}}</article>
                {{resolutionHtml}}{{symptomsHtml}}{{treatmentsHtml}}{{facilities}}
                <div class="disclaimer">This is one family's experience, not medical advice. Talk with a qualified health professional about your care.</div>
                <a class="action" href="{{baseUrl}}/?open={{post.Id}}">Explore more PeerCare stories</a></main></body></html>
                """;
            return Results.Content(html, "text/html; charset=utf-8");
        });

        app.MapGet("/sitemap.xml", async (PeerCareDbContext db, IConfiguration config, CancellationToken ct) =>
        {
            var baseUrl = (config["PublicBaseUrl"] ?? "http://localhost:5173").TrimEnd('/');
            var items = await db.Posts.AsNoTracking().Where(p => p.Status == "published").OrderByDescending(p => p.PublishedAt)
                .Select(p => new { p.Id, p.UpdatedAt }).Take(50000).ToListAsync(ct);
            var urls = string.Join("", items.Select(p => $"<url><loc>{WebUtility.HtmlEncode(baseUrl + "/story/" + p.Id)}</loc><lastmod>{p.UpdatedAt:yyyy-MM-dd}</lastmod></url>"));
            var xml = $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\"><url><loc>{WebUtility.HtmlEncode(baseUrl)}</loc></url>{urls}</urlset>";
            return Results.Content(xml, "application/xml; charset=utf-8");
        });
        app.MapGet("/robots.txt", (IConfiguration config) => Results.Text($"User-agent: *\nAllow: /\nDisallow: /api/\nSitemap: {(config["PublicBaseUrl"] ?? "http://localhost:5173").TrimEnd('/')}/sitemap.xml", "text/plain"));
    }

    private static string OutcomeLabel(string outcome) => outcome switch
    {
        "helped" => "Helped",
        "no_change" => "No change",
        "worse" => "Made things worse",
        _ => "Unsure"
    };
}
