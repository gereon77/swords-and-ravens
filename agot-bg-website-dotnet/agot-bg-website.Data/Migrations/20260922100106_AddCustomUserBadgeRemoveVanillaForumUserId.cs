using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace agot_bg_website.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomUserBadgeRemoveVanillaForumUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "VanillaForumUserId", table: "AspNetUsers");

            migrationBuilder.AddColumn<string>(
                name: "CustomUserBadge",
                table: "AspNetUsers",
                type: "text",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "CustomUserBadge", table: "AspNetUsers");

            migrationBuilder.AddColumn<int>(
                name: "VanillaForumUserId",
                table: "AspNetUsers",
                type: "integer",
                nullable: false,
                defaultValue: 0
            );
        }
    }
}
