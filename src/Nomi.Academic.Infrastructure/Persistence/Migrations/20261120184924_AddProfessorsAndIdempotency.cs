using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nomi.Academic.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProfessorsAndIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_subjects_OwnerUserId_Id",
                schema: "academic",
                table: "subjects");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_subjects_OwnerUserId_Id",
                schema: "academic",
                table: "subjects",
                columns: new[] { "OwnerUserId", "Id" });

            migrationBuilder.CreateTable(
                name: "idempotency_records",
                schema: "academic",
                columns: table => new
                {
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Operation = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ResponseJson = table.Column<string>(type: "text", nullable: true),
                    ETag = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Location = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    LeaseExpiresAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_idempotency_records", x => new { x.OwnerUserId, x.Operation, x.Key });
                });

            migrationBuilder.CreateTable(
                name: "professors",
                schema: "academic",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    Office = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    Archived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_professors", x => x.Id);
                    table.UniqueConstraint("AK_professors_OwnerUserId_Id", x => new { x.OwnerUserId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "subject_professors",
                schema: "academic",
                columns: table => new
                {
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfessorId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subject_professors", x => new { x.OwnerUserId, x.SubjectId, x.ProfessorId });
                    table.ForeignKey(
                        name: "FK_subject_professors_professors_OwnerUserId_ProfessorId",
                        columns: x => new { x.OwnerUserId, x.ProfessorId },
                        principalSchema: "academic",
                        principalTable: "professors",
                        principalColumns: new[] { "OwnerUserId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_subject_professors_subjects_OwnerUserId_SubjectId",
                        columns: x => new { x.OwnerUserId, x.SubjectId },
                        principalSchema: "academic",
                        principalTable: "subjects",
                        principalColumns: new[] { "OwnerUserId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_idempotency_records_CreatedAt",
                schema: "academic",
                table: "idempotency_records",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_professors_OwnerUserId_Archived_Name_Id",
                schema: "academic",
                table: "professors",
                columns: new[] { "OwnerUserId", "Archived", "Name", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_subject_professors_OwnerUserId_ProfessorId",
                schema: "academic",
                table: "subject_professors",
                columns: new[] { "OwnerUserId", "ProfessorId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "idempotency_records",
                schema: "academic");

            migrationBuilder.DropTable(
                name: "subject_professors",
                schema: "academic");

            migrationBuilder.DropTable(
                name: "professors",
                schema: "academic");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_subjects_OwnerUserId_Id",
                schema: "academic",
                table: "subjects");

            migrationBuilder.CreateIndex(
                name: "IX_subjects_OwnerUserId_Id",
                schema: "academic",
                table: "subjects",
                columns: new[] { "OwnerUserId", "Id" },
                unique: true);
        }
    }
}
