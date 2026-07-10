using System;
using System.Collections.Generic;
using System.IO;
using EvilAliens.Constants;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

internal class Wall : AlienDrawableGameComponent
{
	// --- 3D-tower extrusion (Trello d59266cc, plans/walls-3d-towers.md) -------------------
	// Each collidable block is extruded DOWNWARD into a shaft standing on the alien-base
	// ground, so the walls read as towers rising out of the fog rather than flat floating
	// tiles. The gameplay plane (where the ship flies and collides) stays the tower TOPS --
	// nothing below them is collidable, and the top-face pass is byte-identical to before.
	//
	// The projection is the classic top-down fake perspective: a vanishing point at the
	// screen centre, and a block's base rect = VP + (topRect - VP) * DefaultDepth. The depth
	// factor is 0.66 because that is EXACTLY the alien-base ground layer's scrollspeedmodifier
	// (Background.SetAlienBase) -- so a base projected at 0.66 moves at 0.66x the wall's speed,
	// which is the floor's speed, and the towers stay glued to the scrolling ground for free.
	// Change DefaultDepth and the bases slide against the floor.
	//
	// Shafts are drawn as stacked sprite slices (GTA1 voxel-slice style), NOT real 3D quads --
	// per-quad DrawUserIndexedPrimitives forces a batch flush and is brutal on WebGL/WASM (the
	// Quad.cs lesson). Everything below stays inside the one existing SpriteBatch.
	private const float VanishX = 400f;

	private const float VanishY = 300f;

	internal const float DefaultDepth = 0.66f;

	// Design px of corner "lean" per slice. The lean is (1 - depth) * |corner - VP|, so it is
	// ~0 at the screen centre and ~170px at the far corners; the slice count is derived from the
	// worst lean on screen rather than fixed, so centre blocks don't pay for edge blocks. Above
	// ~8px the shrinking slice rects stop overlapping cleanly and the shaft bands into vertical
	// strips with a staircased bottom edge; 5 is comfortably clear of that.
	internal const float DefaultSliceStep = 5f;

	private const int MaxSlices = 64;

	// How far a slice's tint lerps toward the haze colour at the base (1 = fully fogged).
	internal const float DefaultFog = 1f;

	// The haze tint a shaft leans toward at its base. NOTE this is a MULTIPLICATIVE sprite tint,
	// so it can only scale the wall texture -- it can never paint a slice up to a literal fog
	// colour. The alien-base floor composites bright blue (756 plus its two additive 2331-v5
	// layers averages RGB(46,125,199)), so the convincing cue is for the shaft to BRIGHTEN and
	// desaturate as it descends and then alpha-dissolve into that floor. Hence a high-value
	// blue-white here, above DefaultSideDark rather than below it: the lit-but-shadowed upper
	// shaft is the darkest part, the fogged base the palest.
	private static readonly Color DefaultFogColor = new Color(158, 199, 242);

	// Brightness of the shaft's topmost slice (1 = as bright as the top face). Slice sides are
	// darker than the lit top, as they would be under a top-down light.
	internal const float DefaultSideDark = 0.55f;

	// Alpha of the additive fog wisps drawn ACROSS the shafts (0 = off) and their scroll speed
	// relative to the wall. 0.8 matches the near fog background layer, which sits inside the
	// shaft's 0.66..1.0 depth band -- so the wisps parallax correctly against the slices.
	internal const float DefaultWispAlpha = 0.15f;

	internal const float DefaultWispSpeed = 0.8f;

	// Bottom fraction of the shaft that alpha-dissolves into the fog. Slices are otherwise drawn
	// OPAQUE: consecutive slices overlap heavily, so a stack of translucent ones would accumulate
	// back to opaque and defeat the dissolve. Lerping the tint to the haze colour does the work.
	private const float DissolveFraction = 0.18f;

	// Visible-block count at which the wisps reach full alpha. The wisp pass is screen-wide, but
	// a Wall spawns and dies per section (Walls.cs), so gating the alpha on how many blocks are
	// actually on screen keeps the haze from popping in with the entity -- and lets it relax
	// through the empty rows between tower clusters.
	private const float WispFadeBlocks = 6f;

