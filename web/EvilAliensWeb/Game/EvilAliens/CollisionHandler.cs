using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace EvilAliens;

public class CollisionHandler
{
	private struct BoxInfo
	{
		public int x;

		public int y;

		public BoxInfo(int x, int y)
		{
			this.x = x;
			this.y = y;
		}
	}

	private const int pixelsPerSquare = 80;

	private const int squaresX = 10;

	private const int squaresY = 8;

	// Upper bound on DDA line-rasteriser iterations (see FillCollisionMatrixLine). A straight line
	// touches at most squaresX + squaresY cells; the generous headroom covers off-screen beam
	// origins while still turning a degenerate near-axis-aligned line's infinite spin into a bounded
	// no-op.
	private const int maxLineSteps = 128;

	// Cells hold INDICES into `collidables`, not references (card 391e11d2). That is what lets the
	// candidate gather below dedupe with an O(1) stamp instead of List.Contains' linear scan --
	// which was O(k^2) per entity in a cluster, and a boss wave IS one big cluster.
	private List<int>[,] fieldMatrix = new List<int>[10, 8];

	private List<ICollidable> collidables = new List<ICollidable>();

	private Game game;

	private List<List<BoxInfo>> boxes = new List<List<BoxInfo>>();

	private List<int> colliders = new List<int>();

	// Dedupe stamp for the candidate gather: `seen[i] == stamp` means collidable i is already in
	// `colliders` for the entity being resolved.
	//
	// The stamp is a MONOTONIC counter, bumped once per resolved entity, NOT the resolution index.
	// Using the index looks equivalent and is not: after a pass, seen[j] holds the stamp of the
	// LAST entity that had j as a candidate, so the next pass's entity with that same index reads
	// its own stamp back and silently SKIPS a real candidate -- a collision quietly dropped every
	// few frames. Caught by eaBinTest scenario 8, which is what that suite is for.
	private int[] seen = new int[0];

	private int stampCounter;

	// One GetCollisionType() per collidable per pass, reused by the type dispatch AND the fill
	// (card 391e11d2). It was evaluated up to FOUR times per entity per pass -- once per `is` test
	// in the dispatch chain, then again inside FillCollisionMatrix* -- and every ADC evaluation
	// runs retrieveBoundsFromTexture, i.e. two ConditionalWeakTable lookups plus the cell
	// arithmetic. (The narrow phase still recomputes, through ICollidable.DetectCollision; widening
	// that signature is deliberately out of this card's scope.)
	private ICollisionType[] shapes = new ICollisionType[0];

	private const long growthReportIntervalMs = 2000;

	// Diagnostic for the instant-add lifecycle (card 02d9ad67): how many passes ended with
	// `collidables` longer than the frozen count they ran over, i.e. a collision callback
	// spawned a collidable mid-pass. That is the condition that used to index `boxes` out of
	// range, so reporting it under ?binlog lets a run PROVE the path is exercised instead of
	// only showing an absence of crashes.
	private int midPassGrowthPasses;

	private long lastGrowthReportMs;

	// Live set of registered collidables (kept in sync by the ComponentAdded/Removed
	// events below). Exposed read-only so the ?hitboxes debug overlay (HitboxOverlay,
	// drawn from Game1.DrawInner) can iterate every hitbox at present time.
	public IReadOnlyList<ICollidable> Collidables => collidables;

	public CollisionHandler(Game game)
	{
		for (int i = 0; i < 10; i++)
		{
			for (int j = 0; j < 8; j++)
			{
				fieldMatrix[i, j] = new List<int>();
			}
		}
		this.game = game;
		this.game.Components.ComponentAdded += Components_ComponentAdded;
		this.game.Components.ComponentRemoved += Components_ComponentRemoved;
	}

	private void Components_ComponentAdded(object src, GameComponentCollectionEventArgs args)
	{
		if (args.GameComponent is ICollidable)
		{
			collidables.Add((ICollidable)args.GameComponent);
		}
	}

	private void Components_ComponentRemoved(object src, GameComponentCollectionEventArgs args)
	{
		if (args.GameComponent is ICollidable)
		{
			collidables.Remove((ICollidable)args.GameComponent);
		}
	}

