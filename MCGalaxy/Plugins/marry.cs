using System;
using MCGalaxy;
using MCGalaxy.DB;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace Core {
    public sealed class MarryPlugin : Plugin {
        public override string name => "marryplugin";
        public override string creator => "";
        public override string welcome => "";
        public override string MCGalaxy_Version => "1.9.0.7";

        static OnlineStatPrinter onlineLine;
        static OfflineStatPrinter offlineLine;

        public override void Load(bool startup) {
            Command.Register(new CmdAccept());
            Command.Register(new CmdDeny());
            Command.Register(new CmdDivorce());
            Command.Register(new CmdMarry());

            Married.Load();
            onlineLine = (p, who) => FormatMarriedTo(p, who.name);
            offlineLine = (p, who) => FormatMarriedTo(p, who.Name);
            OnlineStat.Stats.Add(onlineLine);
            OfflineStat.Stats.Add(offlineLine);
        }

        public override void Unload(bool shutdown) {
            Command.Unregister(Command.Find("Accept"));
            Command.Unregister(Command.Find("Deny"));
            Command.Unregister(Command.Find("Divorce"));
            Command.Unregister(Command.Find("Marry"));

            OnlineStat.Stats.Remove(onlineLine);
            OfflineStat.Stats.Remove(offlineLine);
        }

        static void FormatMarriedTo(Player p, string who) {
            List<string> names = Married.Get(who);
            if (names.Count == 0) { return; }
            p.Message("  Married to {0}", names.Join((name) => p.FormatNick(name), "&S, "));
        }


        public static class Married {
            static PlayerExtList marriages;

            internal static void Load() {
                marriages = PlayerExtList.Load("extra/marriages.txt");
            }

            public static List<string> Get(string name) {
                string data = marriages.Get(name);
                if (data == null) { return new List<string>(); }
                return new List<string>(data.Split(','));
            }

            public static void AddPair(string nameA, string nameB) {
                var prevMarriedA = Get(nameA);
                var prevMarriedB = Get(nameB);
                if (!prevMarriedA.Contains(nameB)) {
                    prevMarriedA.Add(nameB);
                }
                if (!prevMarriedB.Contains(nameA)) {
                    prevMarriedB.Add(nameA);
                }

                marriages.Update(nameA, prevMarriedA.Join(","));
                marriages.Update(nameB, prevMarriedB.Join(","));
                marriages.Save();
            }

            public static void RemovePair(string nameA, string nameB) {
                var prevMarriedA = Get(nameA);
                var prevMarriedB = Get(nameB);
                prevMarriedA.Remove(nameB);
                prevMarriedB.Remove(nameA);

                if (prevMarriedA.Count == 0) {
                    marriages.Remove(nameA);
                } else {
                    marriages.Update(nameA, prevMarriedA.Join(","));
                }
                if (prevMarriedB.Count == 0) {
                    marriages.Remove(nameB);
                } else {
                    marriages.Update(nameB, prevMarriedB.Join(","));
                }
                marriages.Save();
            }
        }

        public static class Proposal {
            public const string ExtraKey = "__Marry_Name";

            static ConcurrentDictionary<string, DateTime> cooldowns = new ConcurrentDictionary<string, DateTime>();

            public static void AddTimeout(Player p, int days) {
                TimeSpan coolDown = TimeSpan.FromDays(days);
                cooldowns[p.name] = DateTime.UtcNow + coolDown;
            }

            public static TimeSpan GetCooldown(Player p) {
                cooldowns.TryGetValue(p.name, out DateTime expires);
                return expires - DateTime.UtcNow;
            }

            public static string Get(Player p) {
                return p.Extras.GetString(ExtraKey);
            }

            public static void Add(Player p, string name) {
                p.Extras[ExtraKey] = name;
            }

            public static void Remove(Player p) {
                p.Extras.Remove(ExtraKey);
            }

            public static Player Check(Player p) {
                string name = Get(p);
                if (name == null) {
                    p.Message("You do not have a pending marriage proposal.");
                    return null;
                }

                Player src = PlayerInfo.FindExact(name);
                if (src == null) {
                    p.Message("The person who proposed to marry you isn't online.");
                    return null;
                }

                return src;
            }
        }
    }

    public abstract class CmdBase : Command {
        public override string shortcut => "";
        public override string type => "fun";
        public override LevelPermission defaultRank => LevelPermission.Guest;
        public override bool museumUsable => true;
        public override bool MessageBlockRestricted => true;
    }

    public sealed class CmdAccept : CmdBase {
        public override string name => "Accept";

        public override void Help(Player p) {
            p.Message("%T/Accept %H- Accepts a pending marriage proposal.");
        }

        public override void Use(Player p, string message) {
            Player proposer = MarryPlugin.Proposal.Check(p);
            if (proposer == null) { return; }

            Chat.MessageGlobal("-{0} &aaccepted {1}%S's proposal, and they are now happily married-",
                               p.ColoredName, proposer.ColoredName);
            p.Message("&bYou &aaccepted &b{0}&b's proposal", proposer.ColoredName);

            MarryPlugin.Married.AddPair(p.name, proposer.name);
            MarryPlugin.Proposal.Remove(p);
        }
    }

    public sealed class CmdDeny : CmdBase {
        public override string name => "Deny";

        public override void Help(Player p) {
            p.Message("%T/Deny %H- Denies a pending marriage proposal.");
        }

        public override void Use(Player p, string message) {
            Player proposer = MarryPlugin.Proposal.Check(p);
            if (proposer == null) { return; }

            Chat.MessageGlobal("-{0} %Sdenied {1}%S's proposal, it just wasn't meant to be-",
                               p.ColoredName, proposer.ColoredName);
            p.Message("&bYou &cdenied &b{0}&b's proposal", proposer.ColoredName);

            MarryPlugin.Proposal.Remove(p);
            MarryPlugin.Proposal.AddTimeout(proposer, 1);
        }
    }

    public sealed class CmdDivorce : CmdBase {
        public override string name => "Divorce";

        public override void Help(Player p) {
            p.Message("%T/Divorce <player>");
            p.Message("%HLeaves the player you are currently married to.");
            p.Message("%HIf no player is specified, you will divorce your current spouse.");
        }

        public override void Use(Player p, string message) {
            string name = message.Trim();

            List<string> marriedTo = MarryPlugin.Married.Get(p.name);
            if (marriedTo.Count == 0) {
                p.Message("You are not married to anyone.");
                return;
            }

            if (name.Length == 0) {
                if (marriedTo.Count == 1) {
                    name = marriedTo[0];
                } else {
                    Help(p);
                    p.Message("You are married to multiple players. Please specify one.");
                    p.Message("Married to: {0}", marriedTo.Join(", "));
                    return;
                }
            }

            string partnerName = PlayerInfo.FindMatchesPreferOnline(p, name);
            if (partnerName != null) {
                name = partnerName;
            }

            if (!marriedTo.Contains(name)) {
                p.Message("You are not married to {0}", name);
                p.Message("Married to: {0}", marriedTo.Join(", "));
                return;
            }

            MarryPlugin.Married.RemovePair(p.name, name);
            Chat.MessageGlobal("-{0}%S just divorced {1}%S-", p.ColoredName, p.FormatNick(name));

            Player partner = PlayerInfo.FindExact(name);
            partner?.Message("{0} &bjust divorced you.", p.ColoredName);
        }
    }

    public sealed class CmdMarry : CmdBase {
        public override string name => "Marry";

        public override void Help(Player p) {
            p.Message("%T/Marry [player]");
            p.Message("%HProposes to the given player.");
        }

        public override void Use(Player p, string message) {
            string name = message.Trim();
            if (name.Length == 0) {
                Help(p);
                return;
            }

            TimeSpan coolDown = MarryPlugin.Proposal.GetCooldown(p);
            if (coolDown.TotalSeconds > 0) {
                coolDown += new TimeSpan(0, 0, 0, 1, 0);
                p.Message("You must wait {0} to emotionally recover before proposing again.", coolDown.Shorten(true, false));
                return;
            }

            Player partner = PlayerInfo.FindMatches(p, message);
            if (partner == null) { return; }
            if (partner == p) {
                p.Message("You cannot marry yourself.");
                return;
            }

            if (MarryPlugin.Proposal.Get(partner) == p.name) {
                p.Message("You have already proposed to {0}!", partner.ColoredName);
                return;
            }

            List<string> marriedTo = MarryPlugin.Married.Get(p.name);
            List<string> partnerMarriedTo = MarryPlugin.Married.Get(partner.name);

            if (marriedTo.Contains(partner.name) || partnerMarriedTo.Contains(p.name)) {
                p.Message("You are already married to {0}", partner.ColoredName);

                if (!marriedTo.Contains(partner.name) || !partnerMarriedTo.Contains(p.name)) {
                    // fix storage if one of the players is not married to the other
                    MarryPlugin.Married.AddPair(p.name, partner.name);
                }
                return;
            }

            Chat.MessageGlobal("-{0}%S gets down on one knee-",
                               p.ColoredName);
            Chat.MessageGlobal(string.Format("{0}%S is asking {1}%S for {2} hand in marriage!",
                               p.ColoredName, partner.ColoredName, partner.pronouns.Object));

            MarryPlugin.Proposal.Add(partner, p.name);
            partner.Message("&bTo accept {0} proposal type &a/Accept", p.pronouns.Object);
            partner.Message("&bOr to deny it, type &c/Deny");
        }
    }
}
