using MaintainXpert.Maintenance.Application;
using MaintainXpert.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace MaintainXpert.Api.Infrastructure;

public sealed class SqlWorkOrderRepository : IWorkOrderRepository
{
    private readonly AppDbContext _db;

    public SqlWorkOrderRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(WorkOrder workOrder, CancellationToken cancellationToken = default)
    {
        _db.WorkOrders.Add(workOrder);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<WorkOrder?> GetByIdAsync(WorkOrderId id, CancellationToken cancellationToken = default)
    {
        var workOrder = await _db.WorkOrders
            .FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

        return workOrder;
    }

    public async Task UpdateAsync(WorkOrder workOrder, CancellationToken cancellationToken = default)
    {
        await _db.SaveChangesAsync(cancellationToken);
    }
}
