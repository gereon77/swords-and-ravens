using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace agot_bg_website.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserCachedStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CachedFinishedGamesCount",
                table: "AspNetUsers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CachedRemovedFromGameCount",
                table: "AspNetUsers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "CachedWinRate",
                table: "AspNetUsers",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CachedWonGamesCount",
                table: "AspNetUsers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StatsCachedAt",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CachedFinishedGamesCount",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "CachedRemovedFromGameCount",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "CachedWinRate",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "CachedWonGamesCount",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "StatsCachedAt",
                table: "AspNetUsers");
        }
    }
}
