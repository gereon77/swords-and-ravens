using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace agot_bg_website.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUsageMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UsageHour",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true
            );

            migrationBuilder.AddColumn<long>(
                name: "UsageRequests",
                table: "AspNetUsers",
                type: "bigint",
                nullable: false,
                defaultValue: 0L
            );

            migrationBuilder.CreateTable(
                name: "UsageMetricBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsageMetricBatches", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "UsageMetrics",
                columns: table => new
                {
                    Hour = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    Category = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    Outcome = table.Column<string>(
                        type: "character varying(16)",
                        maxLength: 16,
                        nullable: false
                    ),
                    Requests = table.Column<long>(type: "bigint", nullable: false),
                    DurationSeconds = table.Column<double>(
                        type: "double precision",
                        nullable: false
                    ),
                    ProcessSamples = table.Column<long>(type: "bigint", nullable: false),
                    ProcessCpuPercentSum = table.Column<double>(
                        type: "double precision",
                        nullable: false
                    ),
                    ProcessMemoryBytesMax = table.Column<long>(type: "bigint", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_UsageMetrics",
                        x => new
                        {
                            x.Hour,
                            x.Category,
                            x.Outcome,
                        }
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_UsageHour",
                table: "AspNetUsers",
                column: "UsageHour",
                filter: "\"UsageHour\" IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_UsageMetricBatches_CreatedAt",
                table: "UsageMetricBatches",
                column: "CreatedAt"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "UsageMetricBatches");

            migrationBuilder.DropTable(name: "UsageMetrics");

            migrationBuilder.DropIndex(name: "IX_AspNetUsers_UsageHour", table: "AspNetUsers");

            migrationBuilder.DropColumn(name: "UsageHour", table: "AspNetUsers");

            migrationBuilder.DropColumn(name: "UsageRequests", table: "AspNetUsers");
        }
    }
}
