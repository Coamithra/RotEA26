using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

// Draws EVERY live Braineroid's additive blue glow in ONE batch, at its own DrawOrder just under
// the brains (card 391e11d2). The FlyingSpiderSwarm pattern, for a different cost: there the win
// was one render-target round trip instead of N, here it is one SpriteBatch flush instead of N.
//
// WHY IT IS WORTH A COMPONENT. SpriteBatchWrapper opens a new batch whenever the effect or blend
// state changes (`_beginDrawing` -> `effectHandler.HasChanged()` -> `Flush`), and BlazorGL's cost
// is per-CALL. Braineroid.Draw used to flip BlendMode to Additive for its glow and straight back
// to AlphaBlend for the brain, so a brain and its glow could never share a batch with the NEXT
// braineroid's: each one cost two. Measured on the final boss's brainz wave, 29 Braineroids ran
// the frame at 58.6 batches. Drawing all the glows first collapses their half to a single
// additive batch, so the same wave costs ~30. (The brains stay one batch each -- they set
// per-sprite InterpOffset/InterpDelta/FadeValue through the interpolation shader, which no
// batching can merge. That half is reported, not fixed.)
//
// TWO INSTANCES, ONE PER BAND -- and that is not tidiness, it is the whole correctness of the
// thing. Braineroid.Initialize puts huge/medium brains at DrawOrder 20 and SMALL ones at 800, so
// a small brain's glow used to draw above the BrainBoss (21), the walls and everything else up to
// 800. Collapsing every glow onto one low DrawOrder therefore did not merely re-order glows among
// themselves: it dragged the small brains' glows underneath the boss, which hid them behind its
// opaque cables. Measured, that cost 7419 px (peak 129/255) on a 12-brain frame, one-directional
// (never brighter) -- an obvious dimming, not a rounding difference. So each instance serves ONE
// band and sits just under it (19 for the 20s, 799 for the 800s), which puts every glow back in
// the same layer it always occupied.
//
// WHAT MOVES, AFTER THAT. The brains do not move at all. A glow no longer draws immediately
// before its OWN brain but immediately before its band, so within a band a glow now sits behind
// the other brains in that band instead of in front of whichever of them happened to be added
// earlier -- the same "removes double-brightening rather than changes shape" difference the
// FlyingSpiderSwarm flatten makes, and within a DrawOrder the previous order was spawn order,
// i.e. arbitrary. Measured at 0 px on the same frame (see the card).
//
// A plain DrawableGameComponent, not an AlienDrawableGameComponent: pure draw, owns no position,
// and must keep drawing while the world is paused (a pause freezes Enabled/Update, not Visible --
// the frozen brains still draw, so their glows have to as well).
//
// Owned by GameScene (added in Initialize, removed in Terminate) rather than by a level: brains
// are spawned by BrainBoss, BraineroidsLevel, BrainSpawner and StationarySpawner across several
// levels, and every one of those is inside a GameScene. Level scenes are re-added singletons, so
// a drawable left behind in the global bin would draw over later scenes (the NetWaitOverlay
// lesson).
internal class BraineroidGlows : DrawableGameComponent, IComponentWatcher
{
	// True only while an instance is actually live in the bin. Braineroid.Draw suppresses its own
	// glow ONLY when this says someone is driving it -- otherwise a scene that never adds the
	// component (the sprite harness, the end-credits Cast screen, any future level) would draw
	// glowless brains, which is a look regression rather than a slow frame. Set on Initialize
	// (ComponentBin.Add runs it synchronously) and cleared when this instance leaves the bin.
	internal static bool Active => _live && !Suppressed;

	private static bool _live;

	// The A/B seam, and the ONLY way to compare the two draw paths honestly (card 391e11d2).
	// Gameplay RNG is unseeded, so two boots of the same level never reach the same world state
	// and a cross-boot pixel diff measures the wave, not the change. Flipping this between two
	// `shot`s with NO `step` in between puts both paths over the SAME frame -- the same rig the
	// bomb-ripple card's "the honest A/B is IN ONE PROCESS" note describes. false (the default) =
	// the batched driver; true = the pre-card per-brain path, which Braineroid.Draw falls back to
	// whenever nothing is driving it. Console eaBraineroidGlowBatch(on) / `eval
	// BraineroidGlowBatch <on|off>`.
	internal static bool Suppressed;

	// The DrawOrder bands Braineroid.Initialize uses. Add a size that draws at a new DrawOrder and
	// it MUST get a band here, or its glow silently changes layer (see the class comment).
	internal static readonly int[] Bands = { 20, 800 };

	// Scratch, reused per frame: this runs every Draw and must not allocate.
	private readonly List<Braineroid> members = new List<Braineroid>();

	private SpriteBatchWrapper spriteBatch;

	// The brains' DrawOrder this instance serves; it draws only their glows, one slot below them.
	private readonly int band;

	// Live instances, so Active stays true while ANY band is driving -- Braineroid.Draw asks the
	// single question "is someone drawing my glow for me", and both instances answer it.
	private static int _liveCount;

	public BraineroidGlows(Game game, int band)
		: base(game)
	{
		this.band = band;
		base.DrawOrder = band - 1;
		spriteBatch = ServiceHelper.Get<ISpriteBatchWrapperService>().SpriteBatchWrapper;
	}

	public override void Initialize()
	{
		base.Initialize();
		_liveCount++;
		_live = true;
	}

	// IComponentWatcher rather than a raw Components.ComponentRemoved subscription, for the reason
	// FlyingSpiderSwarm spells out: a `+=` in the ctor is never unsubscribed and would root this
	// component in the collection's event for the rest of the process.
	public void OnComponentRemoved(GameComponentCollectionEventArgs e)
	{
		if (e.GameComponent == this)
		{
			_liveCount--;
			_live = _liveCount > 0;
		}
	}

	public void OnComponentAdded(GameComponentCollectionEventArgs e)
	{
	}

	public override void Draw(GameTime gameTime)
	{
		// Suppressed: every Braineroid.Draw is drawing its own glow again, so drawing them here
		// too would double the additive contribution.
		if (Suppressed)
		{
			return;
		}
		CollectMembers();
		if (members.Count == 0)
		{
			return;
		}
		// ONE blend flip for the whole population instead of two per brain. Restored afterwards
		// because the wrapper's BlendMode is shared state that the next drawer inherits.
		spriteBatch.BlendMode = (SpriteBlendMode)2;
		for (int i = 0; i < members.Count; i++)
		{
			members[i].DrawGlowOnly(gameTime);
		}
		spriteBatch.BlendMode = (SpriteBlendMode)1;
	}

	// One pass over the live components, the same shape Oracle.GetBaddies and FlyingSpiderSwarm
	// use. Deliberately not a subscription-maintained mirror list: a stale entry would draw a glow
	// for a dead brain, and the scan is one type test per component per frame.
	private void CollectMembers()
	{
		// Cleared by the collector that owns the scratch, so a throw between the collect and the
		// draw cannot leave entries behind for the next frame to append to.
		members.Clear();
		foreach (GameComponent item in (Collection<IGameComponent>)(object)base.Game.Components)
		{
			if (item is Braineroid brain && brain.Visible && brain.DrawOrder == band)
			{
				members.Add(brain);
			}
		}
	}
}
