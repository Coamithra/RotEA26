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
	//
	// The Default* values below are MIRRORED as literals by the eaWalls slider panel in
	// wwwroot/index.html (it seeds its sliders from them, then pushes them in). Re-bake one here
	// and update the panel's literal too, or ?wallsonly / ?walltune will render the stale value.
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

	// --- ?wall3d spike (Trello a66fc73e, plans/spike-wall3d.md) ---------------------------
	// Distance from the eye to the gameplay plane, in the 3D pass's world units. Arbitrary --
	// only the RATIO to the tower height matters, and that is pinned by DefaultDepth. Together
	// with ShaftHeight below it reproduces Project() exactly, so the 3D towers land on the same
	// pixels the slices do.
	private const float EyeDistance = 600f;

	// The near plane sits slightly in FRONT of the gameplay plane, and the frustum's near-plane
	// extents shrink to match, so the projection is unchanged. Without this the top ring of shaft
	// vertices would lie exactly ON the near plane and could clip out on float error.
	private const float NearFrac = 0.9f;

	// Vertical strips a side face is tessellated into. The fog lerp and the smoothstep bottom
	// dissolve are carried as PER-VERTEX colour that the rasteriser interpolates linearly, so the
	// bands are what resolve their curvature -- 1 gives a straight linear fade, 4 is already hard
	// to tell from the per-slice evaluation. Not a slice stack: each band is a real textured quad.
	private const int DefaultBands = 4;

	private VertexPositionColorTexture[] towerVerts;

	private int[] towerIndices;

	// Visible blocks for the 3D pass, packed as i * width + j, painter-sorted each frame.
	private readonly List<int> towerOrder = new List<int>();

	// Height of a tower in the 3D pass's world units: the z at which the projection's scale factor
	// E/(E+z) equals `depth`, i.e. where the base lands exactly where Project(top, depth) puts it.
	private static float ShaftHeight(float depth) => EyeDistance * (1f - depth) / depth;

	// A shaft's tint at height fraction `t` (1 = the top face, 0 = the ground). Shared by the slice
	// and 3D passes so the two can't drift: the 3D path evaluates it per band vertex instead of per
	// slice, but it is the same curve.
	private static Color ShaftTint(float t, Vector3 sideColor, Vector3 fogColor, float fogAmount)
	{
		Vector3 rgb = Vector3.Lerp(sideColor, fogColor, MathHelper.Clamp(fogAmount * (1f - t), 0f, 1f));
		float alpha = (t < DissolveFraction) ? MathHelper.SmoothStep(0f, 1f, t / DissolveFraction) : 1f;
		return new Color(new Vector4(rgb, alpha));
	}

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
			Color tint = ShaftTint(t, sideColor, fogColor, fogAmount);
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

	// ?wall3d (Trello a66fc73e): the same towers as REAL 3D geometry -- one batched
	// DrawUserIndexedPrimitives of the side faces -- instead of the stacked sprite slices.
	// Returns the number of blocks whose shaft was drawn, like DrawTowerShafts.
	//
	// The whole wall goes out in ONE buffered draw via SpriteBatchWrapper.DrawGeometry3D (which is
	// also where the "why this is nothing like the Quad.cs per-beam immediate-mode pathology"
	// argument lives); this method only builds the geometry for it.
	//
	// WHY THE GEOMETRY IS 3D RATHER THAN PRE-PROJECTED. Sending flat pre-projected quads would
	// lose w and give affine (PS1-style) texture warp across the foreshortened faces. Emitting
	// real boxes and letting the GPU do the perspective divide keeps UV interpolation correct --
	// which is the whole point, since the side faces then sample the REAL 756-v1 cell rather
	// than the radial smear the slice trick leaves behind.
	//
	// WHY NO DEPTH BUFFER. sceneTarget is DepthFormat.None. It doesn't need one: the shafts are
	// vertical boxes of equal height on a ground plane under a perspective camera at the VP, so
	// in polar coordinates about the VP a face's depth at radius r is r / r0 (r0 = its near
	// edge). Two blocks sharing a ray therefore never interleave -- the one whose near edge is
	// closer to the VP wins at every shared radius -- so the occludes relation is acyclic and a
	// painter's sort by distance from the VP is EXACT. Certified over the real level3.txt and
	// every Wall.Setup width by tools/walls/verify_tower_order.py. Top faces sit at depth 1 (the
	// maximum), so the existing "tops last" pass stays correct untouched.
	private int DrawTowerShafts3D()
	{
		float depth = EvilAliensWeb.Compat.DebugFlags.WallDepth ?? DefaultDepth;
		float fogAmount = EvilAliensWeb.Compat.DebugFlags.WallFog ?? DefaultFog;
		float sideDark = EvilAliensWeb.Compat.DebugFlags.WallSideDark ?? DefaultSideDark;
		Color fogColorFlag = EvilAliensWeb.Compat.DebugFlags.WallFogColor ?? DefaultFogColor;
		int bands = EvilAliensWeb.Compat.DebugFlags.Wall3DBands ?? DefaultBands;
		float blockW = (float)texture.Width * scale;
		float blockH = (float)texture.Height * scale;

		// Collect the visible blocks, then painter-sort them FAR-from-VP first so nearer-VP
		// towers paint over the ones leaning across them.
		towerOrder.Clear();
		for (int i = 0; i < height; i++)
		{
			float topY = blockH * (float)i + base.Position.Y;
			if (!RowShaftVisible(topY, blockH, depth))
			{
				continue;
			}
			for (int j = 0; j < width; j++)
			{
				if (blocks[i, j])
				{
					towerOrder.Add(i * width + j);
				}
			}
		}
		if (towerOrder.Count == 0)
		{
			return 0;
		}
		int w = width;
		Vector2 pos = base.Position;
		towerOrder.Sort((a, b) => BlockVpDistanceSq(b, w, blockW, blockH, pos)
			.CompareTo(BlockVpDistanceSq(a, w, blockW, blockH, pos)));

		// Worst case is 2 visible faces per block (a block straddling the VP on an axis shows
		// fewer), each tessellated into `bands` quads.
		EnsureTowerBuffers(towerOrder.Count * 2 * bands);

		Vector3 sideColor = Vector3.One * sideDark;
		Vector3 fogColor = fogColorFlag.ToVector3();
		float shaftH = ShaftHeight(depth);
		int cw = texture.Width / 8;
		int ch = texture.Height / 8;

		int nv = 0;
		int quads = 0;
		foreach (int packed in towerOrder)
		{
			int i = packed / w;
			int j = packed % w;
			float x0 = blockW * (float)j + base.Position.X;
			float y0 = blockH * (float)i + base.Position.Y;
			float x1 = x0 + blockW;
			float y1 = y0 + blockH;
			// The block's own cell of the seamless 8x8 sheet. NO half-texel inset: neighbouring
			// cells ARE the correct continuation (block (i,j) samples cell (j%8, i%8)), so insetting
			// would pull each face away from its neighbour's and re-open the seam it means to avoid.
			float u0 = (float)(j % 8 * cw) / (float)texture.Width;
			float u1 = (float)((j % 8 + 1) * cw) / (float)texture.Width;
			float v0 = (float)(i % 8 * ch) / (float)texture.Height;
			float v1 = (float)((i % 8 + 1) * ch) / (float)texture.Height;

			// Backface cull on the CPU: only the walls FACING the vanishing point are seen (the
			// base projects toward the VP, so a block to the right of it shows its left wall). A
			// block straddling the VP on an axis shows neither of that axis's walls.
			//
			// UV ORIENTATION IS WHAT KILLS THE SEAMS, on both axes, and neither is arbitrary.
			//
			// ALONG the edge: blocks step through the sheet as (u -> columns, v -> rows), so a
			// face's along-edge coordinate must follow the axis its edge runs along -- a vertical
			// edge spans rows, so it is `v`; a horizontal edge spans columns, so it is `u`. Get
			// that backwards and two stacked blocks' coplanar walls each restart the same range
			// instead of continuing it, hard-seaming every block boundary.
			//
			// DOWN the shaft: the wall hangs off one particular cell edge, so it has to START at
			// that edge's coordinate and run away from it -- the sheet then folds over the top
			// face's rim continuously instead of cutting to the far side of the cell. Hence the
			// down range reverses between the left wall (u0 -> u1) and the right one (u1 -> u0).
			//
			// Every tower is exactly shaftH tall in WORLD units, so spending one whole cell across
			// the shaft is uniform for every block, however long the shaft looks on screen.
			if (x0 > VanishX) AddFace(ref nv, ref quads, x0, y0, x0, y1, v0, v1, u0, u1, alongIsX: false, bands, shaftH, sideColor, fogColor, fogAmount);
			if (x1 < VanishX) AddFace(ref nv, ref quads, x1, y1, x1, y0, v1, v0, u1, u0, alongIsX: false, bands, shaftH, sideColor, fogColor, fogAmount);
			if (y0 > VanishY) AddFace(ref nv, ref quads, x1, y0, x0, y0, u1, u0, v0, v1, alongIsX: true, bands, shaftH, sideColor, fogColor, fogAmount);
			if (y1 < VanishY) AddFace(ref nv, ref quads, x0, y1, x1, y1, u0, u1, v1, v0, alongIsX: true, bands, shaftH, sideColor, fogColor, fogAmount);
		}
		if (quads == 0)
		{
			return towerOrder.Count;
		}

		// Eye at the VP, `e` in front of the gameplay plane, looking down -Z (XNA is right-handed).
		// z = 0 is the gameplay plane (the tower tops), z = shaftH the alien-base ground; design y
		// runs down, hence the y flip. A vertex at (x, y, z) lands at VP + (xy - VP) * e/(e+z) --
		// which is Project(xy, d) with d = e/(e+z), and d == depth exactly at z == shaftH. Verified
		// against Project() to ~1e-13 px by tools/walls/preview_wall3d.py's matrix check.
		float e = EyeDistance;
		Matrix view = Matrix.CreateTranslation(0f - VanishX, 0f - VanishY, 0f)
			* Matrix.CreateScale(1f, -1f, -1f)
			* Matrix.CreateTranslation(0f, 0f, 0f - e);
		Matrix projection = Matrix.CreatePerspectiveOffCenter(
			-400f * NearFrac, 400f * NearFrac, -300f * NearFrac, 300f * NearFrac, e * NearFrac, e + shaftH + 1f);
		// The wrapper owns the shared BasicEffect + the batch, and hands the device back after the
		// one buffered draw. BlendMode is AlphaBlend here (set at the top of Draw) -> straight alpha.
		spriteBatch.DrawGeometry3D(texture, towerVerts, nv, towerIndices, quads * 2, view, projection);
		return towerOrder.Count;
	}

	// The painter's key: how far a block's CENTRE is from the vanishing point, squared. Certified
	// as a valid topological order of the occludes relation by tools/walls/verify_tower_order.py
	// (the block's min-corner distance is NOT -- it fails on real level3 geometry).
	private static float BlockVpDistanceSq(int packed, int w, float blockW, float blockH, Vector2 pos)
	{
		float dx = blockW * ((float)(packed % w) + 0.5f) + pos.X - VanishX;
		float dy = blockH * ((float)(packed / w) + 0.5f) + pos.Y - VanishY;
		return dx * dx + dy * dy;
	}

	// One side face: the top edge (ax,ay)->(bx,by) swept down to the ground, cut into `bands`
	// vertical strips so the fog lerp and the bottom dissolve survive as interpolated vertex
	// colour. `alongA`/`alongB` are the along-edge texture coordinate at each end of the top edge;
	// `down0`/`down1` are the down-the-shaft one at the cap and the base. `alongIsX` says which
	// texture channel each belongs to -- see the UV note in DrawTowerShafts3D.
	private void AddFace(ref int nv, ref int quads, float ax, float ay, float bx, float by,
		float alongA, float alongB, float down0, float down1, bool alongIsX, int bands, float shaftH,
		Vector3 sideColor, Vector3 fogColor, float fogAmount)
	{
		for (int k = 0; k < bands; k++)
		{
			float fTop = (float)k / (float)bands;
			float fBot = (float)(k + 1) / (float)bands;
			float zTop = shaftH * fTop;
			float zBot = shaftH * fBot;
			Color cTop = ShaftTint(1f - fTop, sideColor, fogColor, fogAmount);
			Color cBot = ShaftTint(1f - fBot, sideColor, fogColor, fogAmount);
			float dTop = MathHelper.Lerp(down0, down1, fTop);
			float dBot = MathHelper.Lerp(down0, down1, fBot);
			int b = nv;
			towerVerts[nv++] = new VertexPositionColorTexture(new Vector3(ax, ay, zTop), cTop, FaceUv(alongA, dTop, alongIsX));
			towerVerts[nv++] = new VertexPositionColorTexture(new Vector3(bx, by, zTop), cTop, FaceUv(alongB, dTop, alongIsX));
			towerVerts[nv++] = new VertexPositionColorTexture(new Vector3(bx, by, zBot), cBot, FaceUv(alongB, dBot, alongIsX));
			towerVerts[nv++] = new VertexPositionColorTexture(new Vector3(ax, ay, zBot), cBot, FaceUv(alongA, dBot, alongIsX));
			int t = quads * 6;
			towerIndices[t] = b;
			towerIndices[t + 1] = b + 1;
			towerIndices[t + 2] = b + 2;
			towerIndices[t + 3] = b;
			towerIndices[t + 4] = b + 2;
			towerIndices[t + 5] = b + 3;
			quads++;
		}
	}

	private static Vector2 FaceUv(float along, float down, bool alongIsX)
	{
		return alongIsX ? new Vector2(along, down) : new Vector2(down, along);
	}

	// Grow-only scratch arrays; the quad count is stable frame to frame, so this settles at once.
	// The index pattern is positional, so it is rewritten with the vertices rather than cached --
	// BlazorGL re-uploads the whole index array by .Length every call anyway.
	private void EnsureTowerBuffers(int maxQuads)
	{
		if (towerVerts == null || towerVerts.Length < maxQuads * 4)
		{
			towerVerts = new VertexPositionColorTexture[maxQuads * 4];
			towerIndices = new int[maxQuads * 6];
		}
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
		float tileX = (float)fog.Width;
		float tileY = (float)fog.Height;
		float phaseY = MyMath.Mod(base.Position.Y * speed, tileY);
		Color tint = new Color(new Vector4(1f, 1f, 1f, alpha));
		spriteBatch.BlendMode = (SpriteBlendMode)2;
		for (float y = phaseY - tileY; y < 600f; y += tileY)
		{
			for (float x = 0f; x < 800f; x += tileX)
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
			DrawFogWisps(EvilAliensWeb.Compat.DebugFlags.Wall3D ? DrawTowerShafts3D() : DrawTowerShafts());
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
