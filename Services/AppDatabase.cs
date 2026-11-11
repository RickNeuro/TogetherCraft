using System.Data;
using Npgsql;
using Microsoft.AspNetCore.Identity;
using TogetherCraft.Models;

namespace TogetherCraft.Services;

public sealed class AppDatabase
{
    private readonly string _connectionString;
    private readonly IPasswordHasher<UserAccount> _passwordHasher;

    public AppDatabase(IConfiguration configuration, IPasswordHasher<UserAccount> passwordHasher)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? "Host=localhost;Port=5432;Database=togethercraft;Username=togethercraft;Password=local-dev-only";
        _passwordHasher = passwordHasher;
    }

    public async Task InitializeAsync(string? adminUsername, string? adminPassword, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(adminUsername) || string.IsNullOrWhiteSpace(adminPassword))
            return;

        if (adminPassword.Length < 14 || (adminPassword.Contains("replace", StringComparison.OrdinalIgnoreCase) || adminPassword.Contains("change_me", StringComparison.OrdinalIgnoreCase) || adminPassword.Contains("changeme", StringComparison.OrdinalIgnoreCase) || adminPassword.Contains("example", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("ADMIN_PASSWORD должен быть заменён на уникальный пароль длиной не менее 14 символов.");

        var normalized = adminUsername.Trim().ToUpperInvariant();
        await using var check = new NpgsqlCommand("SELECT id FROM tc_users WHERE username_normalized = @username", connection);
        check.Parameters.AddWithValue("username", normalized);
        var existing = await check.ExecuteScalarAsync(cancellationToken);
        if (existing is not null)
            return;

        var admin = new UserAccount
        {
            Username = adminUsername.Trim(),
            UsernameNormalized = normalized,
            MinecraftUsername = "Admin",
            Role = "Admin",
            Status = "Approved",
            CreatedAtUtc = DateTime.UtcNow,
            ApprovedAtUtc = DateTime.UtcNow
        };
        admin.PasswordHash = _passwordHasher.HashPassword(admin, adminPassword);

        await using var insert = new NpgsqlCommand("""
            INSERT INTO tc_users (username, username_normalized, password_hash, minecraft_username, account_role, account_status, approved_at_utc)
            VALUES (@username, @normalized, @hash, @minecraft, 'Admin', 'Approved', NOW())
            ON CONFLICT (username_normalized) DO NOTHING;
            """, connection);
        insert.Parameters.AddWithValue("username", admin.Username);
        insert.Parameters.AddWithValue("normalized", admin.UsernameNormalized);
        insert.Parameters.AddWithValue("hash", admin.PasswordHash);
        insert.Parameters.AddWithValue("minecraft", admin.MinecraftUsername);
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT 1", connection);
        await command.ExecuteScalarAsync(cancellationToken);
        return true;
    }

    public async Task<UserAccount?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT id, username, username_normalized, password_hash, minecraft_username,
                   account_role, account_status, created_at_utc, approved_at_utc,
                   email, minecraft_uuid, wants_fabric, wants_create
            FROM tc_users WHERE username_normalized = @username LIMIT 1;
            """, connection);
        command.Parameters.AddWithValue("username", username.Trim().ToUpperInvariant());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadUser(reader) : null;
    }

    public async Task<bool> CreateUserAsync(UserAccount user, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            INSERT INTO tc_users
                (username, username_normalized, password_hash, minecraft_username, email, wants_fabric, wants_create)
            VALUES
                (@username, @normalized, @hash, NULL, @email, @wantsFabric, @wantsCreate)
            ON CONFLICT DO NOTHING
            RETURNING id;
            """, connection);
        command.Parameters.AddWithValue("username", user.Username.Trim());
        command.Parameters.AddWithValue("normalized", user.Username.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("hash", user.PasswordHash);
        command.Parameters.AddWithValue("email", (user.Email ?? "").Trim().ToLowerInvariant());
        command.Parameters.AddWithValue("wantsFabric", user.WantsFabric);
        command.Parameters.AddWithValue("wantsCreate", user.WantsCreate);
        var id = await command.ExecuteScalarAsync(cancellationToken);
        if (id is null)
            return false;
        user.Id = Convert.ToInt64(id);
        return true;
    }

    public async Task<bool> SetMinecraftProfileAsync(long id, Guid uuid, string username, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            UPDATE tc_users
            SET minecraft_uuid = @uuid, minecraft_username = @username
            WHERE id = @id
              AND account_role = 'User'
              AND account_status IN ('Pending', 'Approved')
              AND (minecraft_uuid IS NULL OR minecraft_uuid = @uuid);
            """, connection);
        command.Parameters.AddWithValue("uuid", uuid);
        command.Parameters.AddWithValue("username", username);
        command.Parameters.AddWithValue("id", id);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<IReadOnlyList<AdminUserViewModel>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        var users = new List<AdminUserViewModel>();
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT id, username, email, minecraft_username, account_role, account_status, created_at_utc
            FROM tc_users
            ORDER BY CASE WHEN account_role = 'Admin' THEN 0 ELSE 1 END, created_at_utc ASC;
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            users.Add(new AdminUserViewModel
            {
                Id = reader.GetInt64(0),
                Username = reader.GetString(1),
                Email = reader.IsDBNull(2) ? null : reader.GetString(2),
                MinecraftUsername = reader.IsDBNull(3) ? null : reader.GetString(3),
                Role = reader.GetString(4),
                Status = reader.GetString(5),
                CreatedAtUtc = reader.GetDateTime(6)
            });
        }
        return users;
    }

    public async Task<bool> SetUserRoleAsync(long id, string role, CancellationToken cancellationToken = default)
    {
        if (role is not ("Admin" or "User"))
            throw new ArgumentOutOfRangeException(nameof(role));

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        // Serialize role changes so two administrators cannot remove the last admin simultaneously.
        await using (var lockCommand = new NpgsqlCommand(
            "LOCK TABLE tc_users IN SHARE ROW EXCLUSIVE MODE;", connection, transaction))
        {
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        string? currentRole = null;
        string? currentStatus = null;
        await using (var find = new NpgsqlCommand(
            "SELECT account_role, account_status FROM tc_users WHERE id = @id FOR UPDATE;", connection, transaction))
        {
            find.Parameters.AddWithValue("id", id);
            await using var reader = await find.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                currentRole = reader.GetString(0);
                currentStatus = reader.GetString(1);
            }
        }

        if (currentRole is null || currentStatus == "Rejected")
            return false;

        if (currentRole == role)
        {
            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        if (role == "User" && currentRole == "Admin")
        {
            await using var count = new NpgsqlCommand(
                "SELECT COUNT(*) FROM tc_users WHERE account_role = 'Admin';", connection, transaction);
            var adminCount = Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken));
            if (adminCount <= 1)
                return false;
        }

        await using (var update = new NpgsqlCommand("""
            UPDATE tc_users
            SET account_role = @role,
                account_status = 'Approved',
                approved_at_utc = COALESCE(approved_at_utc, NOW())
            WHERE id = @id;
            """, connection, transaction))
        {
            update.Parameters.AddWithValue("role", role);
            update.Parameters.AddWithValue("id", id);
            var updated = await update.ExecuteNonQueryAsync(cancellationToken);
            if (updated != 1)
                return false;
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<UserAccount>> GetPendingUsersAsync(CancellationToken cancellationToken = default)
    {
        var users = new List<UserAccount>();
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT id, username, username_normalized, password_hash, minecraft_username,
                   account_role, account_status, created_at_utc, approved_at_utc,
                   email, minecraft_uuid, wants_fabric, wants_create
            FROM tc_users WHERE account_status = 'Pending' ORDER BY created_at_utc ASC;
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            users.Add(ReadUser(reader));
        return users;
    }

    public async Task<UserAccount?> FindByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT id, username, username_normalized, password_hash, minecraft_username,
                   account_role, account_status, created_at_utc, approved_at_utc,
                   email, minecraft_uuid, wants_fabric, wants_create
            FROM tc_users WHERE id = @id LIMIT 1;
            """, connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadUser(reader) : null;
    }

    public async Task<bool> SetStatusAsync(long id, string newStatus, CancellationToken cancellationToken = default)
    {
        if (newStatus is not ("Approved" or "Rejected"))
            throw new ArgumentOutOfRangeException(nameof(newStatus));

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var sql = newStatus == "Approved"
            ? "UPDATE tc_users SET account_status = 'Approved', approved_at_utc = NOW() WHERE id = @id AND account_status = 'Pending' AND account_role = 'User'"
            : "UPDATE tc_users SET account_status = 'Rejected', approved_at_utc = NULL WHERE id = @id AND account_status = 'Pending' AND account_role = 'User'";
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static UserAccount ReadUser(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        Username = reader.GetString(1),
        UsernameNormalized = reader.GetString(2),
        PasswordHash = reader.GetString(3),
        MinecraftUsername = reader.IsDBNull(4) ? null : reader.GetString(4),
        Role = reader.GetString(5),
        Status = reader.GetString(6),
        CreatedAtUtc = reader.GetDateTime(7),
        ApprovedAtUtc = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
        Email = reader.IsDBNull(9) ? null : reader.GetString(9),
        MinecraftUuid = reader.IsDBNull(10) ? null : reader.GetGuid(10),
        WantsFabric = reader.GetBoolean(11),
        WantsCreate = reader.GetBoolean(12)
    };
}
