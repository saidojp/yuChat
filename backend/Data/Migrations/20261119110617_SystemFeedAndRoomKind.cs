using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YuChat.Data.Migrations
{
    /// <inheritdoc />
    public partial class SystemFeedAndRoomKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_Messages_SenderId_ClientMessageId",
                table: "Messages");

            migrationBuilder.AlterColumn<Guid>(
                name: "SenderId",
                table: "Messages",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "ActorId",
                table: "Messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "Messages",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "User");

            migrationBuilder.AddColumn<Guid>(
                name: "ReferenceId",
                table: "Messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceName",
                table: "Messages",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SystemEvent",
                table: "Messages",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "ChatRooms",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Direct");

            // Existing rooms: preserve the previous IsGroup flag before the column is dropped.
            migrationBuilder.Sql("UPDATE \"ChatRooms\" SET \"Kind\" = 'Group' WHERE \"IsGroup\" = TRUE;");

            migrationBuilder.AddColumn<Guid>(
                name: "SystemOwnerId",
                table: "ChatRooms",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Messages_SenderId_ClientMessageId",
                table: "Messages",
                columns: new[] { "SenderId", "ClientMessageId" },
                unique: true,
                filter: "\"SenderId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ChatRooms_SystemOwnerId",
                table: "ChatRooms",
                column: "SystemOwnerId",
                unique: true);

            // Drop the superseded flag only after its data has been carried over to Kind.
            migrationBuilder.DropColumn(
                name: "IsGroup",
                table: "ChatRooms");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Messages_SenderId_ClientMessageId",
                table: "Messages");

            migrationBuilder.DropIndex(
                name: "IX_ChatRooms_SystemOwnerId",
                table: "ChatRooms");

            migrationBuilder.DropColumn(
                name: "ActorId",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "ReferenceId",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "ReferenceName",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "SystemEvent",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "ChatRooms");

            migrationBuilder.DropColumn(
                name: "SystemOwnerId",
                table: "ChatRooms");

            migrationBuilder.AlterColumn<Guid>(
                name: "SenderId",
                table: "Messages",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsGroup",
                table: "ChatRooms",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsRead = table.Column<bool>(type: "boolean", nullable: false),
                    ReferenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notifications_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_SenderId_ClientMessageId",
                table: "Messages",
                columns: new[] { "SenderId", "ClientMessageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_IsRead_CreatedAt",
                table: "Notifications",
                columns: new[] { "UserId", "IsRead", "CreatedAt" });
        }
    }
}
