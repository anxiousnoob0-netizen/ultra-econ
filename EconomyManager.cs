using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TShockAPI;

namespace UltraEconPlugin
{
    /// <summary>
    /// Core economy management system
    /// Handles all economy operations, balances, transactions, and periodic tasks
    /// </summary>
    public class EconomyManager
    {
        private readonly Database _db;
        private Config _config;
        private readonly Dictionary<int, Account> _accountCache;
        private Timer _interestTimer;
        private readonly object _cacheLock = new object();

        public EconomyManager(Database db, Config config)
        {
            _db = db;
            _config = config;
            _accountCache = new Dictionary<int, Account>();
        }

        /// <summary>
        /// Update configuration at runtime
        /// </summary>
        public void UpdateConfig(Config config)
        {
            _config = config;
        }

        /// <summary>
        /// Start periodic tasks like interest payments
        /// </summary>
        public void StartPeriodicTasks()
        {
            if (_config.EnableInterest)
            {
                var interval = TimeSpan.FromMinutes(_config.InterestIntervalMinutes);
                _interestTimer = new Timer(ProcessInterestPayments, null, interval, interval);
                TShock.Log.ConsoleInfo("[UltraEcon] Interest payment system started.");
            }
        }

        /// <summary>
        /// Stop all periodic tasks
        /// </summary>
        public void StopPeriodicTasks()
        {
            _interestTimer?.Dispose();
        }

        /// <summary>
        /// Process interest payments for all accounts
        /// </summary>
        private void ProcessInterestPayments(object state)
        {
            try
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                lock (_cacheLock)
                {
                    foreach (var account in _accountCache.Values)
                    {
                        var timeSinceLastInterest = now - account.LastInterest;
                        if (timeSinceLastInterest >= _config.InterestIntervalMinutes * 60)
                        {
                            var interest = account.Balance * _config.InterestRate;
                            account.Balance += interest;
                            account.LastInterest = now;
                            account.TotalEarned += interest;
                            _db.UpdateAccount(account);
                            _db.LogTransaction(null, account.UserId, interest, "Interest", "Automatic interest payment");
                        }
                    }
                }
                TShock.Log.ConsoleInfo("[UltraEcon] Interest payments processed.");
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[UltraEcon] Error processing interest: {ex.Message}");
            }
        }

