using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;
using UrlShortener;
using UrlShortener.Entities;
using UrlShortener.Extensions;
using UrlShortener.Models;
using UrlShortener.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<ApplicationDbContext>(o =>
{
    o.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
});

builder.Services.AddScoped<UrlShorteningService>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("fixed", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString(),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromSeconds(10),
            }
        ));
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    app.ApplyMigrations();
}

app.MapPost("api/shorten", async (
    ShortenUrlRequest request,
    ApplicationDbContext dbContext,
    HttpContext httpContext,
    UrlShorteningService urlShorteningService) =>
{
    if (!Uri.TryCreate(request.Url, UriKind.Absolute, out _))
        return Results.BadRequest("The url is invalid.");

    var code = await urlShorteningService.GenerateUniqueCode();

    var shortenedUrl = new ShortenedUrl
    {
        Id = Guid.NewGuid(),
        LongUrl = request.Url,
        Code = code,
        ShortUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}/api/{code}",
        CreatedOnUtc = DateTime.UtcNow,
    };

    await dbContext.ShortenedUrls.AddAsync(shortenedUrl);
    await dbContext.SaveChangesAsync();

    return Results.Ok(shortenedUrl.ShortUrl);
}).RequireRateLimiting("fixed");

app.MapGet("api/{code}", async (string code, ApplicationDbContext dbContext) =>
{
    var shortenedUrl = await dbContext.ShortenedUrls
        .FirstOrDefaultAsync(s => s.Code == code);

    if (shortenedUrl is null)
        return Results.NotFound();

    return Results.Redirect(shortenedUrl.LongUrl);
}).RequireRateLimiting("fixed");

app.UseHttpsRedirection();

app.UseRateLimiter();

app.Run();