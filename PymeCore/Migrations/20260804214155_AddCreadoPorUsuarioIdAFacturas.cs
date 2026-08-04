using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PymeCore.Migrations
{
    /// <inheritdoc />
    public partial class AddCreadoPorUsuarioIdAFacturas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CreadoPorUsuarioId",
                table: "Facturas",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Facturas_CreadoPorUsuarioId",
                table: "Facturas",
                column: "CreadoPorUsuarioId");

            migrationBuilder.AddForeignKey(
                name: "FK_Facturas_AspNetUsers_CreadoPorUsuarioId",
                table: "Facturas",
                column: "CreadoPorUsuarioId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Facturas_AspNetUsers_CreadoPorUsuarioId",
                table: "Facturas");

            migrationBuilder.DropIndex(
                name: "IX_Facturas_CreadoPorUsuarioId",
                table: "Facturas");

            migrationBuilder.DropColumn(
                name: "CreadoPorUsuarioId",
                table: "Facturas");
        }
    }
}
