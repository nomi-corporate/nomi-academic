using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nomi.Academic.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStudyGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "study_groups",
                schema: "academic",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_study_groups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "study_group_memberships",
                schema: "academic",
                columns: table => new
                {
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_study_group_memberships", x => new { x.GroupId, x.UserId });
                    table.ForeignKey(
                        name: "FK_study_group_memberships_study_groups_GroupId",
                        column: x => x.GroupId,
                        principalSchema: "academic",
                        principalTable: "study_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_study_group_memberships_UserId_Active_GroupId",
                schema: "academic",
                table: "study_group_memberships",
                columns: new[] { "UserId", "Active", "GroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_study_groups_OwnerUserId_Active",
                schema: "academic",
                table: "study_groups",
                columns: new[] { "OwnerUserId", "Active" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "study_group_memberships",
                schema: "academic");

            migrationBuilder.DropTable(
                name: "study_groups",
                schema: "academic");
        }
    }
}