	// Online co-op (card 11.2): a client-side NetPuppet is deliberately Enabled=false (its
	// gameplay Update must never run) but must stay hit-testable by the local player's own
	// bullets -- that's what client-owned kill claims ARE. The override only answers true
	// while the puppet driver itself is enabled, so a paused stack (ComponentBin.Push)
	// still freezes every collision exactly like single-player. A plain boot never has
	// puppets, so this is byte-identical behaviour outside a net session.
	private static bool IsActive(ICollidable collidable)
	{
		GameComponent gc = (GameComponent)collidable;
		return gc.Enabled || EvilAliensWeb.Compat.Net.NetPuppets.CollidableOverride(gc);
	}

	// Perf batch 3 (card 391e11d2): can this entity take part in a collision AT ALL this pass?
	// An AlienDrawableGameComponent with Collides == false cannot, IN EITHER DIRECTION -- all three
	// ICollidable implementors gate on it (ADC.DetectCollision's own `if (Collides)`, and
	// Floor/Floorbottom's `!(other is ADC) || ((ADC)other).Collides`), so such an entity neither
	// hits nor is hit. Keeping it out of the broad phase is therefore behaviour-neutral, and it is
	// the bulk of a busy frame: the final boss's brainz wave runs 93 live BloodExplosions, and
	// Explosion / MiniExplosion / SmokeDrawer / FloatingText are all permanently non-colliding too.
	//
	// It is read ONCE per pass, at fill time, which is sound because Collides can only ever be
	// CLEARED from inside a collision callback, never set: every write reachable from a
	// CollidesWith/KilledBy is `= false` (Bullet, ParatrooperBrain, SpiderBoss, and the four
	// bosses' KilledBy). The `= true` writes all live in Initialize/Setup/Update, i.e. outside the
	// pass -- and a component born mid-pass joins the NEXT one anyway (the frozen count above).
	// **If you ever add a `Collides = true` inside a collision callback, this hoist stops being
	// sound** -- an entity would have to wait a frame for its first hit.
	// Non-ADC collidables (Floor, Floorbottom) have no Collides and always take part.
	private static bool CanCollide(ICollidable collidable)
	{
		return !(collidable is AlienDrawableGameComponent adc) || adc.Collides;
	}

