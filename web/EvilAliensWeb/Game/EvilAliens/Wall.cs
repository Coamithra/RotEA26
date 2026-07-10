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

	// Design px of corner "lean" per slice. The lean is (topD - depth) * |corner - VP|, so it is
	// ~0 at the screen centre and ~170px at the far corners; the slice count is derived from the
	// worst lean on screen rather than fixed, so centre blocks don't pay for edge blocks. Above
	// ~8px the shrinking slice rects stop overlapping cleanly and the shaft bands into vertical
	// strips with a staircased bottom edge.
	//
	// NOTE the step is CLAMPED by MaxSlices, and at the baked 1px it is the clamp that binds, not
	// the step: the worst on-screen lean is ~170px, so 1px asks for ~170 slices and gets 64 (an
	// effective ~2.7px step at the far corners, finer everywhere else). That is why dropping the
	// step from 5 to 1 barely moved the frame time -- it only took the slice count 34 -> 64. To
	// actually resolve a true 1px step the lever is MaxSlices, and it costs ~3x the slice draws.
	internal const float DefaultSliceStep = 1f;

	private const int MaxSlices = 64;

	// Size of the window the slice pass samples out of 756-v1-side.png, and that sheet's per-cell
	// pitch. A CONTRACT with tools/walls/build_wall_side.py's CELL -- the sheet is wrap-padded by
	// exactly this much, so a window whose origin lands anywhere in [0, scanSpan)^2 stays inside it.
	//
	// 64 rather than a token 16 because adjacent atlas windows do NOT filter across their shared
	// edge -- each clamps -- so a magnified window leaves a soft mismatched band at every block
	// boundary even when the CONTENT is continuous. At 64 one texel is about one on-screen pixel at
	// a typical block size (~67 design px) and that clamp is sub-pixel.
	private const int SideWindow = 64;

	// How far the slice pass scans DIAGONALLY across the side sheet, in TEXELS OF TRAVEL PER SLICE
	// (0 = no scan). Without a scan every slice samples the SAME window, so the sliver each slice
	// leaves exposed is always the same border texels smeared radially out of the VP -- the shaft
	// reads as streaks with no surface of its own. Sliding the window as the shaft descends makes
	// successive slivers expose successive texels, which is what gives the vertical faces a texture
	// at all. The sheet tiles seamlessly (build_wall_side.py), so the walk wraps without a seam.
	//
	// Texels-per-slice, not wrap-cycles, so the natural value is simply 1 and it does not move when
	// the sheet is resized. Below 1 consecutive slices repeat a window and smear; above it they skip
	// texels and the shaft corrugates into visible ridges.
	internal const float DefaultSideScan = 1f;

	// Degrees the shaft TWISTS between its cap and its base (0 = no twist). Each depth-layer is
	// rotated by twist * (1 - t) -- zero at the cap, so it meets the unrotated top face cleanly.
	//
	// The rotation is applied to the WHOLE LAYER about the VANISHING POINT, not to each slice about
	// its own centre. That distinction is load-bearing:
	//   - A rigid rotation of the layer about the VP keeps every footprint at that depth an affine
	//     image of the others, so adjacent blocks stay glued edge to edge and the footprints stay
	//     DISJOINT -- which is exactly what makes the single global painter's order correct.
	//   - Rotating each slice about its own centre would tile nothing: squares rotated in place
	//     don't meet, so every solid block cluster would open X-shaped cracks down its shaft, and
	//     the corners would swing outside their footprint into a neighbour's.
	// Because SpriteBatch source rects are axis-aligned, the texture cannot be rotated independently
	// of the geometry -- so this rotates both, and reads as a spiral shear down the tower.
	internal const float DefaultSliceTwist = 0f;

	// How far the tower TOPS are lifted above the gameplay plane, as a fraction of the VP depth
	// (0 = flush, the shipped XBLIG look). The top faces are drawn projected at depth 1 + lift,
	// i.e. scaled slightly AWAY from the vanishing point, so the caps read as sitting proud of the
	// plane the ship flies on rather than flush with it.
	//
	// This is PURELY cosmetic: CollisionType/CollisionLevelMap still use the unprojected block
	// rects, so the hitbox stays exactly where it has always been. A lift therefore buys visual
	// height at the cost of the sprite drifting off its own hitbox by (lift * distance-from-VP) --
	// ~8 design px at a screen corner for lift 0.02. Keep it small, and check with ?hitboxes.
	internal const float DefaultTopLift = 0f;

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

	// Per-face shading contrast (0 = every face the same shade, the pre-shading look). Once the side
	// texture runs continuously across block boundaries, nothing distinguishes a tower's north face
	// from its east face and the CORNERS disappear -- this puts them back.
	//
	// A block only ever shows the two faces pointing at the VP: one HORIZONTAL-facing (north if the
	// block is below the VP, south if above) and one VERTICAL-facing (east if left of the VP, west if
	// right). So a corner is always a horizontal face meeting a vertical one, and the contrast that
	// makes it read is between those two ORIENTATIONS -- not between compass directions. Hence
	// horizontal faces scale by (1 + light) and vertical faces by (1 - light): every corner in every
	// quadrant gets contrast. (A pure directional light would give ZERO contrast in two of the four
	// quadrants, wherever its two face normals happen to catch it equally.)
	//
	// DefaultFaceAngle then adds a weaker directional term so north != south and east != west, giving
	// the whole wall a consistent light source rather than a flat orientation-only shade.
	internal const float DefaultFaceLight = 0.35f;

	// Azimuth of the light, degrees, screen space: 0 = from the right (+x), 90 = from below (+y).
	// 225 = from the upper left, the usual convention.
	internal const float DefaultFaceAngle = 225f;

	// How much of DefaultFaceLight the directional term gets. Small: the orientation contrast above is
	// what makes corners legible, and a strong directional term would cancel it in some quadrant.
	private const float FaceDirWeight = 0.35f;

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

	// Companion to the wall sheet (tools/walls/build_wall_side.py): the same 8x8 grid, but each cell
	// is a 2D SCAN PLANE rather than a square -- a mirror-tiled, area-averaged version of that cell,
	// wrap-padded by SideWindow. Area-averaged because slicing the full-res cell makes the shaft
	// corduroy (consecutive slices re-draw the same high-frequency detail at slightly different
	// scales, so the sliver each one leaves exposed repeats it instead of smearing into a face); a
	// PLANE rather than a square or a strip because DefaultSideScan must be able to walk EITHER
	// axis -- see the axis choice in DrawTowerShafts. Mirror-tiled, so the scan wraps seamlessly.
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
	// gameplay plane itself; d == DefaultDepth lands on the alien-base ground far below, and
	// d > 1 rises above the plane (the top-face lift).
	private static Vector2 Project(Vector2 top, float d)
	{
		return new Vector2(VanishX + (top.X - VanishX) * d, VanishY + (top.Y - VanishY) * d);
	}

	// Depth the tower TOPS are drawn at. 1 == flush with the gameplay plane. Forced to exactly 1
	// when the towers are off, so ?walltowers=0 reproduces the flat look bit for bit (Project(p, 1)
	// is not guaranteed to round-trip p exactly in float, so the callers skip it entirely at 1).
	private static float TopDepth()
	{
		if (!EvilAliensWeb.Compat.DebugFlags.WallTowers)
		{
			return 1f;
		}
		return 1f + (EvilAliensWeb.Compat.DebugFlags.WallTopLift ?? DefaultTopLift);
	}

	// Is any part of block row `i`'s shaft -- top face down to projected base -- on screen? A
	// block whose TOP face has scrolled off the bottom still shows its base (the base projects
	// toward the VP, i.e. upward, when the block is below the VP), and a block still above the
	// screen already shows its base below the top edge. That second case is the "towers rise
	// base-first out of the fog on entry" effect, so this cull must be wider than the top-face
	// loop's -- which is deliberately left alone.
	private bool RowShaftVisible(float topY, float blockH, float depth, float topD)
	{
		float baseY = VanishY + (topY - VanishY) * depth;
		// The shaft's other extreme is its cap, at topD -- which is the gameplay plane only when the
		// top-face lift is 0. Recomputing it via Project at topD == 1 would be a no-op up to float
		// rounding, so take topY verbatim there and keep the cull bit-identical to the unlifted path.
		float capY = (topD == 1f) ? topY : VanishY + (topY - VanishY) * topD;
		float lo = Math.Min(capY, baseY);
		float hi = Math.Max(capY + blockH * topD, baseY + blockH * depth);
		return hi > 0f && lo < 600f;
	}

	// Which of block (i, j)'s four sides are OUTER EDGES of the wall, packed into a sprite colour for
	// faceshade.fx: r/g/b = north/south/east exposed, a = west. A side shared with a neighbouring
	// block is not a face of the wall at all, and shading it anyway mitres a dark wedge into the top
	// corner of every block -- two of them meeting at each interior boundary, reading as a seam grid.
	//
	// The alpha channel encodes west as 255/128 rather than 255/0: nothing in the batcher should drop
	// a fully-transparent sprite, but the sprite's alpha is meaningless here (SliceTint carries the
	// real one) and it costs nothing not to find out.
	private Color FaceMask(int i, int j)
	{
		return new Color(
			isfree(j, i - 1) ? 255 : 0,
			isfree(j, i + 1) ? 255 : 0,
			isfree(j + 1, i) ? 255 : 0,
			isfree(j - 1, i) ? 255 : 128);
	}

	// The four face multipliers, (north, south, east, west), for faceshade.fx.
	//
	// DARKEN-ONLY (all <= 1): the slice tint already carries the fog lerp, so a factor above 1 would
	// clip the hazy base to white. Two terms:
	//   ORIENTATION -- vertical faces (east/west) darken by `light`, horizontal ones (north/south)
	//     don't. A block only ever shows one horizontal and one vertical face, so a corner is ALWAYS
	//     a horizontal meeting a vertical, and this term alone gives every corner in every quadrant
	//     its contrast. A pure directional light would not: wherever its two visible face normals
	//     catch the light equally (e.g. north and west under a light from the upper left), the
	//     contrast between them is exactly zero and that corner vanishes again.
	//   DIRECTIONAL -- a weaker term so north != south and east != west, giving the whole wall one
	//     consistent light source. Deliberately weak (FaceDirWeight) so it can't cancel the above.
	private static Vector4 FaceFactors(float light, float angle)
	{
		// Unit vector pointing from a surface TOWARD the light.
		float lx = (float)Math.Cos(angle);
		float ly = (float)Math.Sin(angle);
		float dir = FaceDirWeight * light;
		// Face normals in screen space: north (0,-1), south (0,1), east (1,0), west (-1,0).
		// (1 - dot) / 2 is 0 facing the light, 1 facing away.
		float north = 1f - dir * (1f + ly) * 0.5f;
		float south = 1f - dir * (1f - ly) * 0.5f;
		float east = (1f - light) * (1f - dir * (1f - lx) * 0.5f);
		float west = (1f - light) * (1f - dir * (1f + lx) * 0.5f);
		return new Vector4(north, south, east, west);
	}

	// Stacked-slice extrusion. Returns the number of blocks whose shaft was drawn (the wisp pass
	// fades on that count). Draws slice depth k for EVERY block before depth k+1: at one depth all
	// footprints are the same affine scaling of disjoint rects about the VP, so they stay disjoint
	// and painter's order across the whole wall is correct. Per-block slice ladders would NOT be
	// safe -- a tall shaft can lean over a block nearer the VP, and only a shared depth ordering
	// guarantees the nearer slice lands last.
	private int DrawTowerShafts(float topD)
	{
		float depth = EvilAliensWeb.Compat.DebugFlags.WallDepth ?? DefaultDepth;
		float step = EvilAliensWeb.Compat.DebugFlags.WallSliceStep ?? DefaultSliceStep;
		float fogAmount = EvilAliensWeb.Compat.DebugFlags.WallFog ?? DefaultFog;
		float sideDark = EvilAliensWeb.Compat.DebugFlags.WallSideDark ?? DefaultSideDark;
		Color fogColorFlag = EvilAliensWeb.Compat.DebugFlags.WallFogColor ?? DefaultFogColor;
		float scan = Math.Max(0f, EvilAliensWeb.Compat.DebugFlags.WallSideScan ?? DefaultSideScan);
		float twist = MathHelper.ToRadians(EvilAliensWeb.Compat.DebugFlags.WallTwist ?? DefaultSliceTwist);
		float faceLight = MathHelper.Clamp(EvilAliensWeb.Compat.DebugFlags.WallFaceLight ?? DefaultFaceLight, 0f, 1f);
		float faceAngle = MathHelper.ToRadians(EvilAliensWeb.Compat.DebugFlags.WallFaceAngle ?? DefaultFaceAngle);
		// Skipped entirely at 0, which reproduces the flat-shaded look and costs no batch flushes.
		bool faceShade = faceLight > 0.001f;
		Vector4 faceFactors = faceShade ? FaceFactors(faceLight, faceAngle) : Vector4.One;
		float blockW = (float)texture.Width * scale;
		float blockH = (float)texture.Height * scale;
		// One pass to find the worst corner lean on screen; the slice count follows from it, so a
		// wall sitting near the VP (all shafts foreshortened to nothing) costs almost no draws.
		float maxLean = 0f;
		int visibleBlocks = 0;
		for (int i = 0; i < height; i++)
		{
			float topY = blockH * (float)i + base.Position.Y;
			if (!RowShaftVisible(topY, blockH, depth, topD))
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
				maxLean = Math.Max(maxLean, (topD - depth) * (float)Math.Sqrt(dx * dx + dy * dy));
			}
		}
		if (visibleBlocks == 0)
		{
			return 0;
		}
		int slices = (int)MathHelper.Clamp((float)Math.Ceiling(maxLean / step), 1f, MaxSlices);
		EvilAliensWeb.Compat.WallProfiler.NoteSlices(slices, visibleBlocks);
		Vector3 sideColor = Vector3.One * sideDark;
		Vector3 fogColor = fogColorFlag.ToVector3();
		// The side sheet is ONE contiguous, seamless area-averaged copy of the wall sheet, wrap-padded
		// by SideWindow (build_wall_side.py). scanSpan is its wrap period (== 8 * SideWindow); the
		// padding is what lets a window whose origin lands anywhere in [0, scanSpan) stay in bounds.
		int scanSpan = Math.Max(1, side.Width - SideWindow);
		// One effect switch for the WHOLE slice pass, not one per sprite: faceshade.fx classifies each
		// pixel into a face from its own position inside the slice, so it needs no per-block data. Its
		// only per-slice uniform is WindowOrigin, so the pass costs `slices` batch flushes (~64) rather
		// than the ~1500 extra sprite draws a two-tints-per-slice split would have cost.
		if (faceShade)
		{
			spriteBatch.faceShadeEffect.Window = (float)SideWindow;
			spriteBatch.faceShadeEffect.SheetSize = (float)side.Width;
			spriteBatch.faceShadeEffect.Enable();
		}
		for (int k = 0; k < slices; k++)
		{
			// t: 0 at the base, -> 1 at the top face (never reaching it -- the real top face is
			// drawn after and covers the last step's worth of seam).
			float t = (float)k / (float)slices;
			float d = depth + (topD - depth) * t;
			float haze = MathHelper.Clamp(fogAmount * (1f - t), 0f, 1f);
			Vector3 rgb = Vector3.Lerp(sideColor, fogColor, haze);
			float alpha = (t < DissolveFraction) ? MathHelper.SmoothStep(0f, 1f, t / DissolveFraction) : 1f;
			Color tint = new Color(new Vector4(rgb, alpha));
			// Anchor the scan at the TOP of the shaft (offset 0 at t == 1) so the topmost sliver
			// samples the plane's origin and meets the top face continuously; the window then walks
			// across the plane as the shaft descends. Whole-texel, so a scan wide enough to resolve
			// one texel per slice is what makes a face read as texture rather than bands.
			// `scan` is texels per slice, so the travel at depth t is (1 - t) * scan * slices.
			int off = (int)MyMath.Mod((1f - t) * scan * (float)slices, (float)scanSpan);
			if (faceShade)
			{
				// Every block's window origin is congruent to `off` mod SideWindow (the origins are
				// j*SideWindow + off, mod a multiple of SideWindow), so this ONE value lets the shader
				// recover each sprite's local UV from its atlas UV. See faceshade.fx.
				spriteBatch.faceShadeEffect.WindowOrigin = (float)(off % SideWindow);
				// Fade the shading into the haze as the shaft descends: at the fogged base the faces
				// converge, as fog does, instead of darkening the fog itself.
				spriteBatch.faceShadeEffect.Factors = Vector4.Lerp(faceFactors, Vector4.One, haze);
				// The tint moves to a uniform because it is the same for every block at this depth --
				// which is exactly what frees each sprite's vertex colour to carry its face mask.
				spriteBatch.faceShadeEffect.SliceTint = new Vector4(rgb, alpha);
			}
			// A slice covers the same on-screen area as the block's top face, shrunk by d. Scaled
			// per-axis off the block size rather than by one factor, so a non-square wall sheet
			// (blockW != blockH) can't silently squash the shafts.
			Vector2 sliceScale = new Vector2(blockW * d / (float)SideWindow, blockH * d / (float)SideWindow);
			// This depth-layer's rigid rotation about the VP: 0 at the cap, `twist` at the base.
			// Each slice is drawn about its own centre (origin = half the source window) and its
			// centre is orbited about the VP by the same angle -- together that is one rotation of
			// the whole layer, so blocks stay glued and footprints stay disjoint.
			float ang = twist * (1f - t);
			bool twisted = ang != 0f;
			float cos = twisted ? (float)Math.Cos(ang) : 1f;
			float sin = twisted ? (float)Math.Sin(ang) : 0f;
			Vector2 sliceOrigin = new Vector2((float)SideWindow * 0.5f);
			for (int i = 0; i < height; i++)
			{
				float topY = blockH * (float)i + base.Position.Y;
				if (!RowShaftVisible(topY, blockH, depth, topD))
				{
					continue;
				}
				for (int j = 0; j < width; j++)
				{
					if (!blocks[i, j])
					{
						continue;
					}
					float x = blockW * (float)j + base.Position.X;
					// The scan walks the window DIAGONALLY -- the same offset on both axes -- and
					// that is what makes one rule serve every face.
					//
					// The only part of a slice you ever see is its edge FACING the VP, so the scan
					// has to travel PERPENDICULAR to that edge to expose new texels. A block above
					// or below the VP shows a horizontal edge, whose sliver is a ROW of the window
					// (perpendicular = Y); one to its left or right shows a vertical edge, whose
					// sliver is a COLUMN (perpendicular = X). A diagonal advances BOTH by one texel
					// per slice, so every orientation gets the full perpendicular travel at once --
					// where either single axis would give one orientation the ideal rate and the
					// other exactly zero (its sliver would merely translate along its own length,
					// re-showing the same texels, which over N slices traces hard diagonal streaks).
					//
					// Do NOT "fix" this by picking an axis per block from |dx| vs |dy|: that flips
					// as a block scrolls past the VP diagonal, and the texture POPS mid-screen.
					// Depending only on t keeps the scan a pure function of depth, so nothing can pop.
					//
					// The price is that the offset perpendicular to one edge is PARALLEL to the
					// other, so each face is also sheared ~45 degrees along its length. Consecutive
					// slivers carry genuinely different texels, so it reads as diagonal grain, not
					// as the coherent lines a pure translation produces.
					// A block's window sits at its own cell of the shared sheet, so the NEIGHBOURING
					// block's window abuts it in the source. Both stretch their window across
					// footprints that are edge-to-edge on screen, so at a shared edge they sample
					// the identical texel and the texture runs straight through -- no seam. (Per-cell
					// isolated tiles hard-edge at every block boundary; that was the bug.) The scan
					// `off` is the same for every block, so it slides the whole wall's sampling in
					// lockstep and cannot break that. Mod the wrap period: the sheet tiles seamlessly,
					// so a block walking off the 8th cell continues into the 1st.
					Rectangle src = new Rectangle(
						(j % 8 * SideWindow + off) % scanSpan,
						(i % 8 * SideWindow + off) % scanSpan,
						SideWindow, SideWindow);
					// Draw about the block's CENTRE (origin = half the window, in source texels, so
					// scale carries it to half the on-screen slice) -- identical to the old top-left
					// placement at ang == 0, and the only anchor a rotation can use.
					Vector2 p = Project(new Vector2(x + blockW * 0.5f, topY + blockH * 0.5f), d);
					if (twisted)
					{
						float vx = p.X - VanishX;
						float vy = p.Y - VanishY;
						p = new Vector2(VanishX + vx * cos - vy * sin, VanishY + vx * sin + vy * cos);
					}
					// When face-shading, the sprite colour is NOT a tint (that moved to SliceTint) --
					// it is this block's mask of which sides are OUTER EDGES of the wall. A side shared
					// with a neighbour is not a face, and shading it puts a dark mitre wedge at every
					// interior block corner, which reads as a seam grid. Same isfree() test the
					// top-face edge lines use.
					spriteBatch.Draw(side, src, p, ang, sliceScale, sliceOrigin, faceShade ? FaceMask(i, j) : tint);
				}
			}
		}
		// The crisp top faces and the fog wisps must NOT be face-shaded; the wrapper flushes the last
		// slice batch on the next draw, when it sees the effect state change.
		if (faceShade)
		{
			spriteBatch.faceShadeEffect.Disable();
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
		// both passes, forces topD to 1, and the rest of this method reproduces the original flat
		// look exactly.
		float topD = TopDepth();
		bool lifted = topD != 1f;
		if (EvilAliensWeb.Compat.DebugFlags.WallTowers)
		{
			// Timed so the tower cost can be weighed against the flat path (and against the real-3D
			// spike) with a number rather than an argument. Zero cost unless eaWallPerf is on.
			long perf = EvilAliensWeb.Compat.WallProfiler.Begin();
			DrawFogWisps(DrawTowerShafts(topD));
			EvilAliensWeb.Compat.WallProfiler.EndTowers(perf);
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
					// The top-face cap and its edge lines ride the lift together: projecting at
					// topD > 1 scales them away from the VP, so a cap grows and slides outward
					// exactly as its shaft's topmost slice does. Collision is unaffected -- it
					// reads `blocks` + Position, never these draw positions.
					Vector2 topLeft = val + base.Position;
					spriteBatch.Draw(texture, new Rectangle(num2 * texture.Width / 8, num * texture.Height / 8, texture.Width / 8, texture.Height / 8), lifted ? Project(topLeft, topD) : topLeft, 0f, scale * 8f * topD, center: false);
					(val2) = new Vector2((float)texture.Width * scale / 2f);
					val += val2;
					(val3) = new Color(new Vector4(0f, 0f, 0f, 0.6f));
					(val4) = new Color(new Vector4(1f, 1f, 1f, 0.3f));
					Vector2 centre = val + base.Position;
					if (lifted)
					{
						centre = Project(centre, topD);
					}
					float capLineScale = lineScale * topD;
					if (isfree(j + 1, i))
					{
						spriteBatch.Draw(line, centre, 0f, capLineScale, center: true, val3);
					}
					if (isfree(j - 1, i))
					{
						spriteBatch.Draw(line, centre, (float)Math.PI, capLineScale, center: true, val4);
					}
					if (isfree(j, i + 1))
					{
						spriteBatch.Draw(line, centre, (float)Math.PI / 2f, capLineScale, center: true, val3);
					}
					if (isfree(j, i - 1))
					{
						spriteBatch.Draw(line, centre, -(float)Math.PI / 2f, capLineScale, center: true, val4);
					}
				}
			}
		}
	}

	// The Y at which the wall has fully left the screen and may be unloaded.
	//
	// A block's base projects TOWARD the VP, so a block below the VP has its shaft drawn ABOVE its top
	// face. When the last cap crosses the bottom edge the towers are still on screen, and dying at 600
	// pops them out of existence. The last thing to leave is the base of the TOPMOST row -- the row at
	// Position.Y, whose base sits highest of all -- so the wall is done when
	//     VanishY + (Position.Y - VanishY) * depth >= 600
	// which rearranges to the threshold below. At depth 1 (no extrusion) it collapses to 600, and with
	// the towers off it IS 600, so ?walltowers=0 unloads exactly as before.
	//
	// NOTE this also delays Walls.wall_OnDeath -> Terminate(), i.e. the level's next event, by the time
	// it takes to scroll the extra distance. That is intended: the section is not over until its towers
	// have gone. (?walltwist orbits slices about the VP and can push a shaft slightly past this; it is
	// a tuning knob defaulting to 0, and the overshoot is well inside the base's alpha dissolve.)
	private float DeathY()
	{
		if (!EvilAliensWeb.Compat.DebugFlags.WallTowers)
		{
			return 600f;
		}
		float depth = MathHelper.Clamp(EvilAliensWeb.Compat.DebugFlags.WallDepth ?? DefaultDepth, 0.05f, 1f);
		return VanishY + (600f - VanishY) / depth;
	}

	public override void Update(GameTime gameTime)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		Vector2 backgroundSpeed = oracle.BackgroundSpeed;
		base.Speed = (backgroundSpeed).Length() * 1f;
		base.Update(gameTime);
		if (base.Position.Y > DeathY())
		{
			Die();
		}
	}

	public override void CollidesWith(ICollidable other)
	{
		base.CollidesWith(other);
	}
}
