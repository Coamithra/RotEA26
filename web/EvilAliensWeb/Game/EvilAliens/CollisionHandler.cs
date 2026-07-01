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

	private List<ICollidable>[,] fieldMatrix = new List<ICollidable>[10, 8];

	private List<ICollidable> collidables = new List<ICollidable>();

	private Game game;

	private List<List<BoxInfo>> boxes = new List<List<BoxInfo>>();

	private List<ICollidable> colliders = new List<ICollidable>();

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

	public void DetectCollisions()
	{
		//IL_00f6: Unknown result type (might be due to invalid IL or missing references)
		//IL_0102: Unknown result type (might be due to invalid IL or missing references)
		//IL_0247: Unknown result type (might be due to invalid IL or missing references)
		//IL_0255: Unknown result type (might be due to invalid IL or missing references)
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
		for (int l = 0; l < collidables.Count; l++)
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
			foreach (ICollidable collidable2 in collidables)
			{
				if ((((GameComponent)collidable2).Enabled & ((GameComponent)collidable).Enabled) && collidable2 != collidable && collidable.DetectCollision(collidable2))
				{
					collidable2.CollidesWith(collidable);
					collidable.CollidesWith(collidable2);
				}
			}
		}
		for (int m = 0; m < collidables.Count; m++)
		{
			colliders.Clear();
			foreach (BoxInfo item in boxes[m])
			{
				foreach (ICollidable item2 in fieldMatrix[item.x, item.y])
				{
					if (!colliders.Contains(item2) && item2 != collidables[m])
					{
						colliders.Add(item2);
					}
				}
			}
			foreach (ICollidable collider in colliders)
			{
				if (((GameComponent)collidables[m]).Enabled && ((GameComponent)collider).Enabled && collidables[m].DetectCollision(collider))
				{
					collidables[m].CollidesWith(collider);
				}
			}
		}
	}

	private void FillCollisionMatrixLine(ICollidable collidable, List<List<BoxInfo>> boxes, int i)
	{
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		//IL_024b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0154: Unknown result type (might be due to invalid IL or missing references)
		//IL_0447: Unknown result type (might be due to invalid IL or missing references)
		//IL_0401: Unknown result type (might be due to invalid IL or missing references)
		//IL_030c: Unknown result type (might be due to invalid IL or missing references)
		//IL_020d: Unknown result type (might be due to invalid IL or missing references)
		//IL_048d: Unknown result type (might be due to invalid IL or missing references)
		//IL_03c3: Unknown result type (might be due to invalid IL or missing references)
		CollisionLine collisionLine = (CollisionLine)collidable.GetCollisionType();
		Vector2 origin = collisionLine.Origin;
		Vector2 val = origin;
		float num = collisionLine.End.X - collisionLine.Origin.X;
		float num2 = collisionLine.End.Y - collisionLine.Origin.Y;
		float num3 = 1f;
		float num4 = 1f;
		if (num != 0f)
		{
			num3 = num2 / num;
		}
		if (num2 != 0f)
		{
			num4 = num / num2;
		}
		int num5 = (int)(origin.X / 80f);
		int num6 = (int)(origin.Y / 80f);
		addToMatrix(collidable, num5, num6, boxes, i);
		if (num > 0f)
		{
			if (num2 > 0f)
			{
				while (val.X < collisionLine.End.X)
				{
					float num7 = (float)((num5 + 1) * 80) - val.X;
					float num8 = (float)((num6 + 1) * 80) - val.Y;
					float num9 = num8 / num7;
					if (num3 > num9)
					{
						num6++;
						val.Y += num8;
						val.X += num8 * num4;
					}
					else
					{
						num5++;
						val.X += num7;
						val.Y += num7 * num3;
					}
					addToMatrix(collidable, num5, num6, boxes, i);
				}
			}
			else if (num2 < 0f)
			{
				while (val.X < collisionLine.End.X)
				{
					float num10 = (float)((num5 + 1) * 80) - val.X;
					float num11 = (float)(num6 * 80) - val.Y;
					float num12 = num11 / num10;
					if (num3 < num12)
					{
						num6--;
						val.Y += num11;
						val.X += num11 * num4;
					}
					else
					{
						num5++;
						val.X += num10;
						val.Y += num10 * num3;
					}
					addToMatrix(collidable, num5, num6, boxes, i);
				}
			}
			else
			{
				while (val.X < collisionLine.End.X)
				{
					num5++;
					val.X += 80f;
					addToMatrix(collidable, num5, num6, boxes, i);
				}
			}
		}
		else if (num < 0f)
		{
			if (num2 > 0f)
			{
				while (val.X > collisionLine.End.X)
				{
					float num13 = (float)(num5 * 80) - val.X;
					float num14 = (float)((num6 + 1) * 80) - val.Y;
					float num15 = num14 / num13;
					if (num3 < num15)
					{
						num6++;
						val.Y += num14;
						val.X += num14 * num4;
					}
					else
					{
						num5--;
						val.X += num13;
						val.Y += num13 * num3;
					}
					addToMatrix(collidable, num5, num6, boxes, i);
				}
			}
			else if (num2 < 0f)
			{
				while (val.X > collisionLine.End.X)
				{
					float num16 = (float)(num5 * 80) - val.X;
					float num17 = (float)(num6 * 80) - val.Y;
					float num18 = num17 / num16;
					if (num3 > num18)
					{
						num6--;
						val.Y += num17;
						val.X += num17 * num4;
					}
					else
					{
						num5--;
						val.X += num16;
						val.Y += num16 * num3;
					}
					addToMatrix(collidable, num5, num6, boxes, i);
				}
			}
			else
			{
				while (val.X > collisionLine.End.X)
				{
					num5--;
					val.X -= 80f;
					addToMatrix(collidable, num5, num6, boxes, i);
				}
			}
		}
		else if (num2 > 0f)
		{
			while (val.Y < collisionLine.End.Y)
			{
				num6++;
				val.Y += 80f;
				addToMatrix(collidable, num5, num6, boxes, i);
			}
		}
		else if (num2 < 0f)
		{
			while (val.Y > collisionLine.End.Y)
			{
				num6--;
				val.Y -= 80f;
				addToMatrix(collidable, num5, num6, boxes, i);
			}
		}
	}

	private void FillCollisionMatrixBox(ICollidable collidable, List<List<BoxInfo>> boxes, int i)
	{
		CollisionBox collisionBox = (CollisionBox)collidable.GetCollisionType();
		int num = (int)(collisionBox.Top / 80f);
		int num2 = (int)(collisionBox.Left / 80f);
		int num3 = (int)(collisionBox.Right / 80f);
		int num4 = (int)(collisionBox.Bottom / 80f);
		if (num2 < 0)
		{
			num2 = 0;
		}
		if (num < 0)
		{
			num = 0;
		}
		if (num4 >= 8)
		{
			num4 = 7;
		}
		if (num3 >= 10)
		{
			num3 = 9;
		}
		for (int j = num2; j < num3 + 1; j++)
		{
			for (int k = num; k < num4 + 1; k++)
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
		int num = x;
		int num2 = y;
		if (num < 0)
		{
			num = 0;
		}
		if (num >= 10)
		{
			num = 9;
		}
		if (num2 < 0)
		{
			num2 = 0;
		}
		if (num2 >= 8)
		{
			num2 = 7;
		}
		if (!fieldMatrix[num, num2].Contains(collidable))
		{
			fieldMatrix[num, num2].Add(collidable);
			boxes[i].Add(new BoxInfo(num, num2));
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
