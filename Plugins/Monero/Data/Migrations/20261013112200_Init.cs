using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Plugins.Monero.Data.Migrations;

/// <inheritdoc />
[DbContext(typeof(MoneroDbContext))]
[Migration("20261013112200_Init")]
public class Init : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "MoneroPlugin");

        migrationBuilder.CreateTable(
            name: "StoreWallets",
            schema: "MoneroPlugin",
            columns: table => new
            {
                StoreId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                PrimaryAddress = table.Column<string>(type: "character varying(106)", maxLength: 106, nullable: false),
                PrivateViewKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_StoreWallets", x => x.StoreId);
            });
    }
}