using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RelicRun.Core.Combat;
using RelicRun.Core.Content;
using RelicRun.Core.Run;
using RelicRun.Tests.Support;

namespace RelicRun.Tests
{
    /// <summary>
    /// Phase 6 gate: <c>Tools/corpus/versus.json</c>.
    /// </summary>
    /// <remarks>
    /// Each recorded match is replayed from its seed and the lobby it was dealt. The recording
    /// supplies only the human's choices — the relic drafted, and what the bazaar was asked for
    /// — plus the lobby itself, which is handed over the way a pack is because making one spends
    /// most of its randomness on the rivals' appearance.
    ///
    /// Everything else has to come out of the port: which rival is met and how strong they are,
    /// where the bout is held, the duel, what a win or a loss costs, the bracket the unfought
    /// rivals resolve out of sight, the relic every survivor drafts, the pool growing each round,
    /// and the bazaar's prices.
    /// </remarks>
    [TestFixture]
    public class VersusRunTests
    {
        /// <summary>Stops a replay at its first divergence, so the failure is the first one.</summary>
        private sealed class ReplayEnded : Exception
        {
        }

        private sealed class Replay : IVersusChoices, IVersusObserver
        {
            private readonly string _id;
            private readonly JArray _rounds;
            private readonly JObject _bazaar;

            private int _round;
            private int _pick;

            public string Failure;

            public Replay(string id, JArray rounds, JObject bazaar)
            {
                _id = id;
                _rounds = rounds;
                _bazaar = bazaar;
            }

            public bool Exhausted { get { return _round >= _rounds.Count; } }

            private void Fail(string why)
            {
                if (Failure == null) Failure = _id + ": " + why;
                throw new ReplayEnded();
            }

            private JObject Round()
            {
                if (_round >= _rounds.Count) Fail("the recording ended, but the match went on");
                return (JObject)_rounds[_round];
            }

            // ---- the human's choices ----

            RelicId IVersusChoices.Draft(VersusMatch match, IReadOnlyList<RelicId> offer)
            {
                var picks = (JArray)Round()["picks"];
                if (_pick >= picks.Count)
                {
                    Fail("round " + Round()["round"] + ": more picks than were recorded");
                }

                var entry = (JObject)picks[_pick];
                CheckSet(entry["offer"], offer, "round " + Round()["round"] + " offer");

                RelicId pick;
                RelicCatalog.TryParse(entry["pick"].Value<string>(), out pick);
                return pick;
            }

            VersusDeal IVersusChoices.Bazaar(VersusMatch match, IReadOnlyList<RelicId> offer,
                IReadOnlyList<int> awakenable)
            {
                if (_bazaar == null) Fail("the bazaar opened, but the recording never reached one");

                CheckSet(_bazaar["offer"], offer, "bazaar offer");

                var wantSlots = new List<int>();
                foreach (JToken t in (JArray)_bazaar["awakenable"]) wantSlots.Add(t["slot"].Value<int>());
                if (string.Join(",", wantSlots) != string.Join(",", awakenable))
                {
                    Fail("bazaar: awakenable slots recorded [" + string.Join(",", wantSlots) +
                         "], replayed [" + string.Join(",", awakenable) + "]");
                }

                var action = (JObject)_bazaar["action"];
                string kind = action["kind"].Value<string>();

                if (kind == "awaken") return VersusDeal.Awaken(action["slot"].Value<int>());
                if (kind == "buy")
                {
                    RelicId id;
                    RelicCatalog.TryParse(action["relic"].Value<string>(), out id);
                    return VersusDeal.Buy(id);
                }

                return VersusDeal.Leave;
            }

            // ---- and the checks ----

            void IVersusObserver.Draft(VersusMatch match, IReadOnlyList<RelicId> offer, RelicId pick)
            {
                _pick++;
            }

            void IVersusObserver.Bazaar(VersusMatch match, IReadOnlyList<RelicId> offer,
                IReadOnlyList<int> awakenable, VersusDeal deal)
            {
                CheckHero((JObject)_bazaar["after"], match, "bazaar");
            }

            void IVersusObserver.Round(VersusMatch match, int foe, bool won)
            {
                JObject want = Round();
                string where = "round " + want["round"].Value<int>();

                if (want["round"].Value<int>() != match.Round)
                {
                    Fail(where + ": replayed round " + match.Round);
                }

                if (want["foe"].Value<int>() != foe)
                {
                    Fail(where + ": foe recorded " + want["foe"] + ", replayed " + foe);
                }

                if (want["won"].Value<bool>() != won)
                {
                    Fail(where + ": recorded " + (want["won"].Value<bool>() ? "a win" : "a loss") +
                         ", replayed the other");
                }

                CheckHero((JObject)want["hero"], match, where);
                CheckRoster((JArray)want["roster"], match, where);

                _round++;
                _pick = 0;
            }