	public void DetectCollisions()
	{
		// The whole pass runs over a FROZEN count. Bin adds are instant (card 02d9ad67), so a
		// collision callback that spawns a collidable (asteroid split, powerup drop, wall-hit
		// explosion) grows `collidables` through Components_ComponentAdded WHILE this runs.
		// Re-reading .Count mid-pass indexes `boxes` past the entries this pass sized and
		// filled (IndexOutOfRange), and the entries between the old and new count still hold
		// the previous frame's cells. A collidable born during the pass joins the NEXT one --
		// exactly what the old deferred birthList did, since it wasn't in Game.Components
		// until the flush.
		// Indexing against the frozen count is only safe because `collidables` can GROW but
		// never shrink mid-pass: removals go through ComponentBin.Remove -> deathList and are
		// flushed at the tick boundaries, and the direct Game.Components.Remove sites (scene
		// swaps, NetPuppets.Disable, the harness) are unreachable from a CollidesWith callback.
		// Keep it that way -- a mid-pass removal would shift every later index.
		int count = collidables.Count;
		for (int i = 0; i < boxes.Count && i != count; i++)
		{
			boxes[i].Clear();
		}
		while (boxes.Count < count)
		{
			boxes.Add(new List<BoxInfo>());
		}
		for (int j = 0; j < 10; j++)
		{
			for (int k = 0; k < 8; k++)
			{
				fieldMatrix[j, k].Clear();
			}
		}
		if (seen.Length < count)
		{
			seen = new int[count + 64];
		}
		if (shapes.Length < count)
		{
			shapes = new ICollisionType[count + 64];
		}
		for (int l = 0; l < count; l++)
		{
			ICollidable collidable = collidables[l];
			// Non-colliding entities are left out of the grid entirely -- see CanCollide. Their
			// `boxes[l]` stays empty (cleared above), so the resolution loop finds no cells for
			// them and no other entity ever finds them in one.
			if (!CanCollide(collidable))
			{
				continue;
			}
			ICollisionType shape = collidable.GetCollisionType();
			shapes[l] = shape;
			if (shape is CollisionBox)
			{
				FillCollisionMatrixBox(shape, boxes, l);
				continue;
			}
			if (shape is CollisionLine)
			{
				FillCollisionMatrixLine(shape, boxes, l);
				continue;
			}
			// Perf batch 2: circles (Blast/Ball/StarMine/PlasmaBall/JunkBoss) used to fall
			// through to the O(n) full-scan below, which also fired BOTH callbacks per pair —
			// so a circle-circle pair got its separation nudge applied twice per frame. Grid
			// them by their bounding box instead: the shared box/line resolution loop below
			// then handles them like every other gridded collider (one callback per direction),
			// which both removes the O(n)/O(n^2) scan and fixes the double nudge. The bounding
			// box fully covers the disc, so no overlapping pair can miss a shared cell.
			if (shape is CollisionSimpleCircle)
			{
				FillCollisionMatrixCircle(shape, boxes, l);
				continue;
			}
			// Remaining non-gridded types (CollisionMultibox / CollisionLevelMap — level walls,
			// at most one per level) keep the original all-pairs scan with both callbacks.
			for (int n = 0; n < count; n++)
			{
				ICollidable other = collidables[n];
				// The CanCollide skip is the same argument as above -- DetectCollision would
				// return false for a non-colliding `other` anyway, this just skips the call.
				if (CanCollide(other) && (IsActive(other) & IsActive(collidable)) && other != collidable && collidable.DetectCollision(other))
				{
					other.CollidesWith(collidable);
					collidable.CollidesWith(other);
				}
			}
		}
		for (int m = 0; m < count; m++)
		{
			// Monotonic, so a freshly zeroed `seen` (a grown array) and every earlier pass's
			// values are all below it -- see the field comment.
			if (stampCounter == int.MaxValue)
			{
				// Unreachable in practice (~50 hours of play at 200 collidables and 60 Hz), but a
				// wrapped counter would start matching stale entries again, so reset rather than
				// wrap: zeroing `seen` makes every stamp from 1 fresh once more.
				System.Array.Clear(seen, 0, seen.Length);
				stampCounter = 0;
			}
			int stamp = ++stampCounter;
			colliders.Clear();
			foreach (BoxInfo cell in boxes[m])
			{
				foreach (int occupant in fieldMatrix[cell.x, cell.y])
				{
					// Distinct indices are distinct components, so this is the old
					// `occupant != collidables[m]` reference test.
					if (occupant != m && seen[occupant] != stamp)
					{
						seen[occupant] = stamp;
						colliders.Add(occupant);
					}
				}
			}
			foreach (int collider in colliders)
			{
				// IsActive is re-read per candidate, exactly as before: a callback earlier in this
				// same gather can disable a component, and that must still take effect.
				if (IsActive(collidables[m]) && IsActive(collidables[collider]) && collidables[m].DetectCollision(collidables[collider]))
				{
					collidables[m].CollidesWith(collidables[collider]);
				}
			}
		}
		// Don't pin this pass's collision shapes (BrainBoss/Braineroid allocate a fresh
		// CollisionBox per access) past the pass that used them.
		System.Array.Clear(shapes, 0, count);
		if (EvilAliensWeb.Compat.DebugFlags.BinLog && collidables.Count > count)
		{
			midPassGrowthPasses++;
			long nowMs = System.Environment.TickCount64;
			if (nowMs - lastGrowthReportMs >= growthReportIntervalMs)
			{
				lastGrowthReportMs = nowMs;
				System.Console.WriteLine("[bin] " + midPassGrowthPasses
					+ " collision pass(es) held their frozen count through a mid-pass collidable add");
			}
		}
	}

