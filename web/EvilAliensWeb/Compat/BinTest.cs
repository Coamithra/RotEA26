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

		sb.Append("[bin] " + pass + " passed, " + fail + " failed");
		return sb.ToString();
	}
}
