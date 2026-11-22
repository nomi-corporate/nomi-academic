using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nomi.Academic.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "academic");

            migrationBuilder.CreateTable(
                name: "subjects",
                schema: "academic",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ColorToken = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    IconKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Archived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subjects", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_subjects_OwnerUserId_Archived_Name_Id",
                schema: "academic",
                table: "subjects",
                columns: new[] { "OwnerUserId", "Archived", "Name", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_subjects_OwnerUserId_Id",
                schema: "academic",
                table: "subjects",
                columns: new[] { "OwnerUserId", "Id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "subjects",
                schema: "academic");
        }
    }
}