	private void FillCollisionMatrixLine(ICollisionType shape, List<List<BoxInfo>> boxes, int i)
	{
		CollisionLine collisionLine = (CollisionLine)shape;
		Vector2 origin = collisionLine.Origin;
		Vector2 cursor = origin;
		float dx = collisionLine.End.X - collisionLine.Origin.X;
		float dy = collisionLine.End.Y - collisionLine.Origin.Y;
		float slope = 1f;
		float invSlope = 1f;
		if (dx != 0f)
		{
			slope = dy / dx;
		}
		if (dy != 0f)
		{
			invSlope = dx / dy;
		}
		int cellX = (int)(origin.X / 80f);
		int cellY = (int)(origin.Y / 80f);
		addToMatrix(cellX, cellY, boxes, i);
		// Guaranteed-termination backstop for the DDA below (card 7a3e70ad). A straight line crosses
		// at most squaresX + squaresY (=18) grid cells, so every well-behaved lazer steps far fewer
		// times than this. The cap only ever trips for a DEGENERATE near-axis-aligned line: a
		// (near-)perfectly vertical/horizontal lazer at a high coordinate advances cursor.X (or
		// cursor.Y) by a sub-float32-ULP amount each step, so the cursor.X-exit (or cursor.Y-exit)
		// loop can never reach End and spins forever -- a hard 100%-CPU game hang. E.g. a
		// straight-down beam at x~400:
		// End.X = 400 + len*cos(PiOver2) = 399.99997, but cursor.X stays pinned at 400.0 because each
		// step adds < 1 ULP (~6e-5 at that magnitude). The degenerate line still marks its correct
		// column/row of cells before the cap stops the spin, so broad-phase coverage is unaffected for
		// every legitimate line.
		int steps = 0;
		if (dx > 0f)
		{
			if (dy > 0f)
			{
				while (cursor.X < collisionLine.End.X && ++steps < maxLineSteps)
				{
					float dxToEdge = (float)((cellX + 1) * 80) - cursor.X;
					float dyToEdge = (float)((cellY + 1) * 80) - cursor.Y;
					float cornerSlope = dyToEdge / dxToEdge;
					if (slope > cornerSlope)
					{
						cellY++;
						cursor.Y += dyToEdge;
						cursor.X += dyToEdge * invSlope;
					}
					else
					{
						cellX++;
						cursor.X += dxToEdge;
						cursor.Y += dxToEdge * slope;
					}
					addToMatrix(cellX, cellY, boxes, i);
				}
			}
			else if (dy < 0f)
			{
				while (cursor.X < collisionLine.End.X && ++steps < maxLineSteps)
				{
					float dxToEdge = (float)((cellX + 1) * 80) - cursor.X;
					float dyToEdge = (float)(cellY * 80) - cursor.Y;
					float cornerSlope = dyToEdge / dxToEdge;
					if (slope < cornerSlope)
					{
						cellY--;
						cursor.Y += dyToEdge;
						cursor.X += dyToEdge * invSlope;
					}
					else
					{
						cellX++;
						cursor.X += dxToEdge;
						cursor.Y += dxToEdge * slope;
					}
					addToMatrix(cellX, cellY, boxes, i);
				}
			}
			else
			{
				while (cursor.X < collisionLine.End.X && ++steps < maxLineSteps)
				{
					cellX++;
					cursor.X += 80f;
					addToMatrix(cellX, cellY, boxes, i);
				}
			}
		}
		else if (dx < 0f)
		{
			if (dy > 0f)
			{
				while (cursor.X > collisionLine.End.X && ++steps < maxLineSteps)
				{
					float dxToEdge = (float)(cellX * 80) - cursor.X;
					float dyToEdge = (float)((cellY + 1) * 80) - cursor.Y;
					float cornerSlope = dyToEdge / dxToEdge;
					if (slope < cornerSlope)
					{
						cellY++;
						cursor.Y += dyToEdge;
						cursor.X += dyToEdge * invSlope;
					}
					else
					{
						cellX--;
						cursor.X += dxToEdge;
						cursor.Y += dxToEdge * slope;
					}
					addToMatrix(cellX, cellY, boxes, i);
				}
			}
			else if (dy < 0f)
			{
				while (cursor.X > collisionLine.End.X && ++steps < maxLineSteps)
				{
					float dxToEdge = (float)(cellX * 80) - cursor.X;
					float dyToEdge = (float)(cellY * 80) - cursor.Y;
					float cornerSlope = dyToEdge / dxToEdge;
					if (slope > cornerSlope)
					{
						cellY--;
						cursor.Y += dyToEdge;
						cursor.X += dyToEdge * invSlope;
					}
					else
					{
						cellX--;
						cursor.X += dxToEdge;
						cursor.Y += dxToEdge * slope;
					}
					addToMatrix(cellX, cellY, boxes, i);
				}
			}
			else
			{
				while (cursor.X > collisionLine.End.X && ++steps < maxLineSteps)
				{
					cellX--;
					cursor.X -= 80f;
					addToMatrix(cellX, cellY, boxes, i);
				}
			}
		}
		else if (dy > 0f)
		{
			while (cursor.Y < collisionLine.End.Y && ++steps < maxLineSteps)
			{
				cellY++;
				cursor.Y += 80f;
				addToMatrix(cellX, cellY, boxes, i);
			}
		}
		else if (dy < 0f)
		{
			while (cursor.Y > collisionLine.End.Y && ++steps < maxLineSteps)
			{
				cellY--;
				cursor.Y -= 80f;
				addToMatrix(cellX, cellY, boxes, i);
			}
		}
	}

