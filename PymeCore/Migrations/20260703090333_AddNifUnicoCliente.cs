using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PymeCore.Migrations
{
    /// <inheritdoc />
    public partial class AddNifUnicoCliente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Clientes_Nif",
                table: "Clientes",
                column: "Nif",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Clientes_Nif",
                table: "Clientes");
        }
    }
}
