using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using EvilAliens;
using Microsoft.Xna.Framework;

namespace EvilAliensWeb.Compat.Net
{
    // eaNetJipDump() / `eval NetJipDump` -- ONE dump of this peer's replicated world, in a form
    // the two ends of a live session can be DIFFED against each other (card 054947f3).
    //
    // WHAT IT IS FOR. The full join-in-progress pass was a manual two-window Chrome run, and
    // everything automated covered one leg each (netbg_catchup the scenery, eaSlotTest the grant,
    // eaNetPickup leg 6 the option catch-up, eaNetSnap the self-heal). Nothing asserted the whole
    // attach: joiner arrives mid-level -> EvLaunch mirror -> EvReady -> ReplayLive + the latched
    // catch-up beats -> A WORLD THAT MATCHES THE HOST. That last claim is a diff, and a diff
    // needs both worlds -- which needs two PROCESSES, since one holds one `Game.Components`
    // (see the "TWO PEERS WITH INDEPENDENT WORLDS IN ONE PROCESS IS UNREACHABLE" bullet).
    // `python tools/sim/net_jip_sync.py` is the driver; this is what it reads off both ends.
    //
    // WHY IT IS GENERIC RATHER THAN A LIST OF PER-DIMENSION LEGS. Every replicated field of every
    // type is, by construction, either in `NetBaseState` (position, rotation, scale, frame, hp --
    // the explicit keys below) or in that type's descriptor extras block. So the dump RE-ENCODES
    // each entity's spawn and state extras through its OWN descriptor and prints the bytes, with
    // no per-type code here and none needed when a descriptor grows a field. That is the
    // "iterate over all objects and check their key values match" the card asks for, taken
    // literally.
    //
    // WHAT A RE-ENCODE CAN AND CANNOT SAY, because it is the one place this design has a real
    // limit and the differ leans on knowing it. **A spawn-extra block is NOT a constant.** It is
    // whatever the descriptor reads off the entity NOW, and at least two shipped ones read live
    // state there: `FlyingSpiderDescriptor`'s anchor carries the swivel PHASE, which drifts by
    // design, and `UfoDescriptor`'s flags carry `hasbonus`, which turns off in play. So two ends
    // legitimately re-encode different spawn bytes for an entity that was built correctly, and
    // comparing them byte-for-byte would cry wolf on every wasp. The dimension those bytes were
    // meant to cover -- "was this puppet built from the host's extras, or is it card de4d5d65's
    // provisional shape?" -- is therefore reported DIRECTLY as `prov=`, off the puppet layer's
    // own SelfHealed flag, which is exact and does not depend on any descriptor round-tripping.
    // The bytes stay in the dump for their LENGTH (a structural mismatch is still a real fault)
    // and for reading by hand.
    //
    // WHAT IT DELIBERATELY DOES NOT CARRY, each because something else already pins it:
    //   * the WALL's collision tile size -- derived FROM the wall since card 4392bd30, so an
    //     agreeing scale IS an agreeing tile; the derivation is `eaNetWalls`' subject.
    //   * the SHIP puppets' motion -- ships are not NetId entities and their lane is pinned by
    //     `eaNetFire` / `eaNetMotion` / `eaNetResetSpawn`. Per-slot HUD state IS carried (it is a
    //     replicated dimension of this card), the ship's own interpolated position is not.
    //   * the background APPLY path -- `netbg_catchup.txt` round-trips it. What is carried is the
    //     scene's own state LINE, so the two ends can be shown to agree about scenery/music/
    //     doodads/cosmetic swarms; a mismatch there points at that probe, it does not duplicate it.
    //
    // KEYS A PEER IS ENTITLED TO DISAGREE ABOUT are not hidden -- they are LABELLED, off the
    // entity's own declared seams (`NetFrameLocal`, `NetSpinPerMs`, `NetScaleLocal`,
    // `NetPathAnchored`/`NetPathOffset`), so the differ skips a key because the GAME says that
    // key is locally simulated, never because a type name is on a list here.
    internal static class NetJipDump
    {
        // Bumped when a line's SHAPE changes, so the python differ can refuse a dump it does not
        // understand instead of silently comparing half a world.
        internal const int FormatVersion = 5;

