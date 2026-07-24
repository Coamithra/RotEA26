using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using Microsoft.Xna.Framework;
using EvilAliens;

namespace EvilAliensWeb.Compat;

// Console scenario suite for the ComponentBin lifecycle contract (card 02d9ad67):
// instant births, deferred deaths + top-of-tick flush, the standing purge filter, and
// pause-aware adds. Invoke with eaBinTest() from the browser console — best from the main
// menu (the pause scenario briefly Push/Pops the live collection, which force-enables
// anything that was deliberately disabled, exactly like a real pause/unpause does).
// Written in place of an offline sim on purpose (the eaNetSim.test rule): the policy under
// test IS ComponentBin.cs — a mirror would drift and prove nothing.
internal static class BinTest
{
	private sealed class TestAlien : AlienDrawableGameComponent
	{
		private readonly CollisionBox box = new CollisionBox();

		public TestAlien(Game game)
			: base(game)
		{
			// Never collides, never draws — the suite only exercises lifecycle bookkeeping,
			// and every instance is added+removed within the single synchronous call.
			Collides = false;
			((DrawableGameComponent)this).Visible = false;
		}

		public override ICollisionType CollisionType => box;
	}

	// The live EvilBullets in the world, so scenario 6 can tell the puppet it just built from
	// any the game already owned.
	private static HashSet<GameComponent> CollectBullets(Game game)
	{
		HashSet<GameComponent> set = new HashSet<GameComponent>();
		foreach (GameComponent item in (Collection<IGameComponent>)(object)game.Components)
		{
			if (item is EvilBullet)
			{
				set.Add(item);
			}
		}
		return set;
	}