	// The same fog texture the two additive alien-base background layers use, so the wisps read
	// as more of that fog rather than a new element. Loaded in LoadContent (a cache hit -- the
	// background decoded it long before the first wall spawns).
	private Texture2D fog;

	// Low-frequency companion to the wall sheet: the same 8x8 grid, each cell area-averaged down
	// (tools/walls/build_wall_side.py). Slicing the full-res cell for every slice makes the shaft
	// corduroy -- consecutive slices re-draw the same high-frequency detail at slightly different
	// scales, so the sliver each one leaves exposed repeats it instead of smearing into a face.
	// One texture switch per frame (side -> wall), so the slices batch separately from the tops.
	private Texture2D side;

	private bool[,] blocks;

	private Texture2D line;

	private CollisionLevelMap collisionMap;

	private int width => blocks.GetLength(1);

	private int height => blocks.GetLength(0);

	public override ICollisionType CollisionType
	{
		get
		{
			//IL_0028: Unknown result type (might be due to invalid IL or missing references)
			//IL_000a: Unknown result type (might be due to invalid IL or missing references)
			if (collisionMap == null)
			{
				collisionMap = new CollisionLevelMap(base.Position, blocks);
			}
			else
			{
				collisionMap.SetOffset(base.Position);
			}
			return collisionMap;
		}
	}

	protected override void LoadContent()
	{
		base.LoadContent();
		line = content.Load<Texture2D>("GFX/Base/black line lalalal");
		fog = content.Load<Texture2D>("GFX/Base/2331-v5");
		side = content.Load<Texture2D>("GFX/Base/756-v1-side");
	}

	public Wall(Game game)
		: base(game)
	{
		LoadAnimation(new AnimationData("GFX/Base/756-v1"));
		base.DrawOrder = 1;
	}

	public static Wall NewWall(ComponentBin collection, Game game)
	{
		Wall wall = collection.Recycle<Wall>();
		if (wall == null)
		{
			wall = new Wall(game);
		}
		return wall;
	}

