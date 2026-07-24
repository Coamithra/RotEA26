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
// scenarios drive DetectCollisions() directly, which in a live level would give every real
// collidable an extra pass).
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

	// Scratch collidable for the mid-pass-spawn scenarios. Unlike TestAlien it takes part in
	// the real collision pass (Collides = true, a hand-placed collision shape) and records
	// every partner the handler hands it. `Spawn` is the one-shot mid-pass birth: it runs
	// from CollidesWith, so by construction the bin Add it makes happens while
	// DetectCollisions is on the stack — which is the whole point.
	private sealed class CollidingAlien : AlienDrawableGameComponent
	{
		private readonly ICollisionType shape;

		public readonly List<ICollidable> Seen = new List<ICollidable>();

		public Action Spawn;

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

		// Leave no trace: every removed/diverted scratch component landed in the recycle
		// pool (ComponentRemoved -> idleList, filter diverts -> idleList) — prune them so
		// repeated runs don't accumulate pooled TestAliens (each is an IComponentWatcher,
		// so they'd otherwise sit in the notify multiset forever).
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

	// Scenarios 5-6 (card bcdc7430): the collision pass's half of the instant-birth contract.
	// A collidable added DURING a DetectCollisions pass must not take part in that pass, and
	// must join the next one. PR #149 fixed the two loops that re-read the live
	// collidables.Count mid-pass; these pin one each.
	//
	// A plain "does it throw?" scenario would pass on the BROKEN code too — the out-of-range
	// index needs collidables.Count to exceed the high-water mark `boxes` accumulated from
	// prior play, so its verdict would depend on session history. Both scenarios below
	// therefore PLANT the precondition the fault needs, which is what makes them decide the
	// contract rather than the session.
	private static void RunCollisions(ComponentBin bin, Game game, Collection<IGameComponent> components, Action<string, bool> check)
	{
		CollisionHandler handler = (game as Game1)?.CollisionHandler;
		if (handler == null)
		{
			check("collision handler reachable", false);
			return;
		}

		// 5. FILL phase, all-pairs branch. Non-gridded collision types (CollisionMultibox /
		// CollisionLevelMap — level walls) keep the original all-pairs scan, which used to
		// enumerate the LIVE list; a spawn from one of its callbacks bumped the list version
		// mid-enumeration (InvalidOperationException). Deterministic on the broken code with
		// no setup at all: List<T>.MoveNext re-checks the version even on its final step.
		CollisionMultibox multibox = new CollisionMultibox();
		multibox.Items.Add(ProbeBox());
		CollidingAlien probe = new CollidingAlien(game, ProbeBox(), collides: true);
		CollidingAlien wall = new CollidingAlien(game, multibox, collides: true);
		bin.Add((GameComponent)(object)probe);
		bin.Add((GameComponent)(object)wall);
		CollidingAlien fillBorn = null;
		wall.Spawn = delegate
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

		// 6. RESOLUTION phase, the boxes[m] loop PR #149's IndexOutOfRange actually came from.
		// `filler` exists only to make the fault's precondition deterministic: a warm-up pass
		// fills boxes[filler's index] with the probe cell, then filler leaves the collidables
		// list, so the next pass's frozen count is exactly that index — and the pass's clear
		// loop (`i < boxes.Count && i != count`) stops AT it, leaving the previous frame's
		// cells in place. That is the "entries between the old and new count still hold the
		// previous frame's cells" case the fix documents. The newborn lands on that index, so
		// broken code resolves it against the planted cell and calls its CollidesWith.
		CollidingAlien left = new CollidingAlien(game, ProbeBox(), collides: true);
		CollidingAlien right = new CollidingAlien(game, ProbeBox(), collides: true);
		// Collides = false keeps the filler inert in both passes; the fill phase grids it
		// regardless, which is all it is here for.
		CollidingAlien filler = new CollidingAlien(game, ProbeBox(), collides: false);
		bin.Add((GameComponent)(object)left);
		bin.Add((GameComponent)(object)right);
		bin.Add((GameComponent)(object)filler);
		string warmThrew = RunPass(handler);
		// Appended last, so removing it shifts nobody: the index it vacates IS the next
		// pass's count, and the newborn appends straight onto it.
		bin.Remove((GameComponent)(object)filler);
		bin.Update();
		left.Seen.Clear();
		right.Seen.Clear();
		CollidingAlien born = null;
		left.Spawn = delegate
		{
			born = new CollidingAlien(game, ProbeBox(), collides: true);
			bin.Add((GameComponent)(object)born);
		};
		string passThrew = RunPass(handler);
		check("resolution mid-pass spawn doesn't throw" + Threw(warmThrew ?? passThrew), warmThrew == null && passThrew == null);
		check("resolution phase reached both ways", left.Seen.Contains(right) && right.Seen.Contains(left));
		check("resolution spawn is instant", born != null && components.Contains((IGameComponent)(object)born));
		check("newborn sits out its own pass", born != null && born.Seen.Count == 0);
		// The other half of the contract: excluded from the pass that bore it, not excluded
		// for good — a fix that dropped the newborn permanently would fail here.
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
			return ex.GetType().Name;
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
