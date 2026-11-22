using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nomi.Academic.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCommitments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "commitments",
                schema: "academic",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    LocalDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TimeZone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DueInstant = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Deleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_commitments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_commitments_subjects_OwnerUserId_SubjectId",
                        columns: x => new { x.OwnerUserId, x.SubjectId },
                        principalSchema: "academic",
                        principalTable: "subjects",
                        principalColumns: new[] { "OwnerUserId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_commitments_OwnerUserId_Deleted_DueInstant",
                schema: "academic",
                table: "commitments",
                columns: new[] { "OwnerUserId", "Deleted", "DueInstant" });

            migrationBuilder.CreateIndex(
                name: "IX_commitments_OwnerUserId_Deleted_LocalDate",
                schema: "academic",
                table: "commitments",
                columns: new[] { "OwnerUserId", "Deleted", "LocalDate" });

            migrationBuilder.CreateIndex(
                name: "IX_commitments_OwnerUserId_SubjectId",
                schema: "academic",
                table: "commitments",
                columns: new[] { "OwnerUserId", "SubjectId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "commitments",
                schema: "academic");
        }
    }
}