        /// <summary>
        /// Load player economy data from database
        /// </summary>
        public void LoadPlayerData(int userId)
        {
            try
            {
                var account = _db.GetAccount(userId);
                if (account == null)
                {
                    _db.CreateAccount(userId, _config.StartingBalance);
                    account = _db.GetAccount(userId);
                }

                lock (_cacheLock)
                {
                    _accountCache[userId] = account;
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[UltraEcon] Error loading player data: {ex.Message}");
            }
        }

        /// <summary>
        /// Save player economy data to database
        /// </summary>
        public void SavePlayerData(int userId)
        {
            try
            {
                lock (_cacheLock)
                {
                    if (_accountCache.TryGetValue(userId, out var account))
                    {
                        _db.UpdateAccount(account);
                        _accountCache.Remove(userId);
                    }
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[UltraEcon] Error saving player data: {ex.Message}");
            }
        }

        /// <summary>
        /// Send welcome message with balance to player
        /// </summary>
        public void SendWelcomeMessage(TSPlayer player)
        {
            var balance = GetBalance(player.Account.ID);
            player.SendSuccessMessage($"[UltraEcon] Welcome! Your balance: {FormatCurrency(balance)}");
        }

        /// <summary>
        /// Get player balance
        /// </summary>
        public decimal GetBalance(int userId)
        {
            lock (_cacheLock)
            {
                if (_accountCache.TryGetValue(userId, out var account))
                {
                    return account.Balance;
                }
            }

            var dbAccount = _db.GetAccount(userId);
            return dbAccount?.Balance ?? 0;
        }

        /// <summary>
        /// Set player balance (admin only)
        /// </summary>
        public bool SetBalance(int userId, decimal amount)
        {
            if (amount < 0 || amount > _config.MaxBalance)
                return false;

            lock (_cacheLock)
            {
                if (_accountCache.TryGetValue(userId, out var account))
                {
                    var oldBalance = account.Balance;
                    account.Balance = amount;
                    _db.UpdateAccount(account);
                    _db.LogTransaction(null, userId, amount - oldBalance, "AdminSet", "Balance set by admin");
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Add money to player account
        /// </summary>
        public bool AddMoney(int userId, decimal amount, string reason = "Admin Grant")
        {
            if (amount <= 0)
                return false;

            lock (_cacheLock)
            {
                if (_accountCache.TryGetValue(userId, out var account))
                {
                    if (account.Balance + amount > _config.MaxBalance)
                        return false;

                    account.Balance += amount;
                    account.TotalEarned += amount;
                    _db.UpdateAccount(account);
                    _db.LogTransaction(null, userId, amount, "Add", reason);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Remove money from player account
        /// </summary>
        public bool RemoveMoney(int userId, decimal amount, string reason = "Admin Remove")
        {
            if (amount <= 0)
                return false;

            lock (_cacheLock)
            {
                if (_accountCache.TryGetValue(userId, out var account))
                {
                    if (account.Balance < amount)
                        return false;

                    account.Balance -= amount;
                    account.TotalSpent += amount;
                    _db.UpdateAccount(account);
                    _db.LogTransaction(userId, null, amount, "Remove", reason);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Transfer money between players
        /// </summary>
        public TransferResult Transfer(int fromUserId, int toUserId, decimal amount)
        {
            if (amount <= 0)
                return new TransferResult { Success = false, Message = "Amount must be positive" };

            if (fromUserId == toUserId)
                return new TransferResult { Success = false, Message = "Cannot transfer to yourself" };

            var tax = amount * _config.TransactionTaxRate;
            var totalDeduction = amount + tax;

            lock (_cacheLock)
            {
                if (!_accountCache.TryGetValue(fromUserId, out var fromAccount))
                    return new TransferResult { Success = false, Message = "Sender account not found" };

                if (!_accountCache.TryGetValue(toUserId, out var toAccount))
                    return new TransferResult { Success = false, Message = "Recipient account not found" };

                if (fromAccount.Balance < totalDeduction)
                    return new TransferResult { Success = false, Message = "Insufficient funds" };

                if (toAccount.Balance + amount > _config.MaxBalance)
                    return new TransferResult { Success = false, Message = "Recipient balance would exceed maximum" };

                fromAccount.Balance -= totalDeduction;
                fromAccount.TotalSpent += totalDeduction;
                toAccount.Balance += amount;
                toAccount.TotalEarned += amount;

                _db.UpdateAccount(fromAccount);
                _db.UpdateAccount(toAccount);
                _db.LogTransaction(fromUserId, toUserId, amount, "Transfer", $"Transfer with {FormatCurrency(tax)} tax");

                return new TransferResult 
                { 
                    Success = true, 
                    Message = $"Transferred {FormatCurrency(amount)} (Tax: {FormatCurrency(tax)})",
                    TaxAmount = tax
                };
            }
        }

        /// <summary>
        /// Claim daily bonus
        /// </summary>
        public BonusResult ClaimDailyBonus(int userId)
        {
            lock (_cacheLock)
            {
                if (!_accountCache.TryGetValue(userId, out var account))
                    return new BonusResult { Success = false, Message = "Account not found" };

                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var timeSinceBonus = now - account.LastBonus;
                var oneDayInSeconds = 86400;

                if (timeSinceBonus < oneDayInSeconds)
                {
                    var timeRemaining = oneDayInSeconds - timeSinceBonus;
                    var hours = timeRemaining / 3600;
                    var minutes = (timeRemaining % 3600) / 60;
                    return new BonusResult 
                    { 
                        Success = false, 
                        Message = $"Daily bonus available in {hours}h {minutes}m"
                    };
                }

                account.Balance += _config.DailyBonusAmount;
                account.LastBonus = now;
                account.TotalEarned += _config.DailyBonusAmount;
                _db.UpdateAccount(account);
                _db.LogTransaction(null, userId, _config.DailyBonusAmount, "DailyBonus", "Daily bonus claimed");

                return new BonusResult 
                { 
                    Success = true, 
                    Message = $"Claimed daily bonus: {FormatCurrency(_config.DailyBonusAmount)}",
                    Amount = _config.DailyBonusAmount
                };
            }
        }

        /// <summary>
        /// Request loan
        /// </summary>
        public LoanResult RequestLoan(int userId, decimal amount, int durationDays)
        {
            if (amount <= 0 || amount > _config.MaxLoanAmount)
                return new LoanResult { Success = false, Message = $"Loan amount must be between 1 and {FormatCurrency(_config.MaxLoanAmount)}" };

            if (durationDays < 1 || durationDays > 365)
                return new LoanResult { Success = false, Message = "Loan duration must be between 1 and 365 days" };

            var activeLoans = _db.GetActiveLoans(userId);
            if (activeLoans.Any())
                return new LoanResult { Success = false, Message = "You already have an active loan" };

            var dueAt = DateTimeOffset.UtcNow.AddDays(durationDays).ToUnixTimeSeconds();
            
            if (_db.CreateLoan(userId, amount, _config.LoanInterestRate, dueAt))
            {
                AddMoney(userId, amount, "Loan disbursement");
                var totalOwed = amount * (1 + _config.LoanInterestRate);
                
                return new LoanResult 
                { 
                    Success = true, 
                    Message = $"Loan approved! You received {FormatCurrency(amount)}. " +
                             $"Total to repay: {FormatCurrency(totalOwed)} by {DateTimeOffset.FromUnixTimeSeconds(dueAt):yyyy-MM-dd}",
                    Amount = amount,
                    TotalOwed = totalOwed
                };
            }

            return new LoanResult { Success = false, Message = "Failed to process loan" };
        }

        /// <summary>
        /// Repay loan
        /// </summary>
        public LoanResult RepayLoan(int userId, decimal amount)
        {
            var loans = _db.GetActiveLoans(userId);
            if (!loans.Any())
                return new LoanResult { Success = false, Message = "No active loans found" };

            var loan = loans.First();
            
            lock (_cacheLock)
            {
                if (!_accountCache.TryGetValue(userId, out var account))
                    return new LoanResult { Success = false, Message = "Account not found" };

                if (account.Balance < amount)
                    return new LoanResult { Success = false, Message = "Insufficient funds" };

                account.Balance -= amount;
                account.TotalSpent += amount;
                loan.RemainingAmount -= amount;

                if (loan.RemainingAmount <= 0)
                {
                    loan.Status = "Paid";
                    _db.UpdateAccount(account);
                    _db.UpdateLoan(loan);
                    _db.LogTransaction(userId, null, amount, "LoanRepayment", "Loan fully repaid");
                    return new LoanResult 
                    { 
                        Success = true, 
                        Message = "Loan fully repaid! Congratulations!",
                        Amount = amount
                    };
                }

                _db.UpdateAccount(account);
                _db.UpdateLoan(loan);
                _db.LogTransaction(userId, null, amount, "LoanRepayment", "Partial loan repayment");
                
                return new LoanResult 
                { 
                    Success = true, 
                    Message = $"Repaid {FormatCurrency(amount)}. Remaining: {FormatCurrency(loan.RemainingAmount)}",
                    Amount = amount,
                    TotalOwed = loan.RemainingAmount
                };
            }
        }

        /// <summary>
        /// Get account statistics
        /// </summary>
        public AccountStats GetStats(int userId)
        {
            lock (_cacheLock)
            {
                if (_accountCache.TryGetValue(userId, out var account))
                {
                    var loans = _db.GetActiveLoans(userId);
                    return new AccountStats
                    {
                        Balance = account.Balance,
                        TotalEarned = account.TotalEarned,
                        TotalSpent = account.TotalSpent,
                        ActiveLoans = loans.Count,
                        TotalLoanDebt = loans.Sum(l => l.RemainingAmount),
                        AccountAge = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - account.CreatedAt
                    };
                }
            }

            return null;
        }

        /// <summary>
        /// Format currency with symbol
        /// </summary>
        public string FormatCurrency(decimal amount)
        {
            return $"{_config.CurrencySymbol}{amount:N2}";
        }
    }

    // Result classes

    public class TransferResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public decimal TaxAmount { get; set; }
    }

    public class BonusResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public decimal Amount { get; set; }
    }

    public class LoanResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public decimal Amount { get; set; }
        public decimal TotalOwed { get; set; }
    }

    public class AccountStats
    {
        public decimal Balance { get; set; }
        public decimal TotalEarned { get; set; }
        public decimal TotalSpent { get; set; }
        public int ActiveLoans { get; set; }
        public decimal TotalLoanDebt { get; set; }
        public long AccountAge { get; set; }
    }
}
