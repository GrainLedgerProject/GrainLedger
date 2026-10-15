using GrainLedger.Modules.Catalogs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrainLedger.Modules.Catalogs.Migrations
{
    [DbContext(typeof(CatalogDbContext))]
    [Migration("20261014120000_AddCatalogAttributionAndCommodityArchive")]
    partial class AddCatalogAttributionAndCommodityArchive
    {
    }
}