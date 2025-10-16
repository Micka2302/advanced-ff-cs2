using System;
using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Memory;

namespace AdvancedFriendlyFire
{ 
    public class AdvancedFriendlyFireConfig : BasePluginConfig
    {
        [JsonPropertyName("Enable/Disable Advanced Friendly Fire")]
        public bool IsAdvancedFriendlyFireEnabled { get; set; } = true;

        [JsonPropertyName("Enable/Disable Punishments")]
        public bool ArePunishmentsEnabled { get; set; } = true;

        [JsonPropertyName("Damage Inflictors")]
        public string[] DamageInflictors { get; set; } =
        {
            "inferno",
            "hegrenade_projectile",
            "flashbang_projectile",
            "smokegrenade_projectile",
            "decoy_projectile",
            "planted_c4"
        };

        [JsonPropertyName("Warning #1 Required Team Damage (HP Metrics)")]
        public int Warn1 { get; set; } = 100;

        [JsonPropertyName("Warning #1 Chat message")]
        public string chatWarn1 { get; set; } = "Avoid friendly fire, or you will be punished! Friendly fire warning [1/3]";

        [JsonPropertyName("Warning #1 Punishment")]
        public string punishWarn1 { get; set; } = "css_slay {Player} \"Friendly fire warning [1/3]\"";

        [JsonPropertyName("Warning #2 Required Team Damage (HP Metrics)")]
        public int Warn2 { get; set; } = 200;

        [JsonPropertyName("Warning #2 Chat message")]
        public string chatWarn2 { get; set; } = "You have been kicked for dealing excessive damage to your teammates!";

        [JsonPropertyName("Warning #2 Punishment")]
        public string punishWarn2 { get; set; } = "css_kick {Player} \"Friendly fire warning [2/3]\"";

        [JsonPropertyName("Warning #3 Required Team Damage (HP Metrics)")]
        public int Warn3 { get; set; } = 300;

        [JsonPropertyName("Warning #3 Chat message")]
        public string chatWarn3 { get; set; } = "You have been banned for dealing excessive damage to your teammates!";

        [JsonPropertyName("Warning #3 Punishment")]
        public string punishWarn3 { get; set; } = "css_ban {Player} 30 \"Friendly fire warning [3/3]\"";
    }

    [MinimumApiVersion(342)]
    public class AdvancedFriendlyFire : BasePlugin, IPluginConfig<AdvancedFriendlyFireConfig>
    {
        public override string ModuleName => "Advanced Friendly Fire [Extracted from Argentum Suite]";
        public override string ModuleVersion => "1.1.5";
        public override string ModuleAuthor => "phara1";
        public override string ModuleDescription => "https://steamcommunity.com/id/kenoxyd";

        public required AdvancedFriendlyFireConfig Config { get; set; }
        public void OnConfigParsed(AdvancedFriendlyFireConfig config)
        {
            Config = config;
        }

        public override void Load(bool hotReload)
        {
            VirtualFunctions.CBaseEntity_TakeDamageOldFunc.Hook(OnAdvancedFriendlyFireHook, HookMode.Pre);

            RegisterEventHandler<EventPlayerHurt>(OnPlayerHurt);

            if (Config.IsAdvancedFriendlyFireEnabled)
            {
                Server.ExecuteCommand("mp_friendlyfire 1");
                Server.ExecuteCommand("ff_damage_reduction_bullets 0.0");
                Server.ExecuteCommand("ff_damage_reduction_grenade 0.85");
                Server.ExecuteCommand("ff_damage_reduction_grenade_self 1");
                Server.ExecuteCommand("ff_damage_reduction_other 0.4");
            }

        }

        public override void Unload(bool hotReload)
        {
            VirtualFunctions.CBaseEntity_TakeDamageOldFunc.Unhook(OnAdvancedFriendlyFireHook, HookMode.Pre);
        }

        private Dictionary<ulong, (float damage, string attackerName)> tempDamageTracker = new Dictionary<ulong, (float, string)>();
        private Dictionary<ulong, float> teamDamageTracker = new Dictionary<ulong, float>();
        private Dictionary<ulong, int> punishmentLevelTracker = new Dictionary<ulong, int>();

        private HookResult OnPlayerHurt(EventPlayerHurt eventInfo, GameEventInfo info)
        {
            if (eventInfo == null)
            {
                Console.WriteLine("eventInfo is null, skipping...");
                return HookResult.Continue;
            }

            var attacker = eventInfo?.Attacker;
            var victim = eventInfo?.Userid;

            if (attacker == null && victim == null)
            {
                return HookResult.Continue;
            }

            if (attacker != null)
            {
                ulong attackerSteamId = attacker.SteamID;

                if (attacker != victim)
                {
                    var damageTaken = eventInfo.DmgHealth;

                    string attackerName = attacker.PlayerName;  // Safe to access after null check

                    tempDamageTracker[attackerSteamId] = (damageTaken, attackerName);
                }
            }


            return HookResult.Continue;
        }


