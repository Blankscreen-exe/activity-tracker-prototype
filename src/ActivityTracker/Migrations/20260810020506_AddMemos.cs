using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActivityTracker.Migrations
{
    /// <inheritdoc />
    public partial class AddMemos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MemoId",
                table: "Sessions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Memos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Memos", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_MemoId",
                table: "Sessions",
                column: "MemoId");

            migrationBuilder.AddForeignKey(
                name: "FK_Sessions_Memos_MemoId",
                table: "Sessions",
                column: "MemoId",
                principalTable: "Memos",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Sessions_Memos_MemoId",
                table: "Sessions");

            migrationBuilder.DropTable(
                name: "Memos");

            migrationBuilder.DropIndex(
                name: "IX_Sessions_MemoId",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "MemoId",
                table: "Sessions");
        }
    }
}
