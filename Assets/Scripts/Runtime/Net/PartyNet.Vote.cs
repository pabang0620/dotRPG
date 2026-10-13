using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [LEAVE 2026-10-13] "다 함께 마을로": the host asks every person in the run; all must agree within
    /// <see cref="VillageVoteSeconds"/> (no answer = no), then the whole party fails the run and goes home together.
    /// </summary>
    public sealed partial class PartyNet
    {
        public const float VillageVoteSeconds = 10f;

        Action<bool> voteDone;
        readonly HashSet<int> votePending = new HashSet<int>();

        /// <summary>Host: people in the run besides the host (AI seats excluded).</summary>
        public int OtherHumans
        {
            get
            {
                int n = 0;
                foreach (var kv in bySlot) if (kv.Key < humans && kv.Key != mySlot && kv.Value != null) n++;
                return n;
            }
        }

        /// <summary>Host: asks the members; <paramref name="done"/> gets true when everyone agreed in time.</summary>
        public void HostAskVillage(Action<bool> done)
        {
            if (!host || voteDone != null) return;
            votePending.Clear();
            foreach (var kv in bySlot) if (kv.Key < humans && kv.Key != mySlot && kv.Value != null) votePending.Add(kv.Key);
            if (votePending.Count == 0) { done?.Invoke(true); return; }
            voteDone = done;
            Broadcast(NetChannel.Event, PartyWire.Build(w => { w.Write(PartyMsg.VillageVote); w.Write(VillageVoteSeconds); }));
            StartCoroutine(VoteDeadline());
        }

        IEnumerator VoteDeadline()
        {
            // A little longer than the members' own countdown: their answer is still on its way.
            float until = Time.unscaledTime + VillageVoteSeconds + 1.5f;
            while (voteDone != null && Time.unscaledTime < until) yield return null;
            EndVote(false);
        }

        void EndVote(bool agreed)
        {
            var done = voteDone;
            voteDone = null;
            votePending.Clear();
            done?.Invoke(agreed);
        }

        void HostVillageAnswer(int peer, System.IO.BinaryReader r)
        {
            int slot = r.ReadByte();
            bool yes = r.ReadBoolean();
            if (voteDone == null || !OwnsSlot(peer, slot) || !votePending.Contains(slot)) return;
            if (!yes) { EndVote(false); return; }
            votePending.Remove(slot);
            if (votePending.Count == 0) EndVote(true);
        }

        void MemberVillageVote(System.IO.BinaryReader r)
        {
            float seconds = Mathf.Clamp(r.ReadSingle(), 3f, 30f);
            Action<bool> answer = yes =>
            {
                if (Current != this) return;
                SendTo(HostPeer, NetChannel.Event, PartyWire.Build(w => { w.Write(PartyMsg.VillageAnswer); w.Write((byte)mySlot); w.Write(yes); }));
            };
            Game.UI.Confirm("방장이 다 함께 마을로 돌아가자고 합니다.\n동의하면 이번 판은 실패로 끝나고 모두 마을로 돌아갑니다.\n<color=#ffe066>{초}</color>초 안에 답하지 않으면 거절로 처리됩니다.",
                () => answer(true), defaultYes: false, onNo: () => answer(false), timeoutSeconds: seconds, overlay: true);
        }
    }
}
