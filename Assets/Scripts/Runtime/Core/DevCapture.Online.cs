using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [ONLINE] <c>-dotrpgOnline &lt;folder&gt;</c>: client online-structure checks without any server.
    /// NetCommand round trip, LocalAuthority = old random calls, a loopback "second client" driving a party
    /// member through NetworkInput, and the 파티 찾기 / 경매장 preview windows with mock services.
    /// Report lines "ONLINE &lt;what&gt; PASS|FAIL" and "ONLINE summary: N passed, M failed".
    /// </summary>
    public partial class DevCapture
    {
        bool onlineOnly;
        int onlinePassed, onlineFailed;

        void OCheck(string what, bool ok)
        {
            if (ok) onlinePassed++;
            else onlineFailed++;
            log?.WriteLine($"ONLINE {what} {(ok ? "PASS" : "FAIL")}");
        }

        IEnumerator OnlineRun()
        {
            onlinePassed = onlineFailed = 0;
            bool autosave = Game.Config.autosave;
            Game.Config.autosave = false;
            try { OnlineCodecChecks(); OnlineAuthorityChecks(); }
            catch (Exception e) { OCheck("pure checks threw " + e.GetType().Name + ": " + e.Message, false); }

            yield return Wait(1.5f);
            Game.Flow.NewGame(CharacterClass.Warrior);
            yield return Wait(1.6f);
            yield return OnlineLoopback();
            yield return OnlinePartyFinder();
            yield return OnlineAuction();
            yield return OnlineChat();

            Game.Config.autosave = autosave;
            log.WriteLine($"ONLINE summary: {onlinePassed} passed, {onlineFailed} failed");
        }

        // ---------- NetCommand ----------
        void OnlineCodecChecks()
        {
            var cases = new[]
            {
                new ActorCommand { move = new Vector2(1f, 0f), aim = new Vector2(1f, 0f), attack = true, skillSlot = -1 },
                new ActorCommand { move = new Vector2(-0.6f, 0.8f), aim = new Vector2(0.3f, -0.95f), faceAim = true, skillSlot = 3, useMana = true },
                new ActorCommand { move = Vector2.zero, aim = Vector2.zero, interact = true, useHealing = true, townScroll = true, skillSlot = 0 },
                new ActorCommand { move = new Vector2(0.7071f, -0.7071f), aim = new Vector2(-1f, 0f), skillSlot = 4, attack = true, faceAim = true },
            };
            bool ok = true;
            float worstMove = 0f, worstAim = 0f;
            var sb = new StringBuilder();
            for (int i = 0; i < cases.Length; i++)
            {
                var c = cases[i];
                uint tick = 1000u + (uint)i * 70000u;
                var bytes = NetCommand.Pack(c, tick).ToBytes();
                var back = NetCommand.Read(bytes, 0);
                var u = back.Unpack();
                float dm = (u.move - Vector2.ClampMagnitude(c.move, 1f)).magnitude;
                float da = c.aim.sqrMagnitude > 0 ? Vector2.Angle(u.aim, c.aim) : u.aim.magnitude * 999f;
                worstMove = Mathf.Max(worstMove, dm);
                worstAim = Mathf.Max(worstAim, da);
                bool same = back.tick == tick && u.attack == c.attack && u.interact == c.interact && u.useHealing == c.useHealing && u.useMana == c.useMana
                            && u.townScroll == c.townScroll && u.faceAim == c.faceAim && u.skillSlot == c.skillSlot && dm < 0.012f && da < 1.5f;
                ok &= same && bytes.Length == NetCommand.Size;
                sb.Append(same ? "1" : "0");
            }
            OCheck($"ActorCommand round trip: {cases.Length} cases [{sb}] size={NetCommand.Size}B worstMove={worstMove:0.0000} worstAim={worstAim:0.00}deg", ok);

            var input = new NetworkInput();
            input.Enqueue(NetCommand.Pack(new ActorCommand { move = Vector2.right, skillSlot = -1 }, 5));
            input.Enqueue(NetCommand.Pack(new ActorCommand { move = Vector2.up, skillSlot = -1 }, 3));
            input.Enqueue(NetCommand.Pack(new ActorCommand { move = Vector2.left, skillSlot = -1 }, 5)); // duplicate tick replaces
            var first = input.Read(null);
            var second = input.Read(null);
            input.Enqueue(NetCommand.Pack(new ActorCommand { move = Vector2.down, skillSlot = -1 }, 4)); // late
            OCheck($"NetworkInput tick order: first={first.move} second={second.move} lateDropped={input.Buffered == 0} lastTick={input.LastTick}",
                first.move.y > 0.9f && second.move.x < -0.9f && input.Buffered == 0 && input.LastTick == 5);

            var (a, b) = LoopbackTransport.CreatePair(2);
            b.Send(0, NetChannel.Input, new byte[] { 7 });
            bool early = a.TryReceive(out _);
            a.Tick(); a.Tick();
            bool late = a.TryReceive(out var pkt);
            OCheck($"LoopbackTransport latency 2 ticks: early={early} delivered={late} from={pkt.from} data={(pkt.data != null ? pkt.data[0] : -1)}", !early && late && pkt.from == 1 && pkt.data[0] == 7);
        }

        // ---------- IAuthority ----------
        void OnlineAuthorityChecks()
        {
            var auth = new LocalAuthority();
            const int seed = 4242;
            UnityEngine.Random.InitState(seed);
            var legacy = Enumerable.Range(0, 200).Select(_ => UnityEngine.Random.Range(0, 100)).ToList();
            UnityEngine.Random.InitState(seed);
            var routed = Enumerable.Range(0, 200).Select(_ => auth.EnhanceRoll()).ToList();
            OCheck($"enhance rolls identical (seed {seed}, 200 rolls): {legacy.SequenceEqual(routed)} first=[{string.Join(",", routed.Take(6))}]", legacy.SequenceEqual(routed));

            // Same chain of calls as EnemyController.DropLoot before and after routing.
            string Legacy(CharacterClass cls)
            {
                var sb = new StringBuilder();
                sb.Append(UnityEngine.Random.Range(8, 17)).Append('|');
                foreach (var mat in EquipmentDatabase.AllMaterials)
                {
                    if (UnityEngine.Random.value > mat.dropChance) continue;
                    sb.Append(mat.id).Append('x').Append(UnityEngine.Random.Range(mat.minDrop, mat.maxDrop + 1)).Append('|');
                }
                return sb.Append(EquipmentDatabase.RollDrop(cls, 0.4f) ?? "-").ToString();
            }
            string Routed(CharacterClass cls)
            {
                var sb = new StringBuilder();
                sb.Append(auth.DropGold(8, 17)).Append('|');
                foreach (var mat in EquipmentDatabase.AllMaterials)
                {
                    if (!auth.DropChance(mat.dropChance)) continue;
                    sb.Append(mat.id).Append('x').Append(auth.DropCount(mat.minDrop, mat.maxDrop)).Append('|');
                }
                return sb.Append(auth.DropEquipment(cls, 0.4f) ?? "-").ToString();
            }
            UnityEngine.Random.InitState(seed);
            var l = Enumerable.Range(0, 300).Select(i => Legacy(i % 2 == 0 ? CharacterClass.Warrior : CharacterClass.Mage)).ToList();
            UnityEngine.Random.InitState(seed);
            var r = Enumerable.Range(0, 300).Select(i => Routed(i % 2 == 0 ? CharacterClass.Warrior : CharacterClass.Mage)).ToList();
            int gear = r.Count(s => !s.EndsWith("-"));
            OCheck($"monster drops identical (seed {seed}, 300 kills): {l.SequenceEqual(r)} gearDrops={gear} sample='{r[0]}'", l.SequenceEqual(r) && gear > 0);
            OCheck($"Authority.Current is LocalAuthority: {Authority.Current is LocalAuthority} remote={Authority.Current.IsRemote} dungeon={Authority.Current.Dungeon == DungeonAuthority.Current} xp={Authority.Current.GrantXp("t", 37)} gold={Authority.Current.ApplyGold("t", -12)}",
                Authority.Current is LocalAuthority && !Authority.Current.IsRemote && Authority.Current.Dungeon == DungeonAuthority.Current && Authority.Current.GrantXp("t", 37) == 37 && Authority.Current.ApplyGold("t", -12) == -12);
            OCheck($"OnlineSession offline: connect={OnlineSession.TryConnect(out var err)} current={(OnlineSession.Current == null ? "null" : "set")} ({err})", OnlineSession.Current == null);
            UnityEngine.Random.InitState(Environment.TickCount);
        }

        // ---------- Loopback-driven party member ----------
        IEnumerator OnlineLoopback()
        {
            var party = Game.Party;
            var local = Game.Player;
            Game.Session.Progression.AddXp(1200);
            var bron = party.AddCompanion("merc_bron");
            Game.Flow.TravelTo(MapRegistry.Forest);
            yield return Wait(2.6f);
            bron = party.Find("merc_bron");
            if (bron == null) { OCheck("loopback: companion missing after travel", false); yield break; }
            // Clear the field so only the staged skeleton matters.
            foreach (var e in AliveEnemies()) MoveEnemy(e, local.Position + new Vector2(40f, 40f));

            var (host, client) = LoopbackTransport.CreatePair(1);
            var router = new NetCommandRouter(host);
            var net = new NetworkInput();
            int slot = ActorIds.SlotOf(bron);
            var oldInput = bron.Input;
            bron.Input = net;
            router.Register(slot, net);
            var id = ActorIds.Of(bron);
            OCheck($"ActorId: local={ActorIds.Of(local)} bron={id} slot={slot} find={(ActorIds.Find(id) == bron)}", ActorIds.Of(local).Equals(ActorId.LocalOffline) && id.IsAi && slot == 1 && ActorIds.Find(id) == bron);

            uint tick = 1;
            Vector2 start = bron.Position;
            float t = 0f;
            while (t < 1.4f)
            {
                NetCommandRouter.SendCommand(client, 0, slot, new ActorCommand { move = Vector2.right, aim = Vector2.right, skillSlot = -1 }, tick++);
                client.Tick(); host.Tick(); router.Pump();
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            for (int i = 0; i < 10; i++) { client.Tick(); host.Tick(); router.Pump(); yield return null; }
            Vector2 moved = bron.Position - start;
            OCheck($"loopback moves member: dx={moved.x:0.00} dy={moved.y:0.00} sent={client.Sent} routed={router.Routed} applied={net.Applied} localStill={(local.Position - start).magnitude:0.0}",
                moved.x > 1.2f && Mathf.Abs(moved.y) < 0.8f && router.Routed > 10 && net.Applied > 10);

            yield return Wait(0.4f);
            Vector2 spot = bron.Position + new Vector2(1.1f, 0f);
            var skel = EnemyController.Create(Game.Config.skeletonStats, CharacterLook.Skeleton, spot, Game.World.ObjectsRoot);
            int dmgBefore = party.DamageOf(bron);
            t = 0f;
            int presses = 0;
            while (t < 2.5f)
            {
                bool press = (tick % 6) == 0;
                if (press) presses++;
                Vector2 dir = skel != null && !skel.IsDead ? (skel.Position - bron.Position).normalized : Vector2.right;
                NetCommandRouter.SendCommand(client, 0, slot, new ActorCommand { move = Vector2.zero, aim = dir, faceAim = true, attack = press, skillSlot = -1 }, tick++);
                client.Tick(); host.Tick(); router.Pump();
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            int dealt = party.DamageOf(bron) - dmgBefore;
            yield return Shot("online_01_loopback_member");
            OCheck($"loopback attacks: presses={presses} damageDealt={dealt} skeletonDead={(skel == null || skel.IsDead)}", dealt > 0);
            bron.Input = oldInput;
            party.RemoveCompanion("merc_bron");
            yield return Wait(0.3f);
        }

        // ---------- 파티 찾기 ----------
        IEnumerator OnlinePartyFinder()
        {
            var w = PartyFinderScreen.Instance;
            Game.Flow.OpenWindow(w);
            yield return Wait(0.6f);
            OCheck($"party finder open: state={Game.State.Current} active={w != null && w.gameObject.activeInHierarchy} rows={w?.DevVisibleRows} row0='{w?.DevRowText(0)}'",
                Game.State.Current == GameState.Inventory && w != null && w.gameObject.activeInHierarchy && w.DevVisibleRows >= 5);
            w.DevApplyFirstOpen();
            yield return Wait(0.2f);
            string applied = w.DevStatus;
            OCheck($"party finder apply: '{applied}'", applied.Contains("참가 신청했습니다"));
            w.DevPost();
            yield return Wait(0.2f);
            OCheck($"party finder post on top: row0='{w.DevRowText(0)}'", w.DevRowText(0).Contains("★") && w.DevRowText(0).Contains("내 모집 글"));
            w.DevToggleQueue();
            yield return Wait(1.3f);
            string qt = w.DevQueueText;
            OCheck($"auto-match queue running: '{qt}' active={OnlineServices.PartyFinder.Queue.active}", OnlineServices.PartyFinder.Queue.active && qt.Contains("매칭 중"));
            yield return Shot("online_02_party_finder");
            OnlineServices.PartyFinder.Tick(20f);
            int humans = OnlineServices.PartyFinder.Queue.humans;
            // [F4] Departure enters the queued dungeon. Saturday: every weekday dungeon is open.
            var saturday = new DateTime(2026, 10, 3, 12, 0, 0);
            ResetClock.NowOverride = () => saturday;
            string queued = OnlineServices.PartyFinder.Queue.dungeonId;
            w.DevDepart();
            yield return Wait(2.5f);
            OCheck($"AI로 채워 출발 → 입장: humans={humans} status='{w.DevStatus}' active={OnlineServices.PartyFinder.Queue.active} inRun={Game.Dungeon.InRun} map={Game.Session.MapId} queued={queued} state={Game.State.Current}",
                !OnlineServices.PartyFinder.Queue.active && w.DevStatus.Contains("AI 용병") && humans == 2 && Game.Dungeon.InRun && Game.State.Current == GameState.Playing);
            yield return Shot("online_02b_matched_dungeon");
            Game.Dungeon.ExitToVillage(false);
            yield return Wait(2.5f);
            OnlineServices.PartyFinder.StartQueue(DungeonDatabase.Weekday[0].id, DungeonDifficulty.Normal);
            OnlineServices.PartyFinder.Tick(PartyFinderRules.QueueSeconds + 0.5f);
            yield return Wait(2.5f);
            OCheck($"queue times out after {PartyFinderRules.QueueSeconds}s and departs: active={OnlineServices.PartyFinder.Queue.active} inRun={Game.Dungeon.InRun}",
                !OnlineServices.PartyFinder.Queue.active && Game.Dungeon.InRun);
            Game.Dungeon.ExitToVillage(false);
            yield return Wait(2.5f);
            ResetClock.NowOverride = null;
            OCheck($"back in the village: map={Game.Session.MapId} inRun={Game.Dungeon.InRun}", Game.Session.MapId == MapRegistry.Village && !Game.Dungeon.InRun);
        }

        // ---------- [F5] 채팅 · 빠른 신호 · 친구/차단/신고 ----------
        IEnumerator OnlineChat()
        {
            var mock = OnlineServices.Chat as MockChatService;
            var view = ChatView.Instance;
            if (view == null || mock == null) { OCheck($"chat missing: view={view != null} mock={mock != null}", false); yield break; }
            foreach (var n in mock.Blocked.ToList()) mock.Unblock(n);
            mock.Tick(ChatRules.MuteSeconds + 1f);

            string first = mock.Send(ChatChannel.General, "안녕하세요");
            string tooFast = mock.Send(ChatChannel.General, "또 안녕");
            OCheck($"chat rate limit: first={first ?? "sent"} second='{tooFast}'", first == null && tooFast != null && tooFast.Contains("1초"));
            mock.Tick(1.1f); mock.Send(ChatChannel.General, "도배");
            mock.Tick(1.1f); mock.Send(ChatChannel.General, "도배");
            mock.Tick(1.1f); mock.Send(ChatChannel.General, "도배");
            mock.Tick(1.1f);
            string muted = mock.Send(ChatChannel.General, "이제 됨?");
            OCheck($"same line 3x mutes 10s: '{muted}'", muted != null && muted.Contains("막혔습니다"));
            mock.Tick(ChatRules.MuteSeconds);
            mock.Send(ChatChannel.Party, "이 시발 보스");
            var last = mock.Lines[mock.Lines.Count - 1];
            OCheck($"word filter preview: '{last.text}' channel={last.channel}", last.text.Contains("**") && !last.text.Contains("시발") && last.channel == ChatChannel.Party);

            // Typing must not swing the sword: open the input line, check the input gate.
            view.Open(null);
            yield return null;
            yield return null;
            bool gate = InputReader.TextInputActive && view.Typing;
            yield return Shot("online_08_chat_typing");
            view.Close();
            yield return null;
            OCheck($"chat input blocks game keys while open: gate={gate} closed={!view.Typing && !InputReader.TextInputActive}", gate && !view.Typing && !InputReader.TextInputActive);

            mock.Tick(1.1f);
            bool signal = view.QuickSignal(3);
            yield return Wait(0.3f);
            last = mock.Lines[mock.Lines.Count - 1];
            OCheck($"quick signal 4~8: sent={signal} line='{last.text}' channel={last.channel}", signal && last.text == ChatRules.QuickSignals[3] && last.channel == ChatChannel.Party);
            yield return Shot("online_09_quick_signal");

            mock.Tick(60f); // generated village voices
            string speaker = mock.RecentSpeakers().FirstOrDefault(n => n != Game.Session.Journal.PlayerName);
            OCheck($"village chatter arrives: speaker={speaker} lines={mock.Lines.Count}", !string.IsNullOrEmpty(speaker));
            mock.Block(speaker);
            int before = mock.Lines.Count;
            for (int i = 0; i < 12; i++) mock.Tick(60f);
            bool leaked = mock.Lines.Skip(before).Any(l => l.from == speaker);
            OCheck($"blocked player is silenced: blocked={mock.IsBlocked(speaker)} leaked={leaked} whisper='{mock.Send(ChatChannel.Whisper, "hi", speaker)}'", mock.IsBlocked(speaker) && !leaked);
            string report = mock.Report(speaker, ChatRules.ReportReasons[1]);
            OCheck($"report attaches the last {ChatRules.ReportLines} lines: n={mock.Reports.Count} lines={mock.Reports[0].log.Count} '{report}'",
                mock.Reports.Count == 1 && mock.Reports[0].log.Count == Math.Min(ChatRules.ReportLines, mock.Lines.Count));

            GameEvents.RaiseToast("<color=#ffd84a>[알림]</color> 아린님이 뼈손잡이 장검 강화에 성공했습니다!");
            last = mock.Lines[mock.Lines.Count - 1];
            OCheck($"+10 notice reaches the system channel: '{last.text}'", last.channel == ChatChannel.System && last.text.StartsWith("아린님이"));

            // Friends window.
            var social = SocialScreen.Instance;
            Game.Flow.OpenWindow(social);
            yield return Wait(0.5f);
            yield return Shot("online_10_friends");
            social.DevOpenPerson("검은뿔");
            yield return Wait(0.3f);
            yield return Shot("online_11_friend_actions");
            OCheck($"friends window: open={social.gameObject.activeInHierarchy} title='{social.DevTitle}' friends={mock.Friends.Count}",
                social.gameObject.activeInHierarchy && social.DevTitle == "검은뿔");
            mock.Unblock(speaker); // keep the settings file clean for the next run
            Game.Flow.CloseInventory();
            yield return Wait(0.4f);
        }

        // ---------- 경매장 ----------
        IEnumerator OnlineAuction()
        {
            var bag = Game.Session.Inventory;
            bag.Add(ConsumableDatabase.Gold, 60000);
            bag.Add("eq_sword_10_u+7", 1);
            bag.Add(ConsumableDatabase.ProtectTicket, 1);
            var w = AuctionScreen.Instance;
            Game.Flow.OpenWindow(w);
            yield return Wait(0.6f);
            OCheck($"auction open: active={w != null && w.gameObject.activeInHierarchy} rows={w?.DevVisibleRows} row0='{w?.DevRowName(0)}'",
                w != null && w.gameObject.activeInHierarchy && w.DevVisibleRows == 9);
            yield return Shot("online_03_auction_search");

            var svc = OnlineServices.Auction;
            var listing = w.DevListing(0);
            int goldBefore = Game.Session.Gold;
            string key = listing.itemKey;
            int itemBefore = bag.Count(key);
            w.DevBuyRow(0);
            yield return Wait(0.5f);
            var confirm = Game.UI.ConfirmDialog;
            bool confirmShown = confirm != null && confirm.gameObject.activeInHierarchy && w.gameObject.activeInHierarchy;
            yield return Shot("online_04_auction_confirm");
            confirm.DevAnswer(true);
            yield return Wait(0.4f);
            int goldAfter = Game.Session.Gold;
            int mail = svc.UnclaimedMail;
            OCheck($"buyout: confirm overlay={confirmShown} price={listing.buyout} gold {goldBefore}->{goldAfter} mail={mail} gone={!svc.Search(new AuctionQuery()).Any(l => l.id == listing.id)}",
                confirmShown && goldBefore - goldAfter == listing.buyout && mail == 1 && !svc.Search(new AuctionQuery()).Any(l => l.id == listing.id));
            var claim = svc.ClaimAll();
            OCheck($"mail claim: {claim.message} item {key} {itemBefore}->{bag.Count(key)} mail={svc.UnclaimedMail}", claim.ok && bag.Count(key) == itemBefore + listing.count && svc.UnclaimedMail == 0);

            // Register from the bag.
            long price = AuctionRules.BasePrice("eq_sword_10_u+7") * 2 / 10 * 10;
            w.DevSelectRegister("eq_sword_10_u+7", price, 2);
            yield return Wait(0.4f);
            yield return Shot("online_05_auction_register");
            int g0 = Game.Session.Gold;
            w.DevSubmitRegister();
            yield return Wait(0.3f);
            var mine = svc.MyListings();
            long deposit = AuctionRules.Deposit(price);
            OCheck($"register: status='{w.DevStatus}' mine={mine.Count} key={mine.FirstOrDefault()?.itemKey} deposit={g0 - Game.Session.Gold}/{deposit} bag={bag.Count("eq_sword_10_u+7")}",
                mine.Count == 1 && mine[0].itemKey == "eq_sword_10_u+7" && g0 - Game.Session.Gold == deposit && bag.Count("eq_sword_10_u+7") == 0);
            var bound = svc.Register(ConsumableDatabase.ProtectTicket, 1, 500, 0, 24);
            OCheck($"protection ticket bound: ok={bound.ok} '{bound.message}' bind={AuctionRules.BindOf(ConsumableDatabase.ProtectTicket)} enhancedSword={AuctionRules.BindOf("eq_sword_10_u+7")}",
                !bound.ok && AuctionRules.BindOf(ConsumableDatabase.ProtectTicket) == ItemBind.CharacterBound);
            var cheap = svc.Register("eq_sword_10_u", 1, 1, 0, 24);
            OCheck($"price floor enforced: ok={cheap.ok} '{cheap.message}'", !cheap.ok || bag.Count("eq_sword_10_u") == 0);
            w.DevTab(1);
            yield return Wait(0.4f);
            OCheck($"내 등록 tab: rows={w.DevVisibleRows} row0='{w.DevRowName(0)}'", w.DevVisibleRows == 1 && w.DevRowName(0).Contains("+7"));
            yield return Shot("online_06_auction_mine");
            // Seller settlement: price − fee + deposit arrives by mail.
            var mock = svc as MockAuctionService;
            bool sold = mock != null && mock.DevSellMine(mine[0].id);
            long expected = AuctionRules.Payout("eq_sword_10_u+7", price) + deposit;
            int g1 = Game.Session.Gold;
            w.DevTab(3);
            yield return Wait(0.4f);
            yield return Shot("online_07_auction_mail");
            svc.ClaimAll();
            OCheck($"sale settlement: sold={sold} fee={AuctionRules.Fee("eq_sword_10_u+7", price)} received={Game.Session.Gold - g1} expected={expected}", sold && Game.Session.Gold - g1 == expected);
            w.DevTab(0);
            w.DevSearch("철");
            yield return Wait(0.3f);
            OCheck($"search '철': rows={w.DevVisibleRows} row0='{w.DevRowName(0)}'", w.DevVisibleRows > 0 && Enumerable.Range(0, w.DevVisibleRows).All(i => w.DevRowName(i).Contains("철")));
            Game.Flow.CloseInventory();
            yield return Wait(0.4f);
        }
    }
}
