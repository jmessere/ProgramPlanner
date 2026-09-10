using Microsoft.EntityFrameworkCore;
using WmsResourcePlanner.Application.Calculations;
using WmsResourcePlanner.Application.Interfaces;
using WmsResourcePlanner.Application.Services;
using WmsResourcePlanner.Infrastructure.Data;
using WmsResourcePlanner.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

builder.Services.AddScoped<ResourceTransformationService>();
builder.Services.AddScoped<ResourcePlanService>();
builder.Services.AddScoped<CapacityService>();
builder.Services.AddScoped<GapAnalysisService>();
builder.Services.AddScoped<LookupService>();
builder.Services.AddScoped<ResourcePlanGridService>();
builder.Services.AddScoped<ScenarioService>();
builder.Services.AddScoped<GanttService>();
builder.Services.AddScoped<ValidationService>();
builder.Services.AddScoped<ImportService>();
builder.Services.AddScoped<WmsResourcePlanner.Infrastructure.Excel.ExcelExportService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "App_Data"));
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    await WmsResourcePlanner.Infrastructure.Data.SeedData.SeedAsync(db);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();


app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

