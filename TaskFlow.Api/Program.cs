using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using TaskFlow.Api.Agents;
using TaskFlow.Api.Data;
using TaskFlow.Api.Ingestion;
using TaskFlow.Api.Services;
using TaskFlow.Api.Hubs;
using TaskFlow.Api.Repositories;

var builder = WebApplication.CreateBuilder(args);

// ── Controllers + JSON enum serialization ────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter()));

// ── Swagger — with JWT auth support ─────────────────────────────────────────
// This adds the "Authorize" padlock button to the Swagger UI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "TaskFlow API",
        Version = "v1",
        Description = "A workflow task manager API with autonomous AI agents."
    });

    // Tell Swagger how to pass a JWT token
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your JWT token. Example: eyJhbGci..."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// ── Database ─────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// ── JWT Authentication ────────────────────────────────────────────────────────
var jwtKey = builder.Configuration["Jwt:Key"]!;
var jwtIssuer = builder.Configuration["Jwt:Issuer"]!;
var jwtAudience = builder.Configuration["Jwt:Audience"]!;

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,          // Rejects expired tokens
            ValidateIssuerSigningKey = true,  // Rejects tampered tokens
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;

                if (!string.IsNullOrEmpty(accessToken) &&
                    path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// ── Services ──────────────────────────────────────────────────────────────────
builder.Services.AddScoped<JwtService>();
builder.Services.AddScoped<ITaskRepository, TaskRepository>();
builder.Services.AddScoped<IJobApplicationRepository, JobApplicationRepository>();
builder.Services.AddScoped<IResumeContextRepository, ResumeContextRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IAgentLogRepository, AgentLogRepository>();
builder.Services.AddScoped<ITaskService, TaskService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IClaudeClient, ClaudeClient>();
// ── Ingestion (free-first: rules, escalate to Claude) ──────────────────────────
builder.Services.AddScoped<SpecDocumentParser>();
builder.Services.AddScoped<ClaudeIngestionParser>();
builder.Services.AddScoped<IIngestionParser>(sp => new TieredIngestionParser(
    free: sp.GetRequiredService<SpecDocumentParser>(),
    paid: sp.GetRequiredService<ClaudeIngestionParser>()));
builder.Services.AddScoped<JobPostingParser>();
builder.Services.AddScoped<ClaudeJobPostingParser>();
builder.Services.AddScoped<IJobPostingIngestionParser>(sp => new JobPostingIngestionParser(
    free: sp.GetRequiredService<JobPostingParser>(),
    paid: sp.GetRequiredService<ClaudeJobPostingParser>()));
builder.Services.AddScoped<IDraftCommitService, DraftCommitService>();
builder.Services.AddScoped<IResumeContextService, ResumeContextService>();
builder.Services.AddScoped<IJobApplicationAssemblyService, JobApplicationAssemblyService>();
// ── SignalR ──────────────────────────────────────────────────────────────────
builder.Services.AddSignalR();
builder.Services.AddSingleton<IAgentNotifier, SignalRAgentNotifier>();

// ── Guardrails (Sprint 6) ──────────────────────────────────────────────────────
// Executor kill switch is a singleton so its runtime state is shared app-wide; the spend guard is
// scoped because it reads the request-scoped log repository.
builder.Services.AddSingleton<IExecutorSwitch, ExecutorSwitch>();
builder.Services.AddScoped<ISpendGuard, DailyExecutorSpendGuard>();

// ── Agent Infrastructure ──────────────────────────────────────────────────────
// Register each agent as a scoped service implementing ITaskFlowAgent
// The AgentRunner discovers these automatically via GetServices<ITaskFlowAgent>()
builder.Services.AddScoped<ITaskFlowAgent, TaskPrioritizerAgent>();
builder.Services.AddScoped<ITaskFlowAgent, StaleTaskAgent>();
builder.Services.AddScoped<ITaskFlowAgent, GenericExecutorAgent>();
builder.Services.AddScoped<ITaskFlowAgent, ResumeTailoringAgent>();
builder.Services.AddScoped<ITaskFlowAgent, CoverLetterAgent>();

// Register the AgentRunner as a hosted background service
// This starts automatically when the app starts
builder.Services.AddHostedService<AgentRunner>();

// Plain sweep (not an ITaskFlowAgent) that recovers tasks orphaned InProgress by a crashed/killed
// agent process — a hard crash mid-cycle leaves no in-process code path to notice, unlike a graceful
// failure which GenericExecutorAgent's own try/catch already rolls back.
builder.Services.AddHostedService<StaleClaimReaperService>();

// Plain sweep (not an ITaskFlowAgent) that recovers JobApplications left stuck at Building when
// both sibling tasks are actually Review — TailoringAgentBase's per-application join right after a
// save is a best-effort trigger, not a guarantee (PR #43 review, round 2).
builder.Services.AddHostedService<JobApplicationPromotionReconcilerService>();

// ── CORS ─────────────────────────────────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddPolicy("DevPolicy", policy =>
    {
        policy
            .WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();   // required for SignalR websockets
    });
});

// ─────────────────────────────────────────────────────────────────────────────
var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "TaskFlow API v1");
        options.RoutePrefix = "swagger";
    });
}

app.UseHttpsRedirection();
app.UseCors("DevPolicy");

// ORDER MATTERS — Authentication must come before Authorization
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
    app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.MapHub<AgentHub>("/hubs/agents");
app.MapControllers();
app.Run();

// Exposes the top-level-statement Program class to the test project so
// WebApplicationFactory<Program> can boot the app for HTTP integration tests.
public partial class Program { }