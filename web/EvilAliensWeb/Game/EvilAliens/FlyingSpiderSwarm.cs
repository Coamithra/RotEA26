using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

// The SHIPPED flatten path for the background flying-spider swarm (card 9c92962e): flatten the
// WHOLE swarm through ONE render-target round trip per frame instead of one per spider. Default
// since the card's measurement; ?flyspiderflatten=per|0 are the A/B overrides.
//
// WHY IT CAN EXIST AT ALL: every background spider shares the same fog alpha (FlyingSpider
// .Initialize sets `new Color(1,1,1,0.2f)` unconditionally), and the flatten's only job is to stop
// straight-alpha overlaps double-brightening. So the "group" can be the whole swarm rather than
// one spider: draw them all OPAQUE into the shared RT, composite the union once at the fog alpha.
// The render-target cost stops scaling with the population — one Clear + one composite per frame
// instead of N.
//
// WHAT IT CHANGED ABOUT THE LOOK vs the old per-spider path: spider-vs-spider overlaps stop
// double-brightening too, not just body-vs-wing. Arguably more correct (the fog layer reads as
// one translucent stratum), and at alpha 0.2 over bright Mars dust the difference is not
// perceptible — which is what let the measurement card ship it as the default.
//
// HOW IT HOOKS IN: background spiders draw at DrawOrder 1 (between Background's 0 and Floor's 2).
// This component takes that slot and calls FlyingSpider.DrawFlattened on each one itself, while
// FlyingSpider.Draw early-outs in Swarm mode so nothing draws twice. Driving the draws directly —
// rather than sandwiching the bin's own draw pass between two bracketing components — means the
// bracket cannot be broken by anything else that happens to share DrawOrder 1.
//
// It is a plain DrawableGameComponent, not an AlienDrawableGameComponent: it is pure draw, owns no
// position, and must keep drawing while the world is paused (a pause freezes Enabled/Update, not
// Visible — the frozen spiders still draw, so their flatten still has to run).
//
// Owned by Level2 the same way `floor` is: constructed with the level, added in Initialize,
// removed in Level2_OnFinished. Level scenes are re-added singletons, so a drawable left behind in
// the global bin would draw over later scenes (the NetWaitOverlay lesson).
internal class FlyingSpiderSwarm : DrawableGameComponent
{
	// True only while an instance is actually live in the bin. FlyingSpider.Draw suppresses its own
	// draw in Swarm mode ONLY when this says someone is driving it — otherwise a scene that never
	// adds the component (the sprite harness, any future level) would draw no background spiders at
	// all, which is a blank screen rather than a slow one. Set on Initialize (ComponentBin.Add runs
	// it synchronously) and cleared when this instance leaves the bin.
	internal static bool Active { get; private set; }

	// Scratch, reused per frame: the swarm pass runs every Draw and must not allocate.
	private readonly List<FlyingSpider> members = new List<FlyingSpider>();

	private SpriteBatchWrapper spriteBatch;

	public FlyingSpiderSwarm(Game game)
		: base(game)
	{
		// The DrawOrder background spiders would have drawn at themselves.
		base.DrawOrder = 1;
		spriteBatch = ServiceHelper.Get<ISpriteBatchWrapperService>().SpriteBatchWrapper;
		game.Components.ComponentRemoved += OnComponentRemoved;
	}

	public override void Initialize()
	{
		base.Initialize();
		Active = true;
	}

	private void OnComponentRemoved(object sender, GameComponentCollectionEventArgs e)
	{
		if (e.GameComponent == this)
		{
			Active = false;
		}
	}

	public override void Draw(GameTime gameTime)
	{
		CollectMembers();
		if (members.Count == 0)
		{
			return;
		}
		// One box for the union. Every member's own FlattenBox is honoured (so ?flyspiderbox= still
		// moves the cost here), unioned rather than assumed to be the full screen: a small swarm
		// clustered in one corner should not pay for a full-screen render target.
		Rectangle box = members[0].FlattenBox;
		for (int i = 1; i < members.Count; i++)
		{
			box = Rectangle.Union(box, members[i].FlattenBox);
		}
		byte fogAlpha = members[0].FogAlpha;
		spriteBatch.BlendMode = (SpriteBlendMode)1;
		spriteBatch.BeginGroupFlatten(box);
		for (int i = 0; i < members.Count; i++)
		{
			members[i].DrawFlattened(gameTime);
		}
		spriteBatch.BlendMode = (SpriteBlendMode)1;
		spriteBatch.EndGroupFlatten(new Color((byte)255, (byte)255, (byte)255, fogAlpha));
		members.Clear();
	}

	// One pass over the live components, the same shape Oracle.GetBaddies uses. Deliberately not a
	// subscription-maintained mirror list: a ComponentAdded/ComponentRemoved pair would have to
	// survive scene teardown, purges and the recycle pool, and a stale entry here draws a dead
	// spider. The scan costs one type test per component per frame, orders below the draw it
	// brackets.
	private void CollectMembers()
	{
		foreach (GameComponent item in (Collection<IGameComponent>)(object)base.Game.Components)
		{
			if (item is FlyingSpider spider && spider.NetIsBackground && spider.Visible)
			{
				members.Add(spider);
			}
		}
	}
}