        private HookResult OnAdvancedFriendlyFireHook(DynamicHook hook)
        {
            if (!Config.IsAdvancedFriendlyFireEnabled)
            {
                return HookResult.Continue;
            }

            var victimEntity = hook.GetParam<CEntityInstance>(0);
            var damageInfo = hook.GetParam<CTakeDamageInfo>(1);

            if (victimEntity == null || damageInfo == null)
            {
                return HookResult.Continue;
            }

            if (!string.Equals(victimEntity.DesignerName, "player", StringComparison.OrdinalIgnoreCase))
            {
                return HookResult.Continue;
            }

            var attackerHandle = damageInfo.Attacker;
            if (attackerHandle == null || !attackerHandle.IsValid)
            {
                return HookResult.Continue;
            }

            var attackerEntity = attackerHandle.Value;
            if (attackerEntity == null)
            {
                return HookResult.Continue;
            }

            var victimPawn = new CCSPlayerPawn(victimEntity.Handle);
            var attackerPawn = new CCSPlayerPawn(attackerEntity.Handle);

            var victimControllerHandle = victimPawn.Controller;
            var attackerControllerHandle = attackerPawn.Controller;

            if (!victimControllerHandle.IsValid || !attackerControllerHandle.IsValid)
            {
                return HookResult.Continue;
            }

            var victimControllerBase = victimControllerHandle.Value;
            var attackerControllerBase = attackerControllerHandle.Value;

            if (victimControllerBase == null || attackerControllerBase == null)
            {
                return HookResult.Continue;
            }

            var victimController = new CCSPlayerController(victimControllerBase.Handle);
            var attackerController = new CCSPlayerController(attackerControllerBase.Handle);

            if (attackerController.TeamNum != victimController.TeamNum ||
                attackerController.SteamID == victimController.SteamID)
            {
                return HookResult.Continue;
            }

            var inflictorName = damageInfo.Inflictor.Value?.DesignerName ?? string.Empty;
            if (!Config.DamageInflictors.Contains(inflictorName))
            {
                return HookResult.Continue;
            }

            attackerController.PrintToCenterAlert("DON'T HURT YOUR TEAMMATES!");

            if (!Config.ArePunishmentsEnabled)
            {
                return HookResult.Continue;
            }

            var attackerSteamId = attackerController.SteamID;

            if (!tempDamageTracker.TryGetValue(attackerSteamId, out var attackerInfo))
            {
                attackerInfo = (damageInfo.Damage, attackerController.PlayerName);
            }

            var damageAmount = attackerInfo.damage > 0 ? attackerInfo.damage : damageInfo.Damage;
            var attackerName = string.IsNullOrWhiteSpace(attackerInfo.attackerName)
                ? attackerController.PlayerName
                : attackerInfo.attackerName;

            if (!teamDamageTracker.TryGetValue(attackerSteamId, out float totalDamage))
            {
                totalDamage = 0;
            }

            totalDamage += damageAmount;
            teamDamageTracker[attackerSteamId] = totalDamage;

            if (!punishmentLevelTracker.TryGetValue(attackerSteamId, out int punishmentLevel))
            {
                punishmentLevel = 0;
            }

            if (totalDamage >= Config.Warn1 && punishmentLevel < 1)
            {
                attackerController.PrintToChat(Config.chatWarn1);

                var commandToExecute = Config.punishWarn1;

                if (commandToExecute.Contains("{Player}", StringComparison.Ordinal))
                {
                    Server.ExecuteCommand(commandToExecute.Replace("{Player}", attackerName, StringComparison.Ordinal));
                }

                punishmentLevelTracker[attackerSteamId] = 1;
            }
            else if (totalDamage >= Config.Warn2 && punishmentLevel < 2)
            {
                attackerController.PrintToChat(Config.chatWarn2);

                var commandToExecute = Config.punishWarn2;

                if (commandToExecute.Contains("{Player}", StringComparison.Ordinal))
                {
                    Server.ExecuteCommand(commandToExecute.Replace("{Player}", attackerName, StringComparison.Ordinal));
                }

                punishmentLevelTracker[attackerSteamId] = 2;
            }
            else if (totalDamage >= Config.Warn3 && punishmentLevel < 3)
            {
                attackerController.PrintToChat(Config.chatWarn3);

                var commandToExecute = Config.punishWarn3;

                if (commandToExecute.Contains("{Player}", StringComparison.Ordinal))
                {
                    Server.ExecuteCommand(commandToExecute.Replace("{Player}", attackerName, StringComparison.Ordinal));
                }

                teamDamageTracker[attackerSteamId] = 0;
                punishmentLevelTracker[attackerSteamId] = 0;
            }

            return HookResult.Continue;
        }

    }
}