            void IVersusObserver.End(VersusMatch match, RunEnding how) { }

            private void CheckSet(JToken recorded, IReadOnlyList<RelicId> got, string where)
            {
                var want = new List<string>();
                foreach (JToken t in (JArray)recorded) want.Add(t.Value<string>());

                var mine = new List<string>(got.Count);
                for (int i = 0; i < got.Count; i++) mine.Add(RelicCatalog.KeyOf(got[i]));

                want.Sort(StringComparer.Ordinal);
                var sorted = new List<string>(mine);
                sorted.Sort(StringComparer.Ordinal);

                if (string.Join(",", want) != string.Join(",", sorted))
                {
                    Fail(where + ": recorded [" + string.Join(",", want) + "], replayed [" +
                         string.Join(",", sorted) + "]");
                }
            }

            private void CheckHero(JObject want, VersusMatch match, string where)
            {
                // The loadout is compared FIRST. A hand that drifted shows up in every stat
                // that depends on it, and naming a stat instead of the hand sends the reader
                // looking in the wrong place.
                string diff =
                    Items(where, want, match) ??
                    Awake(where, want, match) ??
                    Int(where, want, "php", match.Php) ??
                    Int(where, want, "pmax", match.Pmax) ??
                    Int(where, want, "gold", match.Gold) ??
                    Int(where, want, "lives", match.Lives) ??
                    Int(where, want, "kills", match.Hero.Kills) ??
                    Int(where, want, "adrenaline", match.Hero.Adrenaline) ??
                    Int(where, want, "defB", match.Hero.DefBonus) ??
                    Int(where, want, "anvilB", match.Hero.AnvilBonus) ??
                    Int(where, want, "strikeTot", match.Hero.StrikeTotal);

                if (diff != null) Fail(diff);
            }

            private void CheckRoster(JArray want, VersusMatch match, string where)
            {
                if (want.Count != match.Roster.Count)
                {
                    Fail(where + ": roster recorded " + want.Count + " delvers, replayed " +
                         match.Roster.Count);
                }

                for (int i = 0; i < want.Count; i++)
                {
                    var w = (JObject)want[i];
                    Rival rival = match.Roster[i];
                    string who = where + " " + w["name"].Value<string>();

                    if (w["lives"].Value<int>() != rival.Lives)
                    {
                        Fail(who + ": lives recorded " + w["lives"] + ", replayed " + rival.Lives);
                    }

                    if (w["gold"].Value<int>() != rival.Gold)
                    {
                        Fail(who + ": gold recorded " + w["gold"] + ", replayed " + rival.Gold);
                    }

                    var relics = new List<string>();
                    foreach (JToken t in (JArray)w["relics"]) relics.Add(t.Value<string>());

                    var mine = new List<string>(rival.Relics.Count);
                    for (int k = 0; k < rival.Relics.Count; k++) mine.Add(RelicCatalog.KeyOf(rival.Relics[k]));

                    if (string.Join(",", relics) != string.Join(",", mine))
                    {
                        Fail(who + ": relics recorded [" + string.Join(",", relics) +
                             "], replayed [" + string.Join(",", mine) + "]");
                    }
                }
            }

            private static string Int(string where, JObject want, string key, int got)
            {
                int value = want[key].Value<int>();
                return value == got ? null : where + " " + key + ": recorded " + value + ", replayed " + got;
            }

            private static string Items(string where, JObject want, VersusMatch match)
            {
                var recorded = new List<string>();
                foreach (JToken t in (JArray)want["items"]) recorded.Add(t.Value<string>());

                var mine = new List<string>(match.Items.Count);
                for (int i = 0; i < match.Items.Count; i++) mine.Add(RelicCatalog.KeyOf(match.Items[i]));

                return string.Join(",", recorded) == string.Join(",", mine)
                    ? null
                    : where + " items: recorded [" + string.Join(",", recorded) + "], replayed [" +
                      string.Join(",", mine) + "]";
            }

