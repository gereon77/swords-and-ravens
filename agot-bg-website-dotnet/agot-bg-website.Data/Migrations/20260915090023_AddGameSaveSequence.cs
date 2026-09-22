using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace agot_bg_website.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGameSaveSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "SaveSequence",
                table: "Games",
                type: "bigint",
                nullable: false,
                defaultValue: 0L
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "SaveSequence", table: "Games");
        }
    }
}
