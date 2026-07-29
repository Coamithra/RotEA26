using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using Microsoft.Xna.Framework;
using EvilAliens;

namespace EvilAliensWeb.Compat;

// Console scenario suite for the ComponentBin lifecycle contract (card 02d9ad67):
// instant births, deferred deaths + top-of-tick flush, the standing purge filter, and
// pause-aware adds — plus the collision pass's half of that contract (card bcdc7430): a
// collidable born mid-pass must not take part in the pass that bore it, and must join the
// next one. Invoke with eaBinTest() from the browser console — best from the main menu (the
// pause scenario briefly Push/Pops the live collection, which force-enables anything that
// was deliberately disabled, exactly like a real pause/unpause does; and the collision
// scenarios drive FOUR extra DetectCollisions() passes plus three extra death flushes,
// re-running every real collision that many times, with their own live Collides=true boxes
// parked mid-playfield for the duration).
// Some checks are PRECONDITIONS rather than assertions about the code under test (a
// reachable collision handler, the planted stale cell scenario 8 needs). A failed
// precondition short-circuits the rest of its scenario, so the pass/fail tally shrinks --
// read the FAIL line, never the count.
// Written in place of an offline sim on purpose (the eaNetSim.test rule): the policy under
// test IS ComponentBin.cs / CollisionHandler.cs — a mirror would drift and prove nothing.
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

	// The live EvilBullets in the world, so the net-puppet scenario can tell the puppet it just
	// built from any the game already owned.
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

	// Scratch collidable for the mid-pass-spawn scenarios. Unlike TestAlien it takes part in
	// the real collision pass (Collides = true, a hand-placed collision shape) and records
	// every partner the handler hands it. `Spawn` is the one-shot mid-pass birth: it runs
	// from CollidesWith, so by construction the bin Add it makes happens while
	// DetectCollisions is on the stack — which is the whole point. It is an EVENT because
	// scenario 8 can only subscribe after its warm-up pass, i.e. after Add: the configure-
	// before-Add rule (tools/audit_add_order.py) exempts event subscriptions, and this is
	// exactly the hook-up-after-Add case it exempts them for.
	private sealed class CollidingAlien : AlienDrawableGameComponent
	{
		private readonly ICollisionType shape;

		public readonly List<ICollidable> Seen = new List<ICollidable>();

		public event Action Spawn;

		public CollidingAlien(Game game, ICollisionType shape, bool collides)
			: base(game)
		{
			this.shape = shape;
			Collides = collides;
			((DrawableGameComponent)this).Visible = false;
		}

		public override ICollisionType CollisionType => shape;

		public override void CollidesWith(ICollidable other)
		{
			Seen.Add(other);
			Action spawn = Spawn;
			if (spawn != null)
			{
				Spawn = null;
				spawn();
			}
		}
	}

	// Every scratch collidable below is this 20px square centred on design (440, 300), which
	// falls wholly inside grid cell (5,3) — so they all overlap each other and occupy exactly
	// one cell, keeping the index/cell arithmetic the scenarios rely on trivial to follow.
	private const float ProbeX = 440f;

	private const float ProbeY = 300f;

	private const float ProbeSize = 20f;

	private static CollisionBox ProbeBox()
	{
		return new CollisionBox(ProbeX, ProbeY, ProbeSize, ProbeSize, center: true);
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
		// component the filter diverted points that reference at a ship the world does not
		// have. This is the PRIMITIVE; the two call sites are covered end to end by
		// Compat/Net/NetResetSpawnTest.cs, which also measured the window at one tick.
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

		RunCollisions(bin, game, components, Check);

		sb.Append("[bin] " + pass + " passed, " + fail + " failed");
		return sb.ToString();
	}

	// Scenarios 7-8 (card bcdc7430): the collision pass's half of the instant-birth contract.
	// A collidable added DURING a DetectCollisions pass must not take part in that pass, and
	// must join the next one.
	//
	// A plain "does it throw?" scenario would pass on the BROKEN code — the out-of-range index
	// needs collidables.Count to exceed the high-water mark `boxes` accumulated from prior play,
	// so its verdict would be a function of session history rather than of the contract. These
	// two therefore PLANT the precondition the fault needs, and ASSERT the plant took (a
	// silently-missing plant is the one way they could go quietly vacuous).
	//
	// Commit 8e3f4ef froze THREE bounds, not two. Scenario 8 covers the resolution loop.
	// Scenario 7 covers the other two together, and has to: only a non-gridded type's callback
	// runs during the fill phase at all, so its spawner is the sole way to reach either. With
	// the inner all-pairs bound live the enumeration throws; with only the outer fill bound
	// live the newborn is instead gridded into fieldMatrix, which "newborn invisible to the
	// fill pass" catches (or it indexes `boxes` past the entries this pass sized, which the
	// throw check catches) — so either bound regressing alone still fails the scenario.
	private static void RunCollisions(ComponentBin bin, Game game, Collection<IGameComponent> components, Action<string, bool> check)
	{
		CollisionHandler handler = (game as Game1)?.CollisionHandler;
		if (handler == null)
		{
			check("PRECONDITION collision handler reachable", false);
			return;
		}

		// 7. FILL phase. Non-gridded collision types (CollisionMultibox / CollisionLevelMap —
		// level walls) keep the original all-pairs scan, which used to enumerate the LIVE list;
		// a spawn from one of its callbacks bumped the list version mid-enumeration
		// (InvalidOperationException). Deterministic on the broken code with no setup at all:
		// List<T>.MoveNext re-checks the version even on its final step.
		CollisionMultibox multibox = new CollisionMultibox();
		multibox.Items.Add(ProbeBox());
		CollidingAlien probe = new CollidingAlien(game, ProbeBox(), collides: true);
		CollidingAlien wall = new CollidingAlien(game, multibox, collides: true);
		bin.Add((GameComponent)(object)probe);
		bin.Add((GameComponent)(object)wall);
		CollidingAlien fillBorn = null;
		wall.Spawn += delegate
		{
			fillBorn = new CollidingAlien(game, ProbeBox(), collides: true);
			bin.Add((GameComponent)(object)fillBorn);
		};
		string fillThrew = RunPass(handler);
		check("all-pairs mid-pass spawn doesn't throw" + Threw(fillThrew), fillThrew == null);
		// Positive control: without it every assertion below would also hold for a pass that
		// silently never ran the branch (or a probe geometry that stopped overlapping).
		check("all-pairs branch reached both ways", probe.Seen.Contains(wall) && wall.Seen.Contains(probe));
		check("all-pairs spawn is instant", fillBorn != null && components.Contains((IGameComponent)(object)fillBorn));
		check("newborn sits out the fill pass", fillBorn != null && fillBorn.Seen.Count == 0);
		check("newborn invisible to the fill pass", fillBorn != null && !probe.Seen.Contains(fillBorn) && !wall.Seen.Contains(fillBorn));
		Retire(bin, probe, wall, fillBorn);

		// 8. RESOLUTION phase, the boxes[m] loop PR #149's IndexOutOfRange actually came from.
		// `filler` exists only to make the fault's precondition deterministic: a warm-up pass
		// fills boxes[filler's index] with the probe cell, then filler leaves the collidables
		// list, so the next pass's frozen count is exactly that index — and the pass's clear
		// loop (`i < boxes.Count && i != count`) stops AT it, leaving the previous frame's cells
		// in place. That is the "entries between the old and new count still hold the previous
		// frame's cells" case the fix documents. The newborn appends onto that index, so broken
		// code resolves it against the planted cell and calls its CollidesWith.
		CollidingAlien left = new CollidingAlien(game, ProbeBox(), collides: true);
		CollidingAlien right = new CollidingAlien(game, ProbeBox(), collides: true);
		// Collides = false keeps the filler inert in both passes; the fill phase grids it
		// regardless, which is all it is here for.
		CollidingAlien filler = new CollidingAlien(game, ProbeBox(), collides: false);
		bin.Add((GameComponent)(object)left);
		bin.Add((GameComponent)(object)right);
		bin.Add((GameComponent)(object)filler);
		// The whole scenario rests on filler sitting LAST, so that removing it shifts nobody and
		// the index it vacates is exactly the next pass's frozen count. Every step of that is
		// asserted below rather than assumed: a real collidable spawning from a real collision
		// callback during the warm-up would shift it, and the scenario would otherwise go green
		// on the broken code. That is also why the suite is documented as menu-only — a busy
		// world can legitimately fail this precondition.
		int plantIndex = handler.Collidables.Count - 1;
		bool planted = plantIndex >= 0 && handler.Collidables[plantIndex] == (ICollidable)filler;
		string warmThrew = RunPass(handler);
		if (warmThrew != null)
		{
			// The plant never happened, so every assertion below would report on nothing.
			check("PRECONDITION warm-up pass doesn't throw" + Threw(warmThrew), false);
			Retire(bin, left, right, filler);
			return;
		}
		bin.Remove((GameComponent)(object)filler);
		bin.Update();
		planted = planted && handler.Collidables.Count == plantIndex;
		check("PRECONDITION stale cell planted at the next pass's count", planted);
		left.Seen.Clear();
		right.Seen.Clear();
		CollidingAlien born = null;
		left.Spawn += delegate
		{
			born = new CollidingAlien(game, ProbeBox(), collides: true);
			bin.Add((GameComponent)(object)born);
		};
		string passThrew = RunPass(handler);
		check("resolution mid-pass spawn doesn't throw" + Threw(passThrew), passThrew == null);
		check("resolution phase reached both ways", left.Seen.Contains(right) && right.Seen.Contains(left));
		check("resolution spawn is instant", born != null && components.Contains((IGameComponent)(object)born));
		// Belt and braces on the plant: the newborn has to have landed on the planted index, or
		// the check below is not testing what it says it is.
		check("newborn landed on the planted index",
			born != null && plantIndex < handler.Collidables.Count && handler.Collidables[plantIndex] == (ICollidable)born);
		check("newborn sits out its own pass", born != null && born.Seen.Count == 0);
		// The other half of the contract: excluded from the pass that bore it, not excluded for
		// good — a fix that dropped the newborn permanently would fail here. Clear Seen first, or
		// this reads the entries the PREVIOUS pass wrote and goes green on the broken code.
		born?.Seen.Clear();
		string nextThrew = RunPass(handler);
		check("newborn joins the next pass" + Threw(nextThrew), nextThrew == null && born != null && born.Seen.Count > 0);
		Retire(bin, left, right, filler, born);
	}

	private static string RunPass(CollisionHandler handler)
	{
		try
		{
			handler.DetectCollisions();
			return null;
		}
		catch (Exception ex)
		{
			// Name AND message: for an UNEXPECTED failure this console line is the whole
			// diagnostic — a bare "[NullReferenceException]" would say nothing about where.
			return ((object)ex).GetType().Name + ": " + ex.Message;
		}
	}

	private static string Threw(string exception)
	{
		return (exception == null) ? "" : (" [" + exception + "]");
	}

	// Take the scratch collidables back out of the world and out of the recycle pool — each
	// is an IComponentWatcher, so a pooled leftover would sit in the notify multiset forever
	// and every later eaBinTest() run would add more.
	private static void Retire(ComponentBin bin, params CollidingAlien[] scratch)
	{
		foreach (CollidingAlien alien in scratch)
		{
			if (alien != null)
			{
				bin.Remove((GameComponent)(object)alien);
			}
		}
		bin.Update();
		foreach (CollidingAlien alien in scratch)
		{
			if (alien != null)
			{
				bin.PruneIdle((GameComponent)(object)alien);
			}
		}
	}
}