            private static string Awake(string where, JObject want, VersusMatch match)
            {
                var recorded = new List<int>();
                foreach (JToken t in (JArray)want["awake"]) recorded.Add(t.Value<int>());

                var mine = new List<int>(match.AwakenedSlots);
                recorded.Sort();
                mine.Sort();

                return string.Join(",", recorded) == string.Join(",", mine)
                    ? null
                    : where + " awakened slots: recorded [" + string.Join(",", recorded) +
                      "], replayed [" + string.Join(",", mine) + "]";
            }
        }

        private static VersusLobby LobbyFrom(JObject token)
        {
            var start = (JObject)token["start"];
            var lobby = new VersusLobby
            {
                Hall = start["hall"].Value<int>(),
                HeroPool = ((JObject)start["hero"])["pmax"].Value<int>(),
                SetupDraws = token["setupDraws"].Value<int>(),
            };

            foreach (JToken t in (JArray)start["roster"])
            {
                var r = (JObject)t;
                var b = (JObject)r["base"];
                var rival = new Rival
                {
                    Name = r["name"].Value<string>(),
                    Level = r["lvl"].Value<int>(),
                    Lives = r["lives"].Value<int>(),
                    Hall = r["hall"].Value<int>(),
                    Gold = r["gold"].Value<int>(),
                    Base = new RivalBase
                    {
                        Hp = b["hp"].Value<int>(), Atk = b["atk"].Value<int>(),
                        Def = b["def"].Value<int>(), Spd = b["spd"].Value<int>(),
                        Lck = b["lck"].Value<int>(),
                    },
                };

                foreach (JToken relic in (JArray)r["relics"])
                {
                    RelicId id;
                    if (RelicCatalog.TryParse(relic.Value<string>(), out id)) rival.Relics.Add(id);
                }

                lobby.Rivals.Add(rival);
            }

            // The offer dealt before the first pick came with the lobby, so it is the first
            // recorded offer of the first round rather than something the match rolls.
            var rounds = (JArray)token["rounds"];
            var opening = (JArray)((JObject)((JArray)((JObject)rounds[0])["picks"])[0])["offer"];
            foreach (JToken t in opening)
            {
                RelicId id;
                if (RelicCatalog.TryParse(t.Value<string>(), out id)) lobby.OpeningOffer.Add(id);
            }

            return lobby;
        }

        /// <summary>
        /// Every recorded lobby is MADE from its seed and the hero's level, rather than read
        /// back off the recording.
        /// </summary>
        /// <remarks>
        /// The lobby used to be handed over the way a pack is, because making one spends most
        /// of its randomness on the rivals' faces and the port had no wardrobe. It still has
        /// none — but nothing reads a face back, so the draws can be spent without choosing
        /// anything, and everything that IS read back comes out of the port: seven levels within
        /// one of the hero's, an opening kit of three apiece, a hall each, a statline each, and
        /// the offer the hero opens on.
        ///
        /// The draw count is compared too. It is the one number that notices a wardrobe slot or
        /// a colour family miscounted in a way the rosters happen to survive.
        /// </remarks>
        [Test]
        public void RecordedLobbiesAreMadeExactly()
        {
            JArray cases = Corpus.Array("versus.json");
            Assert.That(cases.Count, Is.GreaterThan(0), "versus corpus is empty");

            var failures = new List<string>();

            foreach (JToken token in cases)
            {
                var match = (JObject)token;
                string id = match["id"].Value<string>();
                var start = (JObject)match["start"];

                VersusLobby made = VersusRun.Make(
                    match["seed"].Value<uint>(), match["level"].Value<int>(),
                    RunRules.AsRecorded());

                string diff =
                    Same(id, "setup draws", match["setupDraws"].Value<int>(), made.SetupDraws) ??
                    Same(id, "hall", start["hall"].Value<int>(), made.Hall) ??
                    Same(id, "hero pool", ((JObject)start["hero"])["pmax"].Value<int>(), made.HeroPool) ??
                    Same(id, "roster size", ((JArray)start["roster"]).Count, made.Rivals.Count) ??
                    Roster(id, (JArray)start["roster"], made.Rivals) ??
                    Offer(id, LobbyFrom(match).OpeningOffer, made.OpeningOffer);

                if (diff != null) failures.Add(diff);
            }

            Assert.That(failures, Is.Empty,
                failures.Count + " of " + cases.Count + " lobbies diverge:\n  " +
                string.Join("\n  ", failures.GetRange(0, System.Math.Min(8, failures.Count))));
        }

        private static string Same(string id, string what, int recorded, int made)
        {
            return recorded == made
                ? null
                : id + ": " + what + " recorded " + recorded + ", made " + made;
        }

