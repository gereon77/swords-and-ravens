using System.Text.Json;
using agot_bg_website.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace agot_bg_website.Data
{
    /// <summary>
    /// Fresh code-first schema, see MIGRATION_PLAN.md §4. Uses Guid keys throughout so imported
    /// legacy ids (User/Game/Room) can be preserved exactly during the future data migration.
    /// </summary>
    public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
    {
        public DbSet<Game> Games => Set<Game>();

        public DbSet<PlayerInGame> PlayersInGame => Set<PlayerInGame>();

        public DbSet<PreviousPlayerInGame> PreviousPlayersInGame => Set<PreviousPlayerInGame>();

        public DbSet<PbemResponseTime> PbemResponseTimes => Set<PbemResponseTime>();

        public DbSet<Room> Rooms => Set<Room>();

        public DbSet<Message> Messages => Set<Message>();

        public DbSet<UserInRoom> UsersInRoom => Set<UserInRoom>();

        // EF Core has no built-in mapping for System.Text.Json.JsonDocument (it looks like a
        // complex/owned type to the model builder otherwise, which fails at model-build time with
        // "No suitable constructor was found for type 'JsonDocument'"). Store it as text/jsonb
        // instead and round-trip through JsonDocument.Parse/RootElement.GetRawText().
        private static readonly ValueConverter<JsonDocument?, string?> JsonDocumentConverter = new(
            doc => doc == null ? null : doc.RootElement.GetRawText(),
            json => json == null ? null : JsonDocument.Parse(json, default));

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<ApplicationUser>(user =>
            {
                user.Property(u => u.GameToken).HasMaxLength(64).IsRequired();
                user.HasIndex(u => u.GameToken).IsUnique();
            });

            builder.Entity<Game>(game =>
            {
                game.Property(g => g.Name).HasMaxLength(200).IsRequired();
                game.Property(g => g.State).HasConversion<string>().HasMaxLength(20);
                game.Property(g => g.SerializedGame).HasConversion(JsonDocumentConverter).HasColumnType("jsonb");
                game.Property(g => g.ViewOfGame).HasConversion(JsonDocumentConverter).HasColumnType("jsonb");
                game.HasOne(g => g.OwnerUser).WithMany().HasForeignKey(g => g.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
                game.HasIndex(g => g.State);
            });

            builder.Entity<PlayerInGame>(pig =>
            {
                pig.Property(p => p.Data).HasConversion(JsonDocumentConverter).HasColumnType("jsonb");
                pig.HasOne(p => p.Game).WithMany(g => g.Players).HasForeignKey(p => p.GameId).OnDelete(DeleteBehavior.Cascade);
                pig.HasOne(p => p.User).WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Restrict);
                pig.HasIndex(p => new { p.GameId, p.UserId }).IsUnique();
            });

            builder.Entity<PreviousPlayerInGame>(ppig =>
            {
                ppig.Property(p => p.House).HasMaxLength(50).IsRequired();
                ppig.Property(p => p.Reason).HasConversion<string>().HasMaxLength(30);
                ppig.HasOne(p => p.Game).WithMany(g => g.PreviousPlayers).HasForeignKey(p => p.GameId).OnDelete(DeleteBehavior.Cascade);
                ppig.HasOne(p => p.User).WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Restrict);
                // Natural key: see MIGRATION_PLAN.md §4.4 for why SequenceNumber, not (GameId, UserId, House).
                ppig.HasIndex(p => new { p.GameId, p.SequenceNumber }).IsUnique();
            });

            builder.Entity<PbemResponseTime>(prt =>
            {
                prt.HasOne(p => p.User).WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<Room>(room =>
            {
                room.Property(r => r.Name).HasMaxLength(200).IsRequired();
            });

            builder.Entity<Message>(message =>
            {
                message.Property(m => m.Text).HasMaxLength(200).IsRequired();
                message.HasOne(m => m.Room).WithMany(r => r.Messages).HasForeignKey(m => m.RoomId).OnDelete(DeleteBehavior.Cascade);
                message.HasOne(m => m.User).WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Restrict);
                message.HasIndex(m => new { m.RoomId, m.CreatedAt });
            });

            builder.Entity<UserInRoom>(uir =>
            {
                uir.HasOne(u => u.User).WithMany().HasForeignKey(u => u.UserId).OnDelete(DeleteBehavior.Cascade);
                uir.HasOne(u => u.Room).WithMany(r => r.UsersInRoom).HasForeignKey(u => u.RoomId).OnDelete(DeleteBehavior.Cascade);
                uir.HasIndex(u => new { u.UserId, u.RoomId }).IsUnique();
            });
        }
    }
}
