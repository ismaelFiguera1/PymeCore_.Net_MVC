using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PymeCore.Migrations
{
    /// <inheritdoc />
    public partial class IndicesUnicosNumero : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Presupuestos_Numero",
                table: "Presupuestos",
                column: "Numero",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_Numero",
                table: "Pedidos",
                column: "Numero",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Facturas_Numero",
                table: "Facturas",
                column: "Numero",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Presupuestos_Numero",
                table: "Presupuestos");

            migrationBuilder.DropIndex(
                name: "IX_Pedidos_Numero",
                table: "Pedidos");

            migrationBuilder.DropIndex(
                name: "IX_Facturas_Numero",
                table: "Facturas");
        }
    }
}