        // Descriptor extras are small (the widest shipped block is a handful of bytes); this is
        // an order of magnitude over any of them and is asserted rather than assumed below.
        private const int ExtraScratchBytes = 256;

        public static string Run()
        {
            var sb = new StringBuilder();
            string role = !NetSession.Active ? "none" : (NetSession.IsClient ? "client" : "host");
            int ids = 0;

            try
            {
                ids = AppendEntities(sb, role);
                AppendExtras(sb);
            }
            catch (Exception ex)
            {
                sb.Append("[netjip] err ").Append(ex.GetType().Name).Append(": ")
                    .Append(ex.Message).Append('\n');
            }

            // LAST, not first: the driver waits for it as the end-of-dump sentinel, so a dump cut
            // short by an exception cannot be mistaken for a complete one with fewer entities.
            sb.Append("[netjip] dump v").Append(FormatVersion)
                .Append(" role=").Append(role)
                .Append(" active=").Append(NetSession.Active ? 1 : 0)
                .Append(" peer=").Append(NetSession.PeerUp ? 1 : 0)
                .Append(" ids=").Append(ids)
                .Append(" end");
            return sb.ToString();
        }

        // ---- the objects ---------------------------------------------------------------------

        private static int AppendEntities(StringBuilder sb, string role)
        {
            var scratch = new byte[ExtraScratchBytes];
            int n = 0;
            // The two ends read DIFFERENT registries -- the host's authoritative NetIdRegistry
            // and the client's puppet map -- and that asymmetry is the whole point: they are two
            // separately-built worlds keyed by the same netIds.
            if (role == "client")
            {
                foreach (var p in NetPuppets.LiveEntries())
                {
                    AppendEntity(sb, p.Id, p.TypeIdx, p.Comp, p.Provisional, client: true, scratch,
                        p.LastAppliedHp);
                    n++;
                }
            }
            else
            {
                foreach (NetIdRegistry.Entry e in NetIdRegistry.Live)
                {
                    // The host's own entities are never provisional -- it built the world. What
                    // it DOES have is the other half of the hp pair: the value it last put on
                    // the wire for this entity, which is what the joiner's `hpwire` must equal.
                    AppendEntity(sb, e.Id, e.TypeIdx, e.Comp, false, client: false, scratch,
                        e.LastSentHp);
                    n++;
                }
            }
            return n;
        }

        private static void AppendEntity(StringBuilder sb, ushort id, byte typeIdx, INetEntity comp,
            bool provisional, bool client, byte[] scratch, int lastAppliedHp)
        {
            // Safe by construction: NetTypeRegistry only ever matches AlienDrawableGameComponent
            // subclasses, and CreatePuppet returns one -- the same invariant the three production
            // downcasts rest on (see INetEntity's header).
            var adc = comp as AlienDrawableGameComponent;
            INetTypeDescriptor desc = NetTypeRegistry.Get(typeIdx);

            sb.Append("[netjip] ent id=").Append(id)
                .Append(" idx=").Append(typeIdx)
                .Append(" type=").Append(comp.GetType().Name)
                .Append(" pos=").Append(F(comp.Position.X)).Append(',').Append(F(comp.Position.Y))
                .Append(" rot=").Append(F(comp.NetRotation))
                .Append(" scale=").Append(F(comp.NetScale))
                .Append(" frame=").Append(F(comp.NetCurFrame))
                // Through the KILLABLE discriminant, not a raw field: hp is 0 for every
                // non-killable, so printing the number flat would make "unhurt" and "not a thing
                // that has hit points" the same value on both ends and hide a real disagreement.
                .Append(" hp=").Append(comp.NetKillable != null
                    ? comp.NetKillable.NetHitPoints.ToString(CultureInfo.InvariantCulture)
                    : "-")
                // WHAT CROSSED THE WIRE FOR THIS ENTITY (card d108c459) -- on a client the hp it
                // last APPLIED, on a host the hp it last SENT, `-` on either end before there is
                // one. Same key on both ends because it is the same quantity, which is the whole
                // point: `hp` above is the LIVE value, and the two peers' live values are NOT the
                // same quantity (a client's carries damage it has dealt locally since its last
                // snapshot turn; a host's has moved on since that entity's turn came round), the
                // same way their `pts` are not (card 94001db7). Measured on the live compare: a
                // Boss read 211 against 179. These two must agree.
                .Append(" hpwire=").Append(lastAppliedHp >= 0
                    ? lastAppliedHp.ToString(CultureInfo.InvariantCulture)
                    : "-")
                .Append(" dead=").Append(comp.IsDead ? 1 : 0)
                .Append(" dying=").Append(comp.NetIsDying ? 1 : 0)
                // Card de4d5d65's provisional shape, reported as a FLAG rather than inferred from
                // the extras below: a self-healed puppet built on defaults is an ordinary-looking
                // entity of the right type, so nothing about it can be read off the entity.
                .Append(" prov=").Append(provisional ? 1 : 0)
                // THE EMITTER, as a netId (card 9a7ee4c0). The visible half of a puppet that
                // was never built: a beam whose owner the joiner could not resolve is card
                // 9ccfe295's ownerless shape, and it is what made a big laser UFO shoot itself
                // dead. Compared EXACTLY by net_jip_sync -- netIds are identity-mapped, and
                // this reports the same thing the host's own spawn extra encodes, so the legit
                // "no emitter, or an emitter that is not replicated" case reads `-` on BOTH
                // ends (a GameScene warm-up beam) instead of inventing a mismatch.
                .Append(" owner=").Append(OwnerId(comp, client))
                .Append(" spawn=").Append(Extra(desc, adc, scratch, spawn: true))
                .Append(" state=").Append(Extra(desc, adc, scratch, spawn: false))
                // The declared local-simulation seams. The differ skips a key because THIS says
                // the game simulates it locally -- never because of a type name in the tool.
                .Append(" local=").Append(LocalSeams(comp))
                .Append('\n');
        }

