using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace agot_bg_website.Migrations
{
    /// <inheritdoc />
    public partial class RenameUseResponsiveLayoutOnMobileToGameStateColumnRight : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "UseResponsiveLayoutOnMobile",
                table: "AspNetUsers",
                newName: "GameStateColumnRight");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "GameStateColumnRight",
                table: "AspNetUsers",
                newName: "UseResponsiveLayoutOnMobile");
        }
    }
}
