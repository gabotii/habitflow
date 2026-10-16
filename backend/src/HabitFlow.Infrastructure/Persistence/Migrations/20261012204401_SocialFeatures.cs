using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HabitFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SocialFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Challenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Rule = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TargetValue = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    Unit = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    JoinCode = table.Column<string>(type: "character(8)", fixedLength: true, maxLength: 8, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Challenges", x => x.Id);
                    table.CheckConstraint("CK_Challenges_Dates", "\"EndDate\" >= \"StartDate\"");
                    table.CheckConstraint("CK_Challenges_Target", "\"TargetValue\" > 0");
                    table.ForeignKey(
                        name: "FK_Challenges_AspNetUsers_CreatorId",
                        column: x => x.CreatorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "SharedGoals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Unit = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    CollectiveTarget = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    IndividualTarget = table.Column<decimal>(type: "numeric(14,2)", nullable: true),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Deadline = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SharedGoals", x => x.Id);
                    table.CheckConstraint("CK_SharedGoals_Deadline", "\"Deadline\" IS NULL OR \"Deadline\" >= \"StartDate\"");
                    table.CheckConstraint("CK_SharedGoals_Target", "\"CollectiveTarget\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "ChallengeParticipants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChallengeId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    HabitId = table.Column<Guid>(type: "uuid", nullable: true),
                    JoinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LeftOn = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChallengeParticipants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChallengeParticipants_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChallengeParticipants_Challenges_ChallengeId",
                        column: x => x.ChallengeId,
                        principalTable: "Challenges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChallengeParticipants_Habits_HabitId",
                        column: x => x.HabitId,
                        principalTable: "Habits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "SharedGoalContributions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SharedGoalId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocalDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    Note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SharedGoalContributions", x => x.Id);
                    table.CheckConstraint("CK_SharedGoalContributions_Amount", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_SharedGoalContributions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SharedGoalContributions_SharedGoals_SharedGoalId",
                        column: x => x.SharedGoalId,
                        principalTable: "SharedGoals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SharedGoalInvitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SharedGoalId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvitedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RespondedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SharedGoalInvitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SharedGoalInvitations_AspNetUsers_InvitedByUserId",
                        column: x => x.InvitedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SharedGoalInvitations_SharedGoals_SharedGoalId",
                        column: x => x.SharedGoalId,
                        principalTable: "SharedGoals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SharedGoalParticipants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SharedGoalId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    JoinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LeftAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SharedGoalParticipants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SharedGoalParticipants_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SharedGoalParticipants_SharedGoals_SharedGoalId",
                        column: x => x.SharedGoalId,
                        principalTable: "SharedGoals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChallengeParticipants_ChallengeId_UserId",
                table: "ChallengeParticipants",
                columns: new[] { "ChallengeId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChallengeParticipants_HabitId",
                table: "ChallengeParticipants",
                column: "HabitId");

            migrationBuilder.CreateIndex(
                name: "IX_ChallengeParticipants_UserId",
                table: "ChallengeParticipants",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Challenges_CreatorId",
                table: "Challenges",
                column: "CreatorId");

            migrationBuilder.CreateIndex(
                name: "IX_Challenges_JoinCode",
                table: "Challenges",
                column: "JoinCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SharedGoalContributions_SharedGoalId_UserId",
                table: "SharedGoalContributions",
                columns: new[] { "SharedGoalId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_SharedGoalContributions_UserId",
                table: "SharedGoalContributions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_SharedGoalInvitations_InvitedByUserId",
                table: "SharedGoalInvitations",
                column: "InvitedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SharedGoalInvitations_NormalizedEmail",
                table: "SharedGoalInvitations",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_SharedGoalInvitations_SharedGoalId_NormalizedEmail",
                table: "SharedGoalInvitations",
                columns: new[] { "SharedGoalId", "NormalizedEmail" },
                unique: true,
                filter: "\"Status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_SharedGoalParticipants_SharedGoalId_UserId",
                table: "SharedGoalParticipants",
                columns: new[] { "SharedGoalId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SharedGoalParticipants_UserId",
                table: "SharedGoalParticipants",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChallengeParticipants");

            migrationBuilder.DropTable(
                name: "SharedGoalContributions");

            migrationBuilder.DropTable(
                name: "SharedGoalInvitations");

            migrationBuilder.DropTable(
                name: "SharedGoalParticipants");

            migrationBuilder.DropTable(
                name: "Challenges");

            migrationBuilder.DropTable(
                name: "SharedGoals");
        }
    }
}