        // The netId of whatever emitted this entity, or "-" for none. The two ends read
        // DIFFERENT registries for it, the same asymmetry AppendEntities has: the host's
        // authoritative NetIdRegistry, the client's puppet map.
        private static string OwnerId(INetEntity comp, bool client)
        {
            if (!(comp.NetOwner is GameComponent emitter))
            {
                return "-";
            }
            if (client)
            {
                return NetPuppets.TryGetId(emitter, out ushort pid)
                    ? pid.ToString(CultureInfo.InvariantCulture) : "-";
            }
            return NetIdRegistry.TryGetByComp(emitter, out NetIdRegistry.Entry e)
                ? e.Id.ToString(CultureInfo.InvariantCulture) : "-";
        }

        // Runs the type's REAL descriptor over the entity and prints the bytes. A throw is
        // reported rather than swallowed: two ends both reporting `err` still compare equal, and
        // an err on one end only is exactly the sort of asymmetry worth failing on.
        private static string Extra(INetTypeDescriptor desc, AlienDrawableGameComponent c,
            byte[] scratch, bool spawn)
        {
            if (desc == null || c == null)
            {
                return "-";
            }
            int end;
            try
            {
                end = spawn ? desc.EncodeSpawnExtra(c, scratch, 0) : desc.EncodeStateExtra(c, scratch, 0);
            }
            catch (Exception ex)
            {
                return "err:" + ex.GetType().Name;
            }
            if (end < 0 || end > scratch.Length)
            {
                // Not defensive decoding -- this is the dump noticing its own scratch is too
                // small, which would otherwise print a truncated block that compares equal.
                return "overflow:" + end;
            }
            if (end == 0)
            {
                return "-";
            }
            var hex = new StringBuilder(end * 2);
            for (int i = 0; i < end; i++)
            {
                hex.Append(scratch[i].ToString("x2", CultureInfo.InvariantCulture));
            }
            return hex.ToString();
        }

        private static string LocalSeams(INetEntity comp)
        {
            string s = "";
            if (comp.NetFrameLocal) { s += (s.Length > 0 ? "," : "") + "frame"; }
            if (comp.NetSpinPerMs != 0f) { s += (s.Length > 0 ? "," : "") + "rot"; }
            if (comp.NetScaleLocal) { s += (s.Length > 0 ? "," : "") + "scale"; }
            if (comp.NetPathOffset != Vector2.Zero) { s += (s.Length > 0 ? "," : "") + "path"; }
            return s.Length > 0 ? s : "-";
        }

