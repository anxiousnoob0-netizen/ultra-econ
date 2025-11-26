using System;
using System.IO;
using Newtonsoft.Json;
using TShockAPI;

namespace UltraEconPlugin
{
    /// <summary>
    /// Configuration management for the plugin
    /// Handles loading, saving, and validation of configuration settings
    /// </summary>
    public class Config
    {
        private static readonly string ConfigPath = Path.Combine(TShock.SavePath, "UltraEcon.json");

        [JsonProperty("database_type")]
        public string DatabaseType { get; set; } = "sqlite";

        [JsonProperty("database_host")]
        public string DatabaseHost { get; set; } = "localhost";

        [JsonProperty("database_port")]
        public int DatabasePort { get; set; } = 3306;

        [JsonProperty("database_name")]
        public string DatabaseName { get; set; } = "ultraecon";

        [JsonProperty("database_username")]
        public string DatabaseUsername { get; set; } = "root";

        [JsonProperty("database_password")]
        public string DatabasePassword { get; set; } = "";

        [JsonProperty("starting_balance")]
        public decimal StartingBalance { get; set; } = 1000m;

        [JsonProperty("currency_name")]
        public string CurrencyName { get; set; } = "Credits";

        [JsonProperty("currency_symbol")]
        public string CurrencySymbol { get; set; } = "$";

        [JsonProperty("enable_interest")]
        public bool EnableInterest { get; set; } = true;

        [JsonProperty("interest_rate")]
        public decimal InterestRate { get; set; } = 0.05m;

        [JsonProperty("interest_interval_minutes")]
        public int InterestIntervalMinutes { get; set; } = 60;

        [JsonProperty("max_balance")]
        public decimal MaxBalance { get; set; } = 1000000000m;

        [JsonProperty("transaction_tax_rate")]
        public decimal TransactionTaxRate { get; set; } = 0.02m;

        [JsonProperty("enable_logging")]
        public bool EnableLogging { get; set; } = true;

        [JsonProperty("daily_bonus_amount")]
        public decimal DailyBonusAmount { get; set; } = 100m;

        [JsonProperty("enable_bank_system")]
        public bool EnableBankSystem { get; set; } = true;

        [JsonProperty("loan_interest_rate")]
        public decimal LoanInterestRate { get; set; } = 0.10m;

        [JsonProperty("max_loan_amount")]
        public decimal MaxLoanAmount { get; set; } = 50000m;

        /// <summary>
        /// Load configuration from file or create default
        /// </summary>
        public static Config Load()
        {
            try
            {
                if (!File.Exists(ConfigPath))
                {
                    var defaultConfig = new Config();
                    defaultConfig.Save();
                    TShock.Log.ConsoleInfo("[UltraEcon] Created default configuration file.");
                    return defaultConfig;
                }

                var json = File.ReadAllText(ConfigPath);
                var config = JsonConvert.DeserializeObject<Config>(json);
                
                if (!config.Validate())
                {
                    throw new Exception("Configuration validation failed");
                }

                return config;
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[UltraEcon] Error loading config: {ex.Message}");
                return new Config();
            }
        }

        /// <summary>
        /// Save current configuration to file
        /// </summary>
        public void Save()
        {
            try
            {
                var json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[UltraEcon] Error saving config: {ex.Message}");
            }
        }

        /// <summary>
        /// Validate configuration values
        /// </summary>
        public bool Validate()
        {
            if (StartingBalance < 0)
            {
                TShock.Log.ConsoleError("[UltraEcon] Starting balance cannot be negative");
                return false;
            }

            if (InterestRate < 0 || InterestRate > 1)
            {
                TShock.Log.ConsoleError("[UltraEcon] Interest rate must be between 0 and 1");
                return false;
            }

            if (TransactionTaxRate < 0 || TransactionTaxRate > 1)
            {
                TShock.Log.ConsoleError("[UltraEcon] Transaction tax rate must be between 0 and 1");
                return false;
            }

            if (MaxBalance <= 0)
            {
                TShock.Log.ConsoleError("[UltraEcon] Max balance must be positive");
                return false;
            }

            return true;
        }
    }
}
