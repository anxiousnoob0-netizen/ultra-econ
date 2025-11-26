using System;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;

namespace UltraEconPlugin
{
    /// <summary>
    /// Main plugin class for UltraEcon TShock integration
    /// Manages plugin lifecycle, initialization, and cleanup
    /// </summary>
    [ApiVersion(2, 1)]
    public class Plugin : TerrariaPlugin
    {
        public override string Name => "UltraEcon Advanced Plugin";
        public override Version Version => new Version(1, 0, 0);
        public override string Author => "Advanced Economy Team";
        public override string Description => "Production-ready UltraEcon plugin with extensive economy features";

        public static EconomyManager Economy { get; private set; }
        public static CommandHandler Commands { get; private set; }
        public static Database Database { get; private set; }
        public static Config Config { get; private set; }
        public static Security Security { get; private set; }

        public Plugin(Main game) : base(game)
        {
            Order = 1;
        }

        /// <summary>
        /// Initialize plugin components and register hooks
        /// </summary>
        public override void Initialize()
        {
            try
            {
                // Load configuration
                Config = Config.Load();
                TShock.Log.ConsoleInfo("[UltraEcon] Configuration loaded successfully.");

                // Initialize security layer
                Security = new Security();

                // Initialize database connection
                Database = new Database(Config);
                Database.Connect();
                Database.InitializeTables();
                TShock.Log.ConsoleInfo("[UltraEcon] Database initialized successfully.");

                // Initialize economy manager
                Economy = new EconomyManager(Database, Config);
                TShock.Log.ConsoleInfo("[UltraEcon] Economy manager initialized successfully.");

                // Initialize and register commands
                Commands = new CommandHandler(Economy, Security);
                Commands.RegisterCommands();
                TShock.Log.ConsoleInfo("[UltraEcon] Commands registered successfully.");

                // Register hooks
                RegisterHooks();

                TShock.Log.ConsoleInfo($"[UltraEcon] Plugin v{Version} initialized successfully.");
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[UltraEcon] Failed to initialize: {ex.Message}");
                TShock.Log.ConsoleError($"[UltraEcon] Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        /// <summary>
        /// Register event hooks for player actions
        /// </summary>
        private void RegisterHooks()
        {
            ServerApi.Hooks.GamePostInitialize.Register(this, OnPostInit);
            ServerApi.Hooks.ServerLeave.Register(this, OnServerLeave);
            PlayerHooks.PlayerPostLogin.Register(this, OnPlayerLogin);
            GeneralHooks.ReloadEvent += OnReload;
        }

        /// <summary>
        /// Unregister event hooks
        /// </summary>
        private void DeregisterHooks()
        {
            ServerApi.Hooks.GamePostInitialize.Deregister(this, OnPostInit);
            ServerApi.Hooks.ServerLeave.Deregister(this, OnServerLeave);
            PlayerHooks.PlayerPostLogin.Deregister(this, OnPlayerLogin);
            GeneralHooks.ReloadEvent -= OnReload;
        }

        private void OnPostInit(EventArgs args)
        {
            Economy.StartPeriodicTasks();
        }

        private void OnServerLeave(LeaveEventArgs args)
        {
            if (args.Who >= 0 && args.Who < Main.maxPlayers)
            {
                var player = TShock.Players[args.Who];
                if (player != null && player.IsLoggedIn)
                {
                    Economy.SavePlayerData(player.Account.ID);
                }
            }
        }

        private void OnPlayerLogin(PlayerPostLoginEventArgs args)
        {
            Economy.LoadPlayerData(args.Player.Account.ID);
            Economy.SendWelcomeMessage(args.Player);
        }

        private void OnReload(ReloadEventArgs args)
        {
            Config = Config.Load();
            Economy.UpdateConfig(Config);
            args.Player.SendSuccessMessage("[UltraEcon] Configuration reloaded successfully.");
        }

        /// <summary>
        /// Clean up resources when plugin is disposed
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DeregisterHooks();
                Economy?.StopPeriodicTasks();
                Database?.Disconnect();
                TShock.Log.ConsoleInfo("[UltraEcon] Plugin disposed successfully.");
            }
            base.Dispose(disposing);
        }
    }
}