	public static string Run()
	{
		ComponentBin bin = ServiceHelper.Get<IComponentBinService>().ComponentBin;
		Game game = bin.Game;
		Collection<IGameComponent> components = (Collection<IGameComponent>)(object)game.Components;
		StringBuilder sb = new StringBuilder();
		int pass = 0;
		int fail = 0;
		void Check(string name, bool ok)
		{
			if (ok)
			{
				pass++;
			}
			else
			{
				fail++;
			}
			sb.Append(ok ? "PASS " : "FAIL ").Append(name).Append('\n');
		}

		// 1. Births are instant: in Game.Components (visible to collisions/scans/purges)
		// the moment Add returns, enabled, but — per KNI's journal — not Updated this tick.
		TestAlien a = new TestAlien(game);
		bin.Add((GameComponent)(object)a);
		Check("instant-add membership", components.Contains((IGameComponent)(object)a));
		Check("instant-add enabled", a.Enabled);

		// 2. Deaths stay queued until a flush (mid-tick or top-of-tick), then recycle.
		bin.Remove((GameComponent)(object)a);
		Check("remove is deferred", components.Contains((IGameComponent)(object)a));
		bin.Update();
		Check("mid-tick flush removes", !components.Contains((IGameComponent)(object)a));

		// 3. Standing purge filter: a same-tick Add after Purge<T> is diverted to the pool,
		// a resurrect-Add of a purged live instance doesn't stick, and the filter expires
		// at the top-of-tick flush so next-tick spawns are untouched.
		TestAlien b = new TestAlien(game);
		bin.Add((GameComponent)(object)b);
		bin.Purge<TestAlien>();
		TestAlien c = new TestAlien(game);
		bin.Add((GameComponent)(object)c);
		Check("purge-filter diverts late add", !components.Contains((IGameComponent)(object)c));
		bin.Add((GameComponent)(object)b);
		bin.TopOfTickFlush();
		Check("purged instance stays dead", !components.Contains((IGameComponent)(object)b));
		TestAlien d = new TestAlien(game);
		bin.Add((GameComponent)(object)d);
		Check("filter expired after flush", components.Contains((IGameComponent)(object)d));
		// Non-standing purge (the UpdateStartup clear-and-respawn-NOW pattern): the purge
		// still queues the sweep but arms NO filter, so a same-tick re-add sticks.
		bin.Purge<TestAlien>(standing: false);
		TestAlien d2 = new TestAlien(game);
		bin.Add((GameComponent)(object)d2);
		Check("non-standing purge lets respawn through", components.Contains((IGameComponent)(object)d2));
		bin.Remove((GameComponent)(object)d2);
		bin.TopOfTickFlush();

		// 4. Pause-aware add: a world object added while the world is pushed joins the
		// freeze (Enabled=false) and the pause layer, so Pop thaws it with everything else.
		bin.Push();
		TestAlien e = new TestAlien(game);
		bin.Add((GameComponent)(object)e);
		bool frozen = components.Contains((IGameComponent)(object)e) && !e.Enabled;
		bin.Pop();
		Check("pause add joins the freeze", frozen);
		Check("pop thaws it", e.Enabled);
		bin.Remove((GameComponent)(object)e);
		bin.Update();

		// 5. TryAdd reports whether the add actually LANDED (card 74403f83). This is the
		// contract the net layer's two ship-puppet spawn sites branch on: they keep a
		// reference to what they add and gate their retry on it being null, so adopting a
		// component the filter diverted strands that player for the rest of the session.
		bin.Purge<TestAlien>();
		TestAlien f = new TestAlien(game);
		Check("TryAdd reports a diverted add", !bin.TryAdd((GameComponent)(object)f));
		bin.TopOfTickFlush();
		TestAlien g = new TestAlien(game);
		Check("TryAdd reports a landed add", bin.TryAdd((GameComponent)(object)g));
		bin.Remove((GameComponent)(object)g);
		bin.Update();

		// 6. The puppet layer is EXEMPT from the standing purge filter (card 74403f83).
		// Driven through the REAL NetPuppets.OnSpawn path, not a mirror: Enable() needs only
		// a Game plus the ServiceHelper bin/score (both live from Game1.Initialize), so no
		// transport and no paired session are required. Before the fix this scenario fails
		// with the puppet registered but absent from the world — the silent ghost the card
		// is about: never drawn, never collidable, and invisible to OnSnapshotEntry's
		// self-heal, which only rebuilds ids it has NEVER seen.
		// GameScene.NetActiveScene is the "a real world is up" test, and it must gate this:
		// the scenario arms Purge<AlienDrawableGameComponent> for real, which during a level —
		// or during the ATTRACT DEMO the main menu launches by itself after an idle timeout —
		// would queue every ship, enemy, powerup and wall for death, and the cleanup flush
		// below would carry it out. Scenarios 1-5 only ever touch a private TestAlien; this one
		// touches the root class of every world object, so it refuses to run near one.
		if (Net.NetSession.Active || Net.NetPuppets.LiveCount > 0 || GameScene.NetActiveScene != null)
		{
			// Also covers a real co-op session, which owns the puppet layer -- Enable/Disable
			// here would tear it down mid-flight. Report rather than silently "passing" an
			// unrun scenario.
			sb.Append("SKIP net-puppet scenarios (run from the main menu, with no level or attract demo up)\n");
		}
		else
		{
			EvilBullet puppet = null;
			try
			{
				Net.NetPuppets.Enable(game);
				// Exactly what GameScene.UpdateResetting / UpdateWin arm, and what
				// NetApplyReset arms from inside the rx drain itself.
				bin.Purge<AlienDrawableGameComponent>();
				Net.NetBaseState state = default(Net.NetBaseState);
				state.Pos = new Vector2(-400f, -400f); // off-screen: never drawn, never collides
				state.Scale = 1f;
				// typeIdx 0 = EvilBulletDescriptor, the simplest replicable (no spawn extras).
				// Identify the puppet as the EvilBullet that was NOT there beforehand: a bare
				// "is there an EvilBullet?" scan would pass vacuously on any pre-existing one
				// (and the cleanup below would then evict a bullet the game still owns).
				bool built;
				HashSet<GameComponent> before = CollectBullets(game);
				built = Net.NetPuppets.OnSpawn(64001, 0, state, new byte[1], 0, 0);
				foreach (GameComponent item in CollectBullets(game))
				{
					if (!before.Contains(item))
					{
						puppet = (EvilBullet)item;
					}
				}
				Check("puppet spawn reports success", built);
				Check("puppet survives the standing purge filter", puppet != null);
				// The invariant the ghost broke: the id registry must never hold a puppet
				// that isn't in the world. (OnSpawn's own !landed guard is defence in depth
				// against a future purge path — the exemption above makes it unreachable from
				// here, so it is deliberately not asserted; it logs if it ever does fire.)
				Check("registry agrees with the world",
					Net.NetPuppets.LiveCount == (puppet != null ? 1 : 0));
			}
			catch (Exception ex)
			{
				Check("net-puppet scenarios ran (" + ex.GetType().Name + ": " + ex.Message + ")", ok: false);
			}
			finally
			{
				Net.NetPuppets.Disable();
				// Disable() deliberately leaves live puppets to the scene's Terminate purge,
				// but this suite has no scene — take the component out ourselves.
				if (puppet != null)
				{
					bin.Remove((GameComponent)(object)puppet);
					bin.Update();
					bin.PruneIdle((GameComponent)(object)puppet);
				}
				// Expire the AlienDrawableGameComponent filter this scenario armed. A live
				// game clears it on the next tick anyway, but the suite must hand the bin
				// back exactly as it found it: TestAlien IS an AlienDrawableGameComponent, so
				// a second back-to-back run would otherwise have its own scenario-1 add
				// diverted and report a phantom failure.
				bin.TopOfTickFlush();
			}
		}

		// Leave no trace: every removed/diverted scratch component landed in the recycle
		// pool (ComponentRemoved -> idleList, filter diverts -> idleList) — prune them so
		// repeated runs don't accumulate pooled TestAliens (each is an IComponentWatcher,
		// so they'd otherwise sit in the notify multiset forever).
		bin.PruneIdle((GameComponent)(object)f);
		bin.PruneIdle((GameComponent)(object)g);
		bin.PruneIdle((GameComponent)(object)a);
		bin.PruneIdle((GameComponent)(object)b);
		bin.PruneIdle((GameComponent)(object)c);
		bin.PruneIdle((GameComponent)(object)d);
		bin.PruneIdle((GameComponent)(object)d2);
		bin.PruneIdle((GameComponent)(object)e);

		sb.Append("[bin] " + pass + " passed, " + fail + " failed");
		return sb.ToString();
	}
}
