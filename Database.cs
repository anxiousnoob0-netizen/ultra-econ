using System;
using System.Data;
using System.Collections.Generic;
using Dapper;
using MySql.Data.MySqlClient;
using Microsoft.Data.Sqlite;
using TShockAPI;
using TShockAPI.DB;

namespace UltraEconPlugin
{
    /// <summary>
    /// Database connection and management for economy data
    /// Supports both MySQL and SQLite with automatic table creation
    /// </summary>
    public class Database
    {
        private IDbConnection _connection;
        private readonly Config _config;
        private readonly string _connectionString;
        private readonly bool _isSqlite;

        public Database(Config config)
        {
            _config = config;
            _isSqlite = config.DatabaseType.ToLower() == "sqlite";

            if (_isSqlite)
            {
                var dbPath = Path.Combine(TShock.SavePath, "UltraEcon.sqlite");
                _connectionString = $"Data Source={dbPath}";
            }
            else
            {
                _connectionString = $"Server={config.DatabaseHost};Port={config.DatabasePort};" +
                                  $"Database={config.DatabaseName};Uid={config.DatabaseUsername};" +
                                  $"Pwd={config.DatabasePassword};";
            }
        }

        /// <summary>
        /// Establish database connection
        /// </summary>
        public void Connect()
        {
            try
            {
                _connection = _isSqlite 
                    ? new SqliteConnection(_connectionString) 
                    : new MySqlConnection(_connectionString);
                
                _connection.Open();
                TShock.Log.ConsoleInfo($"[UltraEcon] Connected to {(_isSqlite ? "SQLite" : "MySQL")} database.");
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[UltraEcon] Database connection error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Close database connection
        /// </summary>
        public void Disconnect()
        {
            _connection?.Close();
            _connection?.Dispose();
            TShock.Log.ConsoleInfo("[UltraEcon] Database disconnected.");
        }

        /// <summary>
        /// Initialize all required database tables
        /// </summary>
        public void InitializeTables()
        {
            try
            {
                // Accounts table
                var createAccountsTable = _isSqlite
                    ? @"CREATE TABLE IF NOT EXISTS Accounts (
                        UserId INTEGER PRIMARY KEY,
                        Balance REAL NOT NULL DEFAULT 0,
                        LastInterest INTEGER NOT NULL DEFAULT 0,
                        LastBonus INTEGER NOT NULL DEFAULT 0,
                        TotalEarned REAL NOT NULL DEFAULT 0,
                        TotalSpent REAL NOT NULL DEFAULT 0,
                        CreatedAt INTEGER NOT NULL,
                        UpdatedAt INTEGER NOT NULL
                    )"
                    : @"CREATE TABLE IF NOT EXISTS Accounts (
                        UserId INT PRIMARY KEY,
                        Balance DECIMAL(20,2) NOT NULL DEFAULT 0,
                        LastInterest BIGINT NOT NULL DEFAULT 0,
                        LastBonus BIGINT NOT NULL DEFAULT 0,
                        TotalEarned DECIMAL(20,2) NOT NULL DEFAULT 0,
                        TotalSpent DECIMAL(20,2) NOT NULL DEFAULT 0,
                        CreatedAt BIGINT NOT NULL,
                        UpdatedAt BIGINT NOT NULL
                    )";

                _connection.Execute(createAccountsTable);

                // Transactions table
                var createTransactionsTable = _isSqlite
                    ? @"CREATE TABLE IF NOT EXISTS Transactions (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        FromUserId INTEGER,
                        ToUserId INTEGER,
                        Amount REAL NOT NULL,
                        Type TEXT NOT NULL,
                        Description TEXT,
                        Timestamp INTEGER NOT NULL
                    )"
                    : @"CREATE TABLE IF NOT EXISTS Transactions (
                        Id INT AUTO_INCREMENT PRIMARY KEY,
                        FromUserId INT,
                        ToUserId INT,
                        Amount DECIMAL(20,2) NOT NULL,
                        Type VARCHAR(50) NOT NULL,
                        Description TEXT,
                        Timestamp BIGINT NOT NULL
                    )";

                _connection.Execute(createTransactionsTable);

