using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PymeCore.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckConstraintsProductos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_Productos_PrecioCoste_NoNegativo",
                table: "Productos",
                sql: "\"PrecioCoste\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Productos_PrecioVenta_NoNegativo",
                table: "Productos",
                sql: "\"PrecioVenta\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Productos_StockActual_NoNegativo",
                table: "Productos",
                sql: "\"StockActual\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Productos_StockMinimo_NoNegativo",
                table: "Productos",
                sql: "\"StockMinimo\" >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Productos_PrecioCoste_NoNegativo",
                table: "Productos");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Productos_PrecioVenta_NoNegativo",
                table: "Productos");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Productos_StockActual_NoNegativo",
                table: "Productos");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Productos_StockMinimo_NoNegativo",
                table: "Productos");
        }
    }
}
