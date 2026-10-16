using GameNet.Server.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameNet.Server.Persistence.Migrations;

[DbContext(typeof(GameNetDbContext))]
[Migration("20261016100000_CashSaleReversal")]
partial class CashSaleReversal
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
        => GameNetDbContextModelSnapshot.BuildMigrationTargetModel(modelBuilder);
}