                // Loans table
                var createLoansTable = _isSqlite
                    ? @"CREATE TABLE IF NOT EXISTS Loans (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        UserId INTEGER NOT NULL,
                        Amount REAL NOT NULL,
                        InterestRate REAL NOT NULL,
                        RemainingAmount REAL NOT NULL,
                        IssuedAt INTEGER NOT NULL,
                        DueAt INTEGER NOT NULL,
                        Status TEXT NOT NULL
                    )"
                    : @"CREATE TABLE IF NOT EXISTS Loans (
                        Id INT AUTO_INCREMENT PRIMARY KEY,
                        UserId INT NOT NULL,
                        Amount DECIMAL(20,2) NOT NULL,
                        InterestRate DECIMAL(5,4) NOT NULL,
                        RemainingAmount DECIMAL(20,2) NOT NULL,
                        IssuedAt BIGINT NOT NULL,
                        DueAt BIGINT NOT NULL,
                        Status VARCHAR(20) NOT NULL
                    )";

                _connection.Execute(createLoansTable);

                // Shops table
                var createShopsTable = _isSqlite
                    ? @"CREATE TABLE IF NOT EXISTS Shops (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        ItemId INTEGER NOT NULL,
                        ItemName TEXT NOT NULL,
                        BuyPrice REAL,
                        SellPrice REAL,
                        Stock INTEGER NOT NULL DEFAULT -1,
                        Category TEXT
                    )"
                    : @"CREATE TABLE IF NOT EXISTS Shops (
                        Id INT AUTO_INCREMENT PRIMARY KEY,
                        ItemId INT NOT NULL,
                        ItemName VARCHAR(100) NOT NULL,
                        BuyPrice DECIMAL(20,2),
                        SellPrice DECIMAL(20,2),
                        Stock INT NOT NULL DEFAULT -1,
                        Category VARCHAR(50)
                    )";

                _connection.Execute(createShopsTable);

                TShock.Log.ConsoleInfo("[UltraEcon] Database tables initialized successfully.");
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[UltraEcon] Error initializing tables: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Get account by user ID
        /// </summary>
        public Account GetAccount(int userId)
        {
            try
            {
                var query = "SELECT * FROM Accounts WHERE UserId = @UserId";
                return _connection.QueryFirstOrDefault<Account>(query, new { UserId = userId });
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[UltraEcon] Error getting account: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Create new account
        /// </summary>
        public bool CreateAccount(int userId, decimal startingBalance)
        {
            try
            {
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var query = @"INSERT INTO Accounts (UserId, Balance, LastInterest, LastBonus, 
                            TotalEarned, TotalSpent, CreatedAt, UpdatedAt) 
                            VALUES (@UserId, @Balance, @Timestamp, @Timestamp, 0, 0, @Timestamp, @Timestamp)";
                
                _connection.Execute(query, new { UserId = userId, Balance = startingBalance, Timestamp = timestamp });
                return true;
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[UltraEcon] Error creating account: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Update account balance and statistics
        /// </summary>
        public bool UpdateAccount(Account account)
        {
            try
            {
                account.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var query = @"UPDATE Accounts SET Balance = @Balance, LastInterest = @LastInterest, 
                            LastBonus = @LastBonus, TotalEarned = @TotalEarned, TotalSpent = @TotalSpent, 
                            UpdatedAt = @UpdatedAt WHERE UserId = @UserId";
                
                _connection.Execute(query, account);
                return true;
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[UltraEcon] Error updating account: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Log transaction to database
        /// </summary>
        public void LogTransaction(int? fromUserId, int? toUserId, decimal amount, string type, string description)
        {
            try
            {
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var query = @"INSERT INTO Transactions (FromUserId, ToUserId, Amount, Type, Description, Timestamp) 
                            VALUES (@FromUserId, @ToUserId, @Amount, @Type, @Description, @Timestamp)";
                
                _connection.Execute(query, new 
                { 
                    FromUserId = fromUserId, 
                    ToUserId = toUserId, 
                    Amount = amount, 
                    Type = type, 
                    Description = description, 
                    Timestamp = timestamp 
                });
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[UltraEcon] Error logging transaction: {ex.Message}");
            }
        }

        /// <summary>
        /// Get transaction history for user
        /// </summary>
        public List<Transaction> GetTransactionHistory(int userId, int limit = 10)
        {
            try
            {
                var query = @"SELECT * FROM Transactions 
                            WHERE FromUserId = @UserId OR ToUserId = @UserId 
                            ORDER BY Timestamp DESC LIMIT @Limit";
                
                return _connection.Query<Transaction>(query, new { UserId = userId, Limit = limit }).AsList();
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[UltraEcon] Error getting transaction history: {ex.Message}");
                return new List<Transaction>();
            }
        }

        /// <summary>
        /// Create new loan
        /// </summary>
        public bool CreateLoan(int userId, decimal amount, decimal interestRate, long dueAt)
        {
            try
            {
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var totalAmount = amount * (1 + interestRate);
                var query = @"INSERT INTO Loans (UserId, Amount, InterestRate, RemainingAmount, 
                            IssuedAt, DueAt, Status) 
                            VALUES (@UserId, @Amount, @InterestRate, @RemainingAmount, @IssuedAt, @DueAt, 'Active')";
                
                _connection.Execute(query, new 
                { 
                    UserId = userId, 
                    Amount = amount, 
                    InterestRate = interestRate, 
                    RemainingAmount = totalAmount, 
                    IssuedAt = timestamp, 
                    DueAt = dueAt 
                });
                return true;
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[UltraEcon] Error creating loan: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get active loans for user
        /// </summary>
        public List<Loan> GetActiveLoans(int userId)
        {
            try
            {
                var query = "SELECT * FROM Loans WHERE UserId = @UserId AND Status = 'Active'";
                return _connection.Query<Loan>(query, new { UserId = userId }).AsList();
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[UltraEcon] Error getting loans: {ex.Message}");
                return new List<Loan>();
            }
        }

        /// <summary>
        /// Update loan status and remaining amount
        /// </summary>
        public bool UpdateLoan(Loan loan)
        {
            try
            {
                var query = @"UPDATE Loans SET RemainingAmount = @RemainingAmount, Status = @Status 
                            WHERE Id = @Id";
                _connection.Execute(query, loan);
                return true;
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[UltraEcon] Error updating loan: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get all shop items
        /// </summary>
        public List<ShopItem> GetShopItems(string category = null)
        {
            try
            {
                var query = category == null 
                    ? "SELECT * FROM Shops" 
                    : "SELECT * FROM Shops WHERE Category = @Category";
                
                return _connection.Query<ShopItem>(query, new { Category = category }).AsList();
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[UltraEcon] Error getting shop items: {ex.Message}");
                return new List<ShopItem>();
            }
        }

        /// <summary>
        /// Add or update shop item
        /// </summary>
        public bool UpsertShopItem(ShopItem item)
        {
            try
            {
                var existing = _connection.QueryFirstOrDefault<ShopItem>(
                    "SELECT * FROM Shops WHERE ItemId = @ItemId", new { item.ItemId });

                if (existing != null)
                {
                    var query = @"UPDATE Shops SET ItemName = @ItemName, BuyPrice = @BuyPrice, 
                                SellPrice = @SellPrice, Stock = @Stock, Category = @Category 
                                WHERE ItemId = @ItemId";
                    _connection.Execute(query, item);
                }
                else
                {
                    var query = @"INSERT INTO Shops (ItemId, ItemName, BuyPrice, SellPrice, Stock, Category) 
                                VALUES (@ItemId, @ItemName, @BuyPrice, @SellPrice, @Stock, @Category)";
                    _connection.Execute(query, item);
                }
                return true;
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[UltraEcon] Error upserting shop item: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get top accounts by balance
        /// </summary>
        public List<Account> GetTopAccounts(int limit = 10)
        {
            try
            {
                var query = "SELECT * FROM Accounts ORDER BY Balance DESC LIMIT @Limit";
                return _connection.Query<Account>(query, new { Limit = limit }).AsList();
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[UltraEcon] Error getting top accounts: {ex.Message}");
                return new List<Account>();
            }
        }
    }

    // Data models

    public class Account
    {
        public int UserId { get; set; }
        public decimal Balance { get; set; }
        public long LastInterest { get; set; }
        public long LastBonus { get; set; }
        public decimal TotalEarned { get; set; }
        public decimal TotalSpent { get; set; }
        public long CreatedAt { get; set; }
        public long UpdatedAt { get; set; }
    }

    public class Transaction
    {
        public int Id { get; set; }
        public int? FromUserId { get; set; }
        public int? ToUserId { get; set; }
        public decimal Amount { get; set; }
        public string Type { get; set; }
        public string Description { get; set; }
        public long Timestamp { get; set; }
    }

    public class Loan
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public decimal Amount { get; set; }
        public decimal InterestRate { get; set; }
        public decimal RemainingAmount { get; set; }
        public long IssuedAt { get; set; }
        public long DueAt { get; set; }
        public string Status { get; set; }
    }

    public class ShopItem
    {
        public int Id { get; set; }
        public int ItemId { get; set; }
        public string ItemName { get; set; }
        public decimal? BuyPrice { get; set; }
        public decimal? SellPrice { get; set; }
        public int Stock { get; set; }
        public string Category { get; set; }
    }
}
