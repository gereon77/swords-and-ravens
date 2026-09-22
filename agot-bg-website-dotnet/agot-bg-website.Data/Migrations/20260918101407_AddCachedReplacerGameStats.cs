using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace agot_bg_website.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCachedReplacerGameStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CachedReplacerGamesCount",
                table: "AspNetUsers",
                type: "integer",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "CachedReplacerLossesExcludedCount",
                table: "AspNetUsers",
                type: "integer",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "CachedReplacerWinsCount",
                table: "AspNetUsers",
                type: "integer",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "CachedReplacerGamesCount", table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "CachedReplacerLossesExcludedCount",
                table: "AspNetUsers"
            );

            migrationBuilder.DropColumn(name: "CachedReplacerWinsCount", table: "AspNetUsers");
        }
    }
}
