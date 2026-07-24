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

	private List<ICollidable>[,] fieldMatrix = new List<ICollidable>[10, 8];

	private List<ICollidable> collidables = new List<ICollidable>();

	private Game game;

	private List<List<BoxInfo>> boxes = new List<List<BoxInfo>>();

	private List<ICollidable> colliders = new List<ICollidable>();

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
				fieldMatrix[i, j] = new List<ICollidable>();
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
		for (int l = 0; l < count; l++)
		{
			ICollidable collidable = collidables[l];
			if (collidable.GetCollisionType() is CollisionBox)
			{
				FillCollisionMatrixBox(collidable, boxes, l);
				continue;
			}
			if (collidable.GetCollisionType() is CollisionLine)
			{
				FillCollisionMatrixLine(collidable, boxes, l);
				continue;
			}
			// Perf batch 2: circles (Blast/Ball/StarMine/PlasmaBall/JunkBoss) used to fall
			// through to the O(n) full-scan below, which also fired BOTH callbacks per pair —
			// so a circle-circle pair got its separation nudge applied twice per frame. Grid
			// them by their bounding box instead: the shared box/line resolution loop below
			// then handles them like every other gridded collider (one callback per direction),
			// which both removes the O(n)/O(n^2) scan and fixes the double nudge. The bounding
			// box fully covers the disc, so no overlapping pair can miss a shared cell.
			if (collidable.GetCollisionType() is CollisionSimpleCircle)
			{
				FillCollisionMatrixCircle(collidable, boxes, l);
				continue;
			}
			// Remaining non-gridded types (CollisionMultibox / CollisionLevelMap — level walls,
			// at most one per level) keep the original all-pairs scan with both callbacks.
			for (int n = 0; n < count; n++)
			{
				ICollidable collidable2 = collidables[n];
				if ((IsActive(collidable2) & IsActive(collidable)) && collidable2 != collidable && collidable.DetectCollision(collidable2))
				{
					collidable2.CollidesWith(collidable);
					collidable.CollidesWith(collidable2);
				}
			}
		}
		for (int m = 0; m < count; m++)
		{
			colliders.Clear();
			foreach (BoxInfo cell in boxes[m])
			{
				foreach (ICollidable occupant in fieldMatrix[cell.x, cell.y])
				{
					if (!colliders.Contains(occupant) && occupant != collidables[m])
					{
						colliders.Add(occupant);
					}
				}
			}
			foreach (ICollidable collider in colliders)
			{
				if (IsActive(collidables[m]) && IsActive(collider) && collidables[m].DetectCollision(collider))
				{
					collidables[m].CollidesWith(collider);
				}
			}
		}
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

	private void FillCollisionMatrixLine(ICollidable collidable, List<List<BoxInfo>> boxes, int i)
	{
		CollisionLine collisionLine = (CollisionLine)collidable.GetCollisionType();
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
		addToMatrix(collidable, cellX, cellY, boxes, i);
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
					addToMatrix(collidable, cellX, cellY, boxes, i);
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
					addToMatrix(collidable, cellX, cellY, boxes, i);
				}
			}
			else
			{
				while (cursor.X < collisionLine.End.X && ++steps < maxLineSteps)
				{
					cellX++;
					cursor.X += 80f;
					addToMatrix(collidable, cellX, cellY, boxes, i);
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
					addToMatrix(collidable, cellX, cellY, boxes, i);
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
					addToMatrix(collidable, cellX, cellY, boxes, i);
				}
			}
			else
			{
				while (cursor.X > collisionLine.End.X && ++steps < maxLineSteps)
				{
					cellX--;
					cursor.X -= 80f;
					addToMatrix(collidable, cellX, cellY, boxes, i);
				}
			}
		}
		else if (dy > 0f)
		{
			while (cursor.Y < collisionLine.End.Y && ++steps < maxLineSteps)
			{
				cellY++;
				cursor.Y += 80f;
				addToMatrix(collidable, cellX, cellY, boxes, i);
			}
		}
		else if (dy < 0f)
		{
			while (cursor.Y > collisionLine.End.Y && ++steps < maxLineSteps)
			{
				cellY--;
				cursor.Y -= 80f;
				addToMatrix(collidable, cellX, cellY, boxes, i);
			}
		}
	}

	private void FillCollisionMatrixBox(ICollidable collidable, List<List<BoxInfo>> boxes, int i)
	{
		CollisionBox collisionBox = (CollisionBox)collidable.GetCollisionType();
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
				fieldMatrix[j, k].Add(collidable);
			}
		}
	}

	private void FillCollisionMatrixCircle(ICollidable collidable, List<List<BoxInfo>> boxes, int i)
	{
		CollisionSimpleCircle circle = (CollisionSimpleCircle)collidable.GetCollisionType();
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
				fieldMatrix[j, k].Add(collidable);
			}
		}
	}

	private void addToMatrix(ICollidable collidable, int x, int y, List<List<BoxInfo>> boxes, int i)
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
		if (!fieldMatrix[cellX, cellY].Contains(collidable))
		{
			fieldMatrix[cellX, cellY].Add(collidable);
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