        private static string Roster(string id, JArray recorded, IReadOnlyList<Rival> made)
        {
            for (int i = 0; i < made.Count; i++)
            {
                var r = (JObject)recorded[i];
                var b = (JObject)r["base"];
                Rival mine = made[i];

                string diff =
                    Same(id + " rival " + i, "name", r["name"].Value<string>(), mine.Name) ??
                    Same(id + " " + mine.Name, "level", r["lvl"].Value<int>(), mine.Level) ??
                    Same(id + " " + mine.Name, "lives", r["lives"].Value<int>(), mine.Lives) ??
                    Same(id + " " + mine.Name, "hall", r["hall"].Value<int>(), mine.Hall) ??
                    Same(id + " " + mine.Name, "gold", r["gold"].Value<int>(), mine.Gold) ??
                    Same(id + " " + mine.Name, "hp", b["hp"].Value<int>(), mine.Base.Hp) ??
                    Same(id + " " + mine.Name, "atk", b["atk"].Value<int>(), mine.Base.Atk) ??
                    Same(id + " " + mine.Name, "def", b["def"].Value<int>(), mine.Base.Def) ??
                    Same(id + " " + mine.Name, "spd", b["spd"].Value<int>(), mine.Base.Spd) ??
                    Same(id + " " + mine.Name, "lck", b["lck"].Value<int>(), mine.Base.Lck) ??
                    Kit(id + " " + mine.Name, (JArray)r["relics"], mine.Relics);

                if (diff != null) return diff;
            }

            return null;
        }

        private static string Same(string where, string what, string recorded, string made)
        {
            return recorded == made
                ? null
                : where + ": " + what + " recorded " + recorded + ", made " + made;
        }

        /// <summary>An opening kit is ORDERED — it is the order the two-at-a-time draw kept.</summary>
        private static string Kit(string where, JArray recorded, IReadOnlyList<RelicId> made)
        {
            var want = new List<string>();
            foreach (JToken t in recorded) want.Add(t.Value<string>());

            var got = new List<string>(made.Count);
            for (int i = 0; i < made.Count; i++) got.Add(RelicCatalog.KeyOf(made[i]));

            return string.Join(",", want) == string.Join(",", got)
                ? null
                : where + ": kit recorded [" + string.Join(",", want) + "], made [" +
                  string.Join(",", got) + "]";
        }

        private static string Offer(string id, IReadOnlyList<RelicId> recorded,
            IReadOnlyList<RelicId> made)
        {
            var want = new List<string>(recorded.Count);
            for (int i = 0; i < recorded.Count; i++) want.Add(RelicCatalog.KeyOf(recorded[i]));

            var got = new List<string>(made.Count);
            for (int i = 0; i < made.Count; i++) got.Add(RelicCatalog.KeyOf(made[i]));

            want.Sort(System.StringComparer.Ordinal);
            got.Sort(System.StringComparer.Ordinal);

            return string.Join(",", want) == string.Join(",", got)
                ? null
                : id + ": opening offer recorded [" + string.Join(",", want) + "], made [" +
                  string.Join(",", got) + "]";
        }

        [Test]
        public void RecordedMatchesReplayExactly()
        {
            JArray cases = Corpus.Array("versus.json");
            Assert.That(cases.Count, Is.GreaterThan(0), "versus corpus is empty");

            var failures = new List<string>();

            foreach (JToken token in cases)
            {
                var match = (JObject)token;
                string id = match["id"].Value<string>();
                // A match that ended before round five never reached a bazaar.
                var replay = new Replay(id, (JArray)match["rounds"], match["bazaar"] as JObject);

                try
                {
                    // The recording is the SOURCE's match, so its duels are fought under the
                    // source's rules. Where the port deliberately differs, the difference is
                    // CombatRules and RunRules and nothing else.
                    VersusRun.Resolve(match["seed"].Value<uint>(), LobbyFrom(match), replay, replay,
                        CombatRules.DuelAsRecorded(), RunRules.AsRecorded());
                }
                catch (ReplayEnded)
                {
                    // The first divergence is recorded; nothing after it means anything.
                }

                if (replay.Failure != null) failures.Add(replay.Failure);
                else if (!replay.Exhausted) failures.Add(id + ": the match ended early");
            }

            Assert.That(failures, Is.Empty,
                failures.Count + " of " + cases.Count + " matches diverge:\n  " +
                string.Join("\n  ", failures.GetRange(0, Math.Min(8, failures.Count))));
        }
    }
}
