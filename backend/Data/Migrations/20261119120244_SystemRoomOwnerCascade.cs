using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YuChat.Data.Migrations
{
    /// <inheritdoc />
    public partial class SystemRoomOwnerCascade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "FK_ChatRooms_Users_SystemOwnerId",
                table: "ChatRooms",
                column: "SystemOwnerId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChatRooms_Users_SystemOwnerId",
                table: "ChatRooms");
        }
    }
}
