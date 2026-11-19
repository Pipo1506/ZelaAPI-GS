using Microsoft.EntityFrameworkCore;
using Zela.SLAApi.Models;

namespace Zela.SLAApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<SlaRecord> SlaRecords => Set<SlaRecord>();
    public DbSet<IdempotencyRequest> IdempotencyRequests => Set<IdempotencyRequest>();
}