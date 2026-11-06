using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CFIS.Api.Migrations
{
    /// <inheritdoc />
    public partial class TransactionAllocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "transaction_allocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transaction_allocations", x => x.Id);
                    table.CheckConstraint("ck_allocation_amount", "\"Amount\">0 AND \"Amount\"<>'NaN'::numeric");
                    table.ForeignKey(
                        name: "FK_transaction_allocations_categories_OrganizationId_CategoryId",
                        columns: x => new { x.OrganizationId, x.CategoryId },
                        principalTable: "categories",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transaction_allocations_transactions_OrganizationId_Transac~",
                        columns: x => new { x.OrganizationId, x.TransactionId },
                        principalTable: "transactions",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_transaction_allocations_OrganizationId_CategoryId",
                table: "transaction_allocations",
                columns: new[] { "OrganizationId", "CategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_transaction_allocations_OrganizationId_TransactionId_Catego~",
                table: "transaction_allocations",
                columns: new[] { "OrganizationId", "TransactionId", "CategoryId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "transaction_allocations");
        }
    }
}
