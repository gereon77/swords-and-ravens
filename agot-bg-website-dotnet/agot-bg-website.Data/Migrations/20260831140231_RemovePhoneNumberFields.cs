using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace agot_bg_website.Migrations
{
    /// <inheritdoc />
    public partial class RemovePhoneNumberFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "PhoneNumber", table: "AspNetUsers");

            migrationBuilder.DropColumn(name: "PhoneNumberConfirmed", table: "AspNetUsers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PhoneNumber",
                table: "AspNetUsers",
                type: "text",
                nullable: true
            );

            migrationBuilder.AddColumn<bool>(
                name: "PhoneNumberConfirmed",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false
            );
        }
    }
}