        // ---- the things that are not objects ---------------------------------------------------

        private static void AppendExtras(StringBuilder sb)
        {
            // THE REMOVAL LEDGER, so a host-only id can be EXPLAINED rather than tolerated (card
            // d108c459). A client that released a puppet to its own death animation
            // (ReleaseDyingPuppet) drops it from the map this dump walks, while the host keeps
            // the entity for the whole 2.5-5 s animation -- so the id sets legitimately differ
            // for seconds. This is the client saying "I had that one and let it go"; an id the
            // host holds that appears in neither this line nor a host-side death is the real
            // defect the diff exists to catch. Always emitted, `-` when empty and on a host, so
            // the line's SHAPE is assertable off a peerless dump (net_jip_dump.txt).
            sb.Append("[netjip] gone ");
            if (NetSession.Active && NetSession.IsClient)
            {
                List<ushort> gone = NetPuppets.RemovedIds();
                sb.Append(gone.Count == 0 ? "-" : string.Join(",", gone));
            }
            else
            {
                sb.Append('-');
            }
            sb.Append('\n');

            GameScene scene = GameScene.NetActiveScene;
            sb.Append("[netjip] scene ")
                .Append(scene == null ? "none" : scene.NetCatchUpStateLine()).Append('\n');

            Oracle oracle = ServiceHelper.Get<IOracleService>()?.Oracle;
            ScoreVisualiser score = ServiceHelper.Get<IScoreService>()?.Score;
            if (oracle == null || score == null)
            {
                sb.Append("[netjip] hud unavailable\n");
                return;
            }

            sb.Append("[netjip] hud lives=").Append(score.Lives);
            for (int slot = 0; slot < Oracle.MaxPlayers; slot++)
            {
                // A FRESH array per slot, not one reused down the loop: NetReadHudState returns
                // EARLY for a slot past ScoreVisualiser's own list without writing `levels`, so a
                // shared buffer would print the previous slot's ladder for it -- and `lv` is
                // compared EXACTLY by net_jip_sync, so that both invents mismatches and masks
                // real ones.
                var levels = new int[NetProtocol.HudLevelCount];
                sb.Append(" | s").Append(slot)
                    .Append(" seat=").Append(oracle.IsSeated(slot) ? oracle.Controller(slot).ToString() : "-")
                    // `pts` is directly comparable across the two peers since card af96bcc2
                    // (one writer per slot): the replica is a verbatim adoption of the owner's
                    // declared total, so the two figures are the SAME quantity, at most one
                    // MsgHudState packet apart. The `uns=` field the dump used to carry (the
                    // provisional-ledger correction, cards b0ab09ec / 94001db7) is gone with
                    // the ledger -- dump v5.
                    .Append(" pts=").Append((int)score.PointScore(slot));
                if (slot < ScoreVisualiser.SlotCount)
                {
                    score.NetReadHudState(slot, levels, out int combo,
                        out Powerup.PowerupType? activeType, out float progress);
                    sb.Append(" combo=").Append(combo)
                        .Append(" pu=").Append(activeType.HasValue ? activeType.Value.ToString() : "none")
                        .Append('@').Append((int)(progress * 100f))
                        .Append(" lv=").Append(string.Join(",", levels));
                }
                // The Option ships are a replicated POPULATION (owner-authoritative per orbit
                // layer since card c5228350) and the one part of the ship the joiner rebuilds
                // rather than interpolates, so a JIP peer being permanently short shows here.
                PlayerShip ship = FindShip(oracle, slot);
                sb.Append(" opt=").Append(ship == null ? "-" : ship.NetOptionCount.ToString(CultureInfo.InvariantCulture));
            }
            sb.Append('\n');
        }

        private static PlayerShip FindShip(Oracle oracle, int slot)
        {
            foreach (PlayerShip s in oracle.GetShips())
            {
                if (s.Owner == slot)
                {
                    return s;
                }
            }
            return null;
        }

        // Fixed 3 decimals, invariant: the differ parses these, and a culture-dependent comma
        // would silently split one field into two. (InvariantGlobalization is on, so this is
        // belt-and-braces -- but the parse contract is worth stating at the format.)
        private static string F(float v) => v.ToString("0.000", CultureInfo.InvariantCulture);
    }
}