	private void FillCollisionMatrixBox(ICollisionType shape, List<List<BoxInfo>> boxes, int i)
	{
		CollisionBox collisionBox = (CollisionBox)shape;
		int top = (int)(collisionBox.Top / 80f);
		int left = (int)(collisionBox.Left / 80f);
		int right = (int)(collisionBox.Right / 80f);
		int bottom = (int)(collisionBox.Bottom / 80f);
		if (left < 0)
		{
			left = 0;
		}
		if (top < 0)
		{
			top = 0;
		}
		if (bottom >= 8)
		{
			bottom = 7;
		}
		if (right >= 10)
		{
			right = 9;
		}
		for (int j = left; j < right + 1; j++)
		{
			for (int k = top; k < bottom + 1; k++)
			{
				boxes[i].Add(new BoxInfo(j, k));
				fieldMatrix[j, k].Add(i);
			}
		}
	}

	private void FillCollisionMatrixCircle(ICollisionType shape, List<List<BoxInfo>> boxes, int i)
	{
		CollisionSimpleCircle circle = (CollisionSimpleCircle)shape;
		float r = circle.Radius;
		int left = (int)((circle.Position.X - r) / 80f);
		int right = (int)((circle.Position.X + r) / 80f);
		int top = (int)((circle.Position.Y - r) / 80f);
		int bottom = (int)((circle.Position.Y + r) / 80f);
		if (left < 0)
		{
			left = 0;
		}
		if (top < 0)
		{
			top = 0;
		}
		if (right >= 10)
		{
			right = 9;
		}
		if (bottom >= 8)
		{
			bottom = 7;
		}
		for (int j = left; j < right + 1; j++)
		{
			for (int k = top; k < bottom + 1; k++)
			{
				boxes[i].Add(new BoxInfo(j, k));
				fieldMatrix[j, k].Add(i);
			}
		}
	}

	private void addToMatrix(int x, int y, List<List<BoxInfo>> boxes, int i)
	{
		int cellX = x;
		int cellY = y;
		if (cellX < 0)
		{
			cellX = 0;
		}
		if (cellX >= 10)
		{
			cellX = 9;
		}
		if (cellY < 0)
		{
			cellY = 0;
		}
		if (cellY >= 8)
		{
			cellY = 7;
		}
		if (!fieldMatrix[cellX, cellY].Contains(i))
		{
			fieldMatrix[cellX, cellY].Add(i);
			boxes[i].Add(new BoxInfo(cellX, cellY));
		}
	}

	public void DetectCollisionsOld()
	{
		for (int i = 0; i < collidables.Count - 1; i++)
		{
			for (int j = i + 1; j < collidables.Count; j++)
			{
				if (collidables[i].DetectCollision(collidables[j]))
				{
					collidables[i].CollidesWith(collidables[j]);
					collidables[j].CollidesWith(collidables[i]);
				}
			}
		}
	}
}
