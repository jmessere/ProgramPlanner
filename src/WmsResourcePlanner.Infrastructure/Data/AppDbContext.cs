using Microsoft.EntityFrameworkCore;
using WmsResourcePlanner.Application.Interfaces;
using WmsResourcePlanner.Domain.Entities;

namespace WmsResourcePlanner.Infrastructure.Data;

public class AppDbContext : DbContext, IAppDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Program> Programs => Set<Program>();

    public DbSet<PlanningScenario> PlanningScenarios => Set<PlanningScenario>();

    public DbSet<Template> Templates => Set<Template>();

    public DbSet<TemplatePhase> TemplatePhases => Set<TemplatePhase>();

    public DbSet<Workstream> Workstreams => Set<Workstream>();

    public DbSet<FocusArea> FocusAreas => Set<FocusArea>();

    public DbSet<Team> Teams => Set<Team>();

    public DbSet<TeamTemplateAssignment> TeamTemplateAssignments => Set<TeamTemplateAssignment>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<TeamRole> TeamRoles => Set<TeamRole>();

    public DbSet<Person> People => Set<Person>();

    public DbSet<ResourcePlanLine> ResourcePlanLines => Set<ResourcePlanLine>();

    public DbSet<Site> Sites => Set<Site>();

    public DbSet<TeamSiteAssignment> TeamSiteAssignments => Set<TeamSiteAssignment>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Domain.Entities.Program>(e =>
        {
            e.Property(p => p.Name).IsRequired().HasMaxLength(200);
            e.HasMany(p => p.Scenarios).WithOne(s => s.Program).HasForeignKey(s => s.ProgramId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(p => p.Templates).WithOne(t => t.Program).HasForeignKey(t => t.ProgramId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(p => p.Workstreams).WithOne(w => w.Program).HasForeignKey(w => w.ProgramId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(p => p.Teams).WithOne(t => t.Program).HasForeignKey(t => t.ProgramId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(p => p.Roles).WithOne(r => r.Program).HasForeignKey(r => r.ProgramId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(p => p.People).WithOne(pp => pp.Program).HasForeignKey(pp => pp.ProgramId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(p => p.Sites).WithOne(s => s.Program).HasForeignKey(s => s.ProgramId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PlanningScenario>(e =>
        {
            e.Property(p => p.Name).IsRequired().HasMaxLength(200);
            e.HasMany(s => s.ResourcePlanLines).WithOne(r => r.Scenario).HasForeignKey(r => r.ScenarioId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Template>(e =>
        {
            e.Property(t => t.Name).IsRequired().HasMaxLength(200);
            e.HasMany(t => t.Phases).WithOne(p => p.Template).HasForeignKey(p => p.TemplateId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(t => t.TeamAssignments).WithOne(a => a.Template).HasForeignKey(a => a.TemplateId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TemplatePhase>(e =>
        {
            e.Property(p => p.Name).IsRequired().HasMaxLength(200);
            e.HasMany(p => p.ResourcePlanLines).WithOne(r => r.TemplatePhase).HasForeignKey(r => r.TemplatePhaseId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Workstream>(e =>
        {
            e.Property(w => w.Name).IsRequired().HasMaxLength(200);
            e.HasMany(w => w.FocusAreas).WithOne(f => f.Workstream).HasForeignKey(f => f.WorkstreamId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(w => w.Teams).WithOne(t => t.Workstream).HasForeignKey(t => t.WorkstreamId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(w => w.ResourcePlanLines).WithOne(r => r.Workstream).HasForeignKey(r => r.WorkstreamId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<FocusArea>(e =>
        {
            e.Property(f => f.Name).IsRequired().HasMaxLength(200);
            e.HasMany(f => f.Teams).WithOne(t => t.FocusArea).HasForeignKey(t => t.FocusAreaId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(f => f.ResourcePlanLines).WithOne(r => r.FocusArea).HasForeignKey(r => r.FocusAreaId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Team>(e =>
        {
            e.Property(t => t.Name).IsRequired().HasMaxLength(200);
            e.Property(t => t.TeamType).IsRequired().HasMaxLength(100);
            e.HasMany(t => t.TemplateAssignments).WithOne(a => a.Team).HasForeignKey(a => a.TeamId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(t => t.TeamRoles).WithOne(r => r.Team).HasForeignKey(r => r.TeamId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(t => t.SiteAssignments).WithOne(s => s.Team).HasForeignKey(s => s.TeamId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(t => t.ResourcePlanLines).WithOne(r => r.Team).HasForeignKey(r => r.TeamId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TeamTemplateAssignment>(e =>
        {
            e.HasMany(a => a.ResourcePlanLines).WithOne(r => r.TeamTemplateAssignment).HasForeignKey(r => r.TeamTemplateAssignmentId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Role>(e =>
        {
            e.Property(r => r.Name).IsRequired().HasMaxLength(200);
            e.HasMany(r => r.TeamRoles).WithOne(tr => tr.Role).HasForeignKey(tr => tr.RoleId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(r => r.ResourcePlanLines).WithOne(rpl => rpl.Role).HasForeignKey(rpl => rpl.RoleId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Person>(e =>
        {
            e.Property(p => p.FirstName).IsRequired().HasMaxLength(100);
            e.Property(p => p.LastName).IsRequired().HasMaxLength(100);
            e.Property(p => p.DisplayName).IsRequired().HasMaxLength(200);
            e.Property(p => p.DefaultCapacityFte).HasColumnType("decimal(5,2)");
            e.HasOne(p => p.PrimaryRole).WithMany().HasForeignKey(p => p.PrimaryRoleId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(p => p.ResourcePlanLines).WithOne(r => r.Person).HasForeignKey(r => r.PersonId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ResourcePlanLine>(e =>
        {
            e.Property(r => r.Fte).HasColumnType("decimal(6,2)");
            e.HasIndex(r => new { r.ScenarioId, r.TeamId, r.RoleId, r.PersonId });
            e.HasIndex(r => new { r.StartDate, r.EndDate });
        });

        modelBuilder.Entity<Site>(e =>
        {
            e.Property(s => s.Name).IsRequired().HasMaxLength(200);
            e.HasMany(s => s.TeamAssignments).WithOne(a => a.Site).HasForeignKey(a => a.SiteId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
