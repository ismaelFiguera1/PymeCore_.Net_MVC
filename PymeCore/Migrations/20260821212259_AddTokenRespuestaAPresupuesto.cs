using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PymeCore.Migrations
{
    /// <inheritdoc />
    public partial class AddTokenRespuestaAPresupuesto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "TokenRespuestaExpiraUtc",
                table: "Presupuestos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TokenRespuestaHash",
                table: "Presupuestos",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TokenRespuestaExpiraUtc",
                table: "Presupuestos");

            migrationBuilder.DropColumn(
                name: "TokenRespuestaHash",
                table: "Presupuestos");
        }
    }
}
