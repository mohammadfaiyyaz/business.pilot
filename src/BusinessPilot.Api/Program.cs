using System.Text;
using System.Threading.RateLimiting;
using BusinessPilot.Api.Data;
using BusinessPilot.Api.Models;
using BusinessPilot.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Configure ConnectionStrings:Default before starting the API.");
var signingKey = builder.Configuration["Jwt:SigningKey"]
    ?? throw new InvalidOperationException("Configure Jwt:SigningKey before starting the API.");
if (signingKey.Length < 32) throw new InvalidOperationException("Jwt:SigningKey must contain at least 32 characters.");
if (!builder.Environment.IsDevelopment() && signingKey.StartsWith("dev-only-", StringComparison.Ordinal))
    throw new InvalidOperationException("Set a unique production Jwt:SigningKey through secure configuration.");

builder.Services.AddDbContext<BusinessPilotDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<CustomerService>();
builder.Services.AddScoped<InvoiceService>();
builder.Services.AddScoped<InvoicePdfService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<BillingService>();
builder.Services.AddScoped<ExpenseService>();
builder.Services.AddScoped<ProductService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddScoped<SupportService>();
builder.Services.AddHttpClient<SupportAiProvider>(client => client.Timeout = TimeSpan.FromSeconds(18));
builder.Services.AddScoped<ISupportAiProvider>(sp => sp.GetRequiredService<SupportAiProvider>());
builder.Services.AddHttpClient<RazorpayBillingService>(client => {
    client.BaseAddress = new Uri("https://api.razorpay.com/v1/");
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddIdentityCore<ApplicationUser>(options => {
    options.User.RequireUniqueEmail = true;
    options.Password.RequiredLength = 12;
    options.Password.RequireDigit = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.SignIn.RequireConfirmedEmail = false;
}).AddRoles<IdentityRole>().AddEntityFrameworkStores<BusinessPilotDbContext>().AddSignInManager();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options => {
    options.TokenValidationParameters = new TokenValidationParameters {
        ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"], ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
        ClockSkew = TimeSpan.FromSeconds(30), RoleClaimType = System.Security.Claims.ClaimTypes.Role
    };
});
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options => {
    options.AddFixedWindowLimiter("auth", limiter => {
        limiter.PermitLimit = 10;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
        limiter.AutoReplenishment = true;
    });
    options.AddPolicy("support-widget", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions {
            PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
        }));
});
builder.Services.AddCors(options => options.AddPolicy("AngularDev", policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:4200"])
    .AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddCors(options => options.AddPolicy("SupportWidget", policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseStaticFiles();
app.UseCors("AngularDev");
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<BillingAccessMiddleware>();
app.MapGet("/api/health", () => Results.Ok(new { status = "ok", service = "BusinessPilot API" }));
app.MapControllers();
app.Run();