	public void Setup(int variation)
	{
		//IL_0262: Unknown result type (might be due to invalid IL or missing references)
		//IL_027e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0283: Unknown result type (might be due to invalid IL or missing references)
		collisionMap = null;
		switch (variation)
		{
		case 0:
			blocks = new bool[122, 12]
			{
				{
					false, false, false, false, true, true, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, true, false, false, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, false, false, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, false, false, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, false, false, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, false, false, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, false, false, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, false, false, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, false, false, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, false, false, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, false, false, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, false, false, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, false, false, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, false, false, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, false, false, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, false, false, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, false, false, true, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, true, true, true, true, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				},
				{
					false, false, false, false, false, false, false, false, false, false,
					false, false
				}
			};
			break;
		case 1:
			blocks = new bool[106, 7]
			{
				{ true, true, true, false, true, true, true },
				{ true, true, true, false, true, true, true },
				{ true, true, true, false, true, true, true },
				{ true, false, false, false, false, false, true },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, true, true, true, false, false },
				{ false, false, true, true, true, false, false },
				{ false, false, true, true, true, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ true, false, false, false, false, false, true },
				{ true, true, false, false, false, true, true },
				{ true, true, false, false, false, true, true },
				{ true, true, false, false, false, true, true },
				{ true, false, false, false, false, false, false },
				{ true, false, false, false, false, false, false },
				{ true, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, true, true, true },
				{ false, false, false, false, true, true, true },
				{ false, false, false, false, true, true, true },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ true, true, true, false, false, false, false },
				{ true, true, true, false, false, false, false },
				{ true, true, true, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ true, false, false, true, false, false, true },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, true, false, false, true, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, true, false, false, true, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ true, false, false, true, false, false, true },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, true, false, false, true, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, true, false, false, true, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ true, false, false, false, false, false, true },
				{ true, false, false, false, false, false, true },
				{ true, true, false, false, false, true, true },
				{ true, true, true, false, true, true, true },
				{ true, true, false, false, false, true, true },
				{ true, true, false, false, false, true, true },
				{ true, true, false, false, false, true, true },
				{ true, true, false, true, false, true, true },
				{ true, true, false, true, false, true, true },
				{ true, false, false, true, false, true, true },
				{ true, false, true, true, false, true, true },
				{ true, false, true, true, false, true, true },
				{ true, false, true, false, false, false, true },
				{ true, false, true, false, false, false, true },
				{ true, false, true, false, true, false, true },
				{ true, false, true, false, true, false, true },
				{ true, false, false, false, false, false, true },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ true, true, true, true, false, false, false },
				{ true, true, true, true, false, false, false },
				{ true, true, true, true, false, false, false },
				{ true, true, true, true, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, true, true, true, true },
				{ false, false, false, true, true, true, true },
				{ false, false, false, true, true, true, true },
				{ false, false, false, true, true, true, true },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ true, true, true, false, false, false, false },
				{ true, true, true, false, false, false, false },
				{ true, true, true, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, true, true, true },
				{ false, false, false, false, true, true, true },
				{ false, false, false, false, true, true, true },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false }
			};
			break;
		case 2:
			try
			{
				List<string> list = new List<string>();
				int num;
				using (StreamReader streamReader = new StreamReader(General.Path + "Levels/level3.txt"))
				{
					string text = streamReader.ReadLine();
					num = Convert.ToInt32(text.Remove(0, 6));
					while (true)
					{
						text = streamReader.ReadLine();
						if (text != null && !text.Contains("end"))
						{
							list.Add(text);
							continue;
						}
						break;
					}
				}
				blocks = new bool[list.Count, num];
				for (int i = 0; i < list.Count; i++)
				{
					for (int j = 0; j < num; j++)
					{
						if (j >= list[i].Length || list[i][j] == ' ')
						{
							blocks[i, j] = false;
						}
						else
						{
							blocks[i, j] = true;
						}
					}
				}
			}
			catch (Exception)
			{
				blocks = new bool[5, 19]
				{
					{
						true, true, true, false, true, true, false, false, true, true,
						false, false, false, true, false, false, true, true, false
					},
					{
						true, false, false, false, true, false, true, false, true, false,
						true, false, true, false, true, false, true, false, true
					},
					{
						true, true, true, false, true, true, false, false, true, true,
						false, false, true, false, true, false, true, true, false
					},
					{
						true, false, false, false, true, false, true, false, true, false,
						true, false, true, false, true, false, true, false, true
					},
					{
						true, true, true, false, true, false, true, false, true, false,
						true, false, false, true, false, false, true, false, true
					}
				};
			}
			break;
		case 3:
			blocks = new bool[179, 9]
			{
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, true, true, true, false, true, false, false },
				{ false, false, true, false, false, false, true, false, false },
				{ false, false, true, false, false, false, true, false, false },
				{ false, false, true, false, false, false, true, false, false },
				{ false, false, true, false, false, false, true, false, false },
				{ false, false, true, false, false, false, true, false, false },
				{ false, false, true, false, false, false, true, false, false },
				{ false, false, true, false, false, false, true, false, false },
				{ false, false, true, false, true, true, true, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, true, true, true, true, true, true, true },
				{ false, false, false, false, true, true, true, true, true },
				{ false, false, false, false, false, false, true, true, true },
				{ false, false, false, false, false, false, false, false, true },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ true, true, true, true, true, true, true, false, false },
				{ true, true, true, true, true, false, false, false, false },
				{ true, true, true, false, false, false, false, false, false },
				{ true, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, true, true, true, true, true, true, true },
				{ false, false, false, true, true, true, true, true, true },
				{ false, false, false, false, true, true, true, true, true },
				{ false, false, false, false, false, true, true, true, true },
				{ false, false, false, false, false, false, true, true, true },
				{ false, false, false, false, false, false, false, true, true },
				{ false, false, false, false, false, false, false, false, true },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ true, true, true, true, true, true, false, false, false },
				{ true, true, true, true, true, false, false, false, false },
				{ true, true, true, true, false, false, false, false, false },
				{ true, true, true, false, false, false, false, false, false },
				{ true, true, false, false, false, false, false, false, false },
				{ true, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, true, true, true, false, false, false },
				{ false, false, true, true, true, true, true, false, false },
				{ false, false, true, true, true, true, true, false, false },
				{ false, false, false, true, true, true, false, false, false },
				{ false, false, false, false, true, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ true, false, false, false, false, false, false, false, true },
				{ true, true, false, false, false, false, false, true, true },
				{ true, false, false, false, false, false, false, false, true },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, true, false, false, false, false },
				{ false, false, false, true, true, true, false, false, false },
				{ false, false, true, true, true, true, true, false, false },
				{ false, false, true, true, true, true, true, false, false },
				{ false, false, false, true, true, true, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, true, false, false, false, false },
				{ false, false, false, true, true, true, false, false, false },
				{ false, false, true, true, true, true, true, false, false },
				{ false, false, true, true, true, true, true, false, false },
				{ false, false, false, true, true, true, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ true, false, false, false, false, false, false, false, true },
				{ true, false, false, false, false, false, false, false, true },
				{ true, false, false, false, false, false, false, false, true },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, true, true, true, false, false, false },
				{ false, false, true, true, true, true, true, false, false },
				{ false, false, true, true, true, true, true, false, false },
				{ false, false, false, true, true, true, false, false, false },
				{ false, false, false, false, true, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, true, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, true, false, false, false, false, false, true },
				{ false, false, false, false, false, true, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ true, false, false, false, false, false, true, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, true, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, true, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, true, false },
				{ false, false, false, true, false, false, false, false, false },
				{ false, false, false, false, false, false, true, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, true, false, false, false, false, false, false },
				{ true, false, false, false, false, false, false, true, false },
				{ false, false, false, false, true, false, false, false, false },
				{ false, false, false, false, false, false, true, false, false },
				{ false, true, false, false, false, false, false, false, true },
				{ false, false, false, true, false, false, false, false, false },
				{ false, false, false, false, false, true, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ true, false, false, false, false, false, true, false, false },
				{ false, false, false, true, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, true },
				{ false, false, false, false, false, true, false, false, false },
				{ false, false, true, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, true, false, false },
				{ false, true, false, false, false, false, false, false, true },
				{ false, false, false, false, true, false, false, false, false },
				{ false, false, true, false, false, false, false, false, false },
				{ true, false, false, false, false, false, false, true, false },
				{ false, false, false, false, false, true, false, false, false },
				{ false, false, false, true, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, true, false, false, false, false, false, true },
				{ false, false, false, false, false, true, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ true, false, false, false, false, false, true, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, true, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, true, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, true, false },
				{ false, false, false, true, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ true, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, true, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, true, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, false, false },
				{ false, false, false, false, false, false, false, true, false }
			};
			break;
		case 4:
			blocks = new bool[11, 3]
			{
				{ false, false, true },
				{ false, false, false },
				{ true, false, false },
				{ false, false, false },
				{ false, false, true },
				{ false, false, false },
				{ true, false, false },
				{ false, false, false },
				{ false, false, true },
				{ false, false, false },
				{ true, false, false }
			};
			break;
		default:
			throw new Exception("illegal wall variation specified " + variation);
		}
		if (Settings.GetInstance().CurrentDifficulty <= Settings.DifficultyLevel.Medium)
		{
			int num2 = height / 2;
			bool[,] array = new bool[num2, width];
			for (int k = 0; k < num2; k++)
			{
				for (int l = 0; l < width; l++)
				{
					array[k, l] = blocks[k, l];
				}
			}
			blocks = array;
		}
		scale = 800f / (float)(texture.Width * width);
		float num3 = (float)texture.Height * scale;
		base.Position = new Vector2(0f, (0f - num3) * (float)height);
		base.Direction = (float)Math.PI / 2f;
		Vector2 backgroundSpeed = oracle.BackgroundSpeed;
		base.Speed = (backgroundSpeed).Length() * 1f;
	}

	public override void Initialize()
	{
		base.Initialize();
	}

	private bool isfree(int x, int y)
	{
		if (x < 0 || x >= width)
		{
			return false;
		}
		if (y < 0 || y >= height)
		{
			return true;
		}
		return !blocks[y, x];
	}

	// Project a point on the gameplay plane (a top-face corner) down to depth `d`. d == 1 is the
	// top face itself; d == DefaultDepth lands on the alien-base ground far below.
	private static Vector2 Project(Vector2 top, float d)
	{
		return new Vector2(VanishX + (top.X - VanishX) * d, VanishY + (top.Y - VanishY) * d);
	}

	// Is any part of block row `i`'s shaft -- top face down to projected base -- on screen? A
	// block whose TOP face has scrolled off the bottom still shows its base (the base projects
	// toward the VP, i.e. upward, when the block is below the VP), and a block still above the
	// screen already shows its base below the top edge. That second case is the "towers rise
	// base-first out of the fog on entry" effect, so this cull must be wider than the top-face
	// loop's -- which is deliberately left alone.
	private bool RowShaftVisible(float topY, float blockH, float depth)
	{
		float baseY = VanishY + (topY - VanishY) * depth;
		float lo = Math.Min(topY, baseY);
		float hi = Math.Max(topY + blockH, baseY + blockH * depth);
		return hi > 0f && lo < 600f;
	}

	// Stacked-slice extrusion. Returns the number of blocks whose shaft was drawn (the wisp pass
	// fades on that count). Draws slice depth k for EVERY block before depth k+1: at one depth all
	// footprints are the same affine scaling of disjoint rects about the VP, so they stay disjoint
	// and painter's order across the whole wall is correct. Per-block slice ladders would NOT be
	// safe -- a tall shaft can lean over a block nearer the VP, and only a shared depth ordering
	// guarantees the nearer slice lands last.
	private int DrawTowerShafts()
	{
		float depth = EvilAliensWeb.Compat.DebugFlags.WallDepth ?? DefaultDepth;
		float step = EvilAliensWeb.Compat.DebugFlags.WallSliceStep ?? DefaultSliceStep;
		float fogAmount = EvilAliensWeb.Compat.DebugFlags.WallFog ?? DefaultFog;
		float sideDark = EvilAliensWeb.Compat.DebugFlags.WallSideDark ?? DefaultSideDark;
		Color fogColorFlag = EvilAliensWeb.Compat.DebugFlags.WallFogColor ?? DefaultFogColor;
		float blockW = (float)texture.Width * scale;
		float blockH = (float)texture.Height * scale;
		// One pass to find the worst corner lean on screen; the slice count follows from it, so a
		// wall sitting near the VP (all shafts foreshortened to nothing) costs almost no draws.
		float maxLean = 0f;
		int visibleBlocks = 0;
		for (int i = 0; i < height; i++)
		{
			float topY = blockH * (float)i + base.Position.Y;
			if (!RowShaftVisible(topY, blockH, depth))
			{
				continue;
			}
			for (int j = 0; j < width; j++)
			{
				if (!blocks[i, j])
				{
					continue;
				}
				visibleBlocks++;
				float x = blockW * (float)j + base.Position.X;
				float dx = Math.Max(Math.Abs(x - VanishX), Math.Abs(x + blockW - VanishX));
				float dy = Math.Max(Math.Abs(topY - VanishY), Math.Abs(topY + blockH - VanishY));
				maxLean = Math.Max(maxLean, (1f - depth) * (float)Math.Sqrt(dx * dx + dy * dy));
			}
		}
		if (visibleBlocks == 0)
		{
			return 0;
		}
		int slices = (int)MathHelper.Clamp((float)Math.Ceiling(maxLean / step), 1f, MaxSlices);
		Vector3 sideColor = Vector3.One * sideDark;
		Vector3 fogColor = fogColorFlag.ToVector3();
		// The side sheet mirrors the wall sheet's 8x8 grid at whatever resolution it was built
		// at, so derive the cell pitch rather than hard-coding it.
		int sideCell = side.Width / 8;
		for (int k = 0; k < slices; k++)
		{
			// t: 0 at the base, -> 1 at the top face (never reaching it -- the real top face is
			// drawn after and covers the last step's worth of seam).
			float t = (float)k / (float)slices;
			float d = depth + (1f - depth) * t;
			Vector3 rgb = Vector3.Lerp(sideColor, fogColor, MathHelper.Clamp(fogAmount * (1f - t), 0f, 1f));
			float alpha = (t < DissolveFraction) ? MathHelper.SmoothStep(0f, 1f, t / DissolveFraction) : 1f;
			Color tint = new Color(new Vector4(rgb, alpha));
			// A slice covers the same on-screen area as the block's top face, shrunk by d. Scaled
			// per-axis off the block size rather than by one factor, so a non-square wall sheet
			// (blockW != blockH) can't silently squash the shafts.
			Vector2 sliceScale = new Vector2(blockW * d / (float)sideCell, blockH * d / (float)sideCell);
			for (int i = 0; i < height; i++)
			{
				float topY = blockH * (float)i + base.Position.Y;
				if (!RowShaftVisible(topY, blockH, depth))
				{
					continue;
				}
				for (int j = 0; j < width; j++)
				{
					if (!blocks[i, j])
					{
						continue;
					}
					Vector2 top = new Vector2(blockW * (float)j + base.Position.X, topY);
					spriteBatch.Draw(side, new Rectangle(j % 8 * sideCell, i % 8 * sideCell, sideCell, sideCell), Project(top, d), 0f, sliceScale, Vector2.Zero, tint);
				}
			}
		}
		return visibleBlocks;
	}

	// Drifting fog wisps ACROSS the shafts: background fog -> tower shafts -> these -> crisp top
	// faces. Additive can only brighten, never truly occlude -- but fog in front of a dark object
	// IS a brightening, the shafts are darkest exactly where the wisps cross them, and the bright
	// top faces draw AFTER so they never get hazed. It also matches how 2331-v5 is already used by
	// the two additive background layers. (NonPremultiplied is not an option here: the texture's
	// alpha channel is fully opaque, so it would paint a hard rectangle.)
	private void DrawFogWisps(int visibleBlocks)
	{
		float alpha = (EvilAliensWeb.Compat.DebugFlags.WallWisps ?? DefaultWispAlpha)
			* MathHelper.Clamp((float)visibleBlocks / WispFadeBlocks, 0f, 1f);
		if (alpha <= 0.001f)
		{
			return;
		}
		float speed = EvilAliensWeb.Compat.DebugFlags.WallWispSpeed ?? DefaultWispSpeed;
		// Tile by POSITION, never by a drifting source rect: the batch begins with a null
		// samplerState (LinearClamp), so an out-of-bounds source window clamps instead of
		// wrapping. Same tiling loop BackgroundImage.Draw uses. base.Position.Y is the integral of
		// the wall's scroll speed (Wall.Update sets Speed = |oracle.BackgroundSpeed|, unmodified),
		// so scaling it by `speed` yields exactly the screen motion of a background layer with
		// that scrollspeedmodifier -- no persistent scroll state needed.
		float tile = (float)fog.Width;
		float phaseY = MyMath.Mod(base.Position.Y * speed, tile);
		Color tint = new Color(new Vector4(1f, 1f, 1f, alpha));
		spriteBatch.BlendMode = (SpriteBlendMode)2;
		for (float y = phaseY - tile; y < 600f; y += tile)
		{
			for (float x = 0f; x < 800f; x += tile)
			{
				spriteBatch.Draw(fog, new Vector2(x, y), 0f, 1f, center: false, tint);
			}
		}
		spriteBatch.BlendMode = (SpriteBlendMode)1;
	}

	public override void Draw(GameTime gameTime)
	{
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0065: Unknown result type (might be due to invalid IL or missing references)
		//IL_0095: Unknown result type (might be due to invalid IL or missing references)
		//IL_0126: Unknown result type (might be due to invalid IL or missing references)
		//IL_012b: Unknown result type (might be due to invalid IL or missing references)
		//IL_012d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0132: Unknown result type (might be due to invalid IL or missing references)
		//IL_016e: Unknown result type (might be due to invalid IL or missing references)
		//IL_016f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0171: Unknown result type (might be due to invalid IL or missing references)
		//IL_0176: Unknown result type (might be due to invalid IL or missing references)
		//IL_018d: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ad: Unknown result type (might be due to invalid IL or missing references)
		//IL_01cf: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d1: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d6: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ed: Unknown result type (might be due to invalid IL or missing references)
		//IL_020c: Unknown result type (might be due to invalid IL or missing references)
		//IL_020e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0213: Unknown result type (might be due to invalid IL or missing references)
		//IL_022a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0249: Unknown result type (might be due to invalid IL or missing references)
		//IL_024b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0250: Unknown result type (might be due to invalid IL or missing references)
		//IL_0267: Unknown result type (might be due to invalid IL or missing references)
		//IL_0286: Unknown result type (might be due to invalid IL or missing references)
		//IL_0288: Unknown result type (might be due to invalid IL or missing references)
		//IL_028d: Unknown result type (might be due to invalid IL or missing references)
		//IL_02a4: Unknown result type (might be due to invalid IL or missing references)
		spriteBatch.BlendMode = (SpriteBlendMode)1;
		// The towers live entirely BELOW the top faces, so they go in first. ?walltowers=0 skips
		// both passes and the rest of this method reproduces the original flat look exactly.
		if (EvilAliensWeb.Compat.DebugFlags.WallTowers)
		{
			DrawFogWisps(DrawTowerShafts());
		}
		Vector2 val2 = default(Vector2);
		Color val3 = default(Color);
		Color val4 = default(Color);
		// Edge-line draw scale (card a54cc13a): `line` ("black line lalalal") is a SEPARATE, fixed-
		// resolution texture -- a thin line inset near the right edge of its own square canvas, not
		// part of the 8x8 wall sheet -- drawn `center:true` at each wall block's centre so it reaches
		// out to the block's true edge. The on-screen block size is `texture.Width * scale` regardless
		// of the wall sheet's resolution (the whole point of the 8x8 scheme), so the line's draw scale
		// must track `texture.Width` the same way, not a bare constant: `2f` was only correct for the
		// wall sheet's PRE-uprez width (512px -> 512*scale/line.Width(256) == 2*scale). Uprezzing
		// 756-v1 to 1248px shrank `scale` to compensate (on-screen block size is unchanged) but the
		// hard-coded `scale * 2f` shrank right along with it, so the line now reaches under half the
		// distance to the block edge -- reading as "too close to the centre". Deriving it from
		// texture.Width keeps the line's on-screen length pinned to the (resolution-independent) block
		// size at any wall-sheet resolution.
		float lineScale = scale * (float)texture.Width / (float)line.Width;
		for (int i = 0; i < height; i++)
		{
			if (!((float)texture.Height * scale * (float)i + base.Position.Y > (float)(-texture.Height) * scale) || !((float)texture.Height * scale * (float)i + base.Position.Y <= 600f))
			{
				continue;
			}
			for (int j = 0; j < width; j++)
			{
				if (blocks[i, j])
				{
					Vector2 val = default(Vector2);
					val.X = (float)texture.Width * scale * (float)j;
					val.Y = (float)texture.Height * scale * (float)i;
					int num = 0;
					int num2 = j % 8;
					num = i % 8;
					spriteBatch.Draw(texture, new Rectangle(num2 * texture.Width / 8, num * texture.Height / 8, texture.Width / 8, texture.Height / 8), val + base.Position, 0f, scale * 8f, center: false);
					(val2) = new Vector2((float)texture.Width * scale / 2f);
					val += val2;
					(val3) = new Color(new Vector4(0f, 0f, 0f, 0.6f));
					(val4) = new Color(new Vector4(1f, 1f, 1f, 0.3f));
					if (isfree(j + 1, i))
					{
						spriteBatch.Draw(line, val + base.Position, 0f, lineScale, center: true, val3);
					}
					if (isfree(j - 1, i))
					{
						spriteBatch.Draw(line, val + base.Position, (float)Math.PI, lineScale, center: true, val4);
					}
					if (isfree(j, i + 1))
					{
						spriteBatch.Draw(line, val + base.Position, (float)Math.PI / 2f, lineScale, center: true, val3);
					}
					if (isfree(j, i - 1))
					{
						spriteBatch.Draw(line, val + base.Position, -(float)Math.PI / 2f, lineScale, center: true, val4);
					}
				}
			}
		}
	}

	public override void Update(GameTime gameTime)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		Vector2 backgroundSpeed = oracle.BackgroundSpeed;
		base.Speed = (backgroundSpeed).Length() * 1f;
		base.Update(gameTime);
		if (base.Position.Y > 600f)
		{
			Die();
		}
	}

	public override void CollidesWith(ICollidable other)
	{
		base.CollidesWith(other);
	}
}
