using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nomi.Academic.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCommitmentCreateOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                schema: "academic",
                table: "commitments",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                schema: "academic",
                table: "commitments",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.CreateTable(
                name: "outbox_events",
                schema: "academic",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AggregateId = table.Column<Guid>(type: "uuid", nullable: false),
                    AggregateVersion = table.Column<int>(type: "integer", nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_events", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_events_AggregateId_AggregateVersion",
                schema: "academic",
                table: "outbox_events",
                columns: new[] { "AggregateId", "AggregateVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_outbox_events_PublishedAt_OccurredAt",
                schema: "academic",
                table: "outbox_events",
                columns: new[] { "PublishedAt", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox_events",
                schema: "academic");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                schema: "academic",
                table: "commitments");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                schema: "academic",
                table: "commitments");
        }
    }
}
