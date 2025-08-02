using System;
using MCGalaxy;
using MCGalaxy.Commands;
using MCGalaxy.DB;
using System.Collections.Generic;

namespace Core {
    public sealed class MarryPlugin : Plugin {
        public override string name { get { return "marryplugin"; } }
        public override string creator { get { return ""; } }
        public override string welcome { get { return ""; } }
        public override string MCGalaxy_Version { get { return "1.9.0.7"; } }

        public const string ExtraName = "__Marry_Name";
        static PlayerExtList marriages;
        static OnlineStatPrinter onlineLine;
        static OfflineStatPrinter offlineLine;

        public override void Load(bool startup) {
            Command.Register(new CmdAccept());
            Command.Register(new CmdDeny());
            Command.Register(new CmdDivorce());
            Command.Register(new CmdMarry());

            marriages = PlayerExtList.Load("extra/marriages.txt");
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
            string data = GetMarriedPlayer(who);
            if (data == null) return;
            p.Message("  Married to {0}", p.FormatNick(data));
        }

        public static string GetMarriedPlayer(string name) {
            string data = marriages.Get(name);
            if (data == null) return null;
            return data;
        }

        public static void UpdateMarriedPlayer(string name, string marriedTo) {
            marriages.Update(name, marriedTo);
            marriages.Update(marriedTo, name);
            marriages.Save();
        }

        public static void RemoveMarriedPlayer(string name, string marriedTo) {
            marriages.Remove(name);
            marriages.Remove(marriedTo);
            marriages.Save();
        }
    }

    public sealed class CmdAccept : CmdDeny {
        public override string name { get { return "Accept"; } }
        public override string shortcut { get { return ""; } }
        public override string type { get { return "fun"; } }
        public override bool museumUsable { get { return true; } }
        public override LevelPermission defaultRank { get { return LevelPermission.Guest; } }

        public override void Use(Player p, string message) {
            Player proposer = CheckProposal(p);
            if (proposer == null) return;

            Chat.MessageGlobal("-{0} &aaccepted {1}%S's proposal, and they are now happily married-",
                               p.ColoredName, proposer.ColoredName);
            p.Message("&bYou &aaccepted &b{0}&b's proposal", proposer.ColoredName);

            MarryPlugin.UpdateMarriedPlayer(p.name, proposer.name);
            p.Extras.Remove(MarryPlugin.ExtraName);
        }

        public override void Help(Player p) {
            p.Message("%T/Accept %H- Accepts a pending marriage proposal.");
        }
    }

    public class CmdDeny : Command {
        public override bool MessageBlockRestricted { get { return true; } }
        public override string name { get { return "Deny"; } }
        public override string type { get { return "fun"; } }

        public override void Use(Player p, string message) {
            Player proposer = CheckProposal(p);
            if (proposer == null) return;

            Chat.MessageGlobal("-{0} %Sdenied {1}%S's proposal, it just wasn't meant to be-",
                               p.ColoredName, proposer.ColoredName);
            p.Message("&bYou &cdenied &b{0}&b's proposal", proposer.ColoredName);

            p.Extras.Remove(MarryPlugin.ExtraName);
        }

        protected Player CheckProposal(Player p) {
            string name = p.Extras.GetString(MarryPlugin.ExtraName);
            if (name == null) {
                p.Message("You do not have a pending marriage proposal."); return null;
            }

            Player src = PlayerInfo.FindExact(name);
            if (src == null) {
                p.Message("The person who proposed to marry you isn't online."); return null;
            }

            if (MarryPlugin.GetMarriedPlayer(name) != null) {
                p.Message(name + " is already married to someone else.");
                p.Extras.Remove(MarryPlugin.ExtraName); return null;
            }

            if (MarryPlugin.GetMarriedPlayer(p.name) != null) {
                p.Message("You are already married to someone else.");
                return null;
            }
            return src;
        }

        public override void Help(Player p) {
            p.Message("%T/Deny %H- Denies a pending marriage proposal.");
        }
    }

    public sealed class CmdDivorce : Command {
        public override bool MessageBlockRestricted { get { return true; } }
        public override string name { get { return "Divorce"; } }
        public override string type { get { return "fun"; } }

        public override void Use(Player p, string message) {
            string marriedTo = MarryPlugin.GetMarriedPlayer(p.name);
            if (marriedTo == null) { p.Message("You are not married to anyone."); return; }

            MarryPlugin.RemoveMarriedPlayer(p.name, marriedTo);
            Player partner = PlayerInfo.FindExact(marriedTo);

            Chat.MessageGlobal("-{0}%S just divorced {1}%S-",
                               p.ColoredName, p.FormatNick(marriedTo));
            if (partner != null)
                partner.Message("{0} &bjust divorced you.", p.ColoredName);
        }

        public override void Help(Player p) {
            p.Message("%T/Divorce");
            p.Message("%HLeaves the player you are currently married to.");
        }
    }

    public sealed class CmdMarry : Command {
        public override bool MessageBlockRestricted { get { return true; } }
        public override string name { get { return "Marry"; } }
        public override string type { get { return "fun"; } }

        //cooldown ------------------
        static readonly object locker = new object();
        static Dictionary<string, DateTime> cooldowns = new Dictionary<string, DateTime>();
        public static void InitiateDelay(Player p, int days) {
            TimeSpan coolDown = TimeSpan.FromDays(days);
            lock (locker) { cooldowns[p.name] = DateTime.UtcNow + coolDown; }
        }
        TimeSpan GetCooldown(Player p) {
            DateTime expires;
            lock (locker) { cooldowns.TryGetValue(p.name, out expires); }
            return expires - DateTime.UtcNow;
        }
        //------------------------------

        public override void Use(Player p, string message) {
            string entry = MarryPlugin.GetMarriedPlayer(p.name);
            if (entry != null) {
                p.Message("You are already married to someone"); return;
            }

            Player partner = PlayerInfo.FindMatches(p, message);
            if (partner == null) return;
            if (partner == p) { p.Message("You cannot marry yourself."); return; }

            entry = MarryPlugin.GetMarriedPlayer(partner.name);
            if (entry != null) {
                p.Message("{0} %Sis already married to someone else", partner.ColoredName); return;
            }

            //cooldown ------------------
            TimeSpan coolDown = GetCooldown(p);
            if (coolDown.TotalSeconds > 0) {
                coolDown += new TimeSpan(0, 0, 0, 1, 0);
                p.Message("You must wait {0} before proposing again.", coolDown.Shorten(true, false));
                return;
            }
            InitiateDelay(p, 1);
            //------------------------------


            Chat.MessageGlobal("-{0}%S gets down on one knee-",
                               p.ColoredName);
            Chat.MessageGlobal(string.Format("{0}%S is asking {1}%S for {2} hand in marriage!",
                               p.ColoredName, partner.ColoredName, partner.pronouns.Object));

            partner.Extras[MarryPlugin.ExtraName] = p.name;
            partner.Message("&bTo accept {0} proposal type &a/Accept", p.pronouns.Object);
            partner.Message("&bOr to deny it, type &c/Deny");
        }

        public override void Help(Player p) {
            p.Message("%T/Marry [player]");
            p.Message("%HProposes to the given player.");
        }
    }
}
