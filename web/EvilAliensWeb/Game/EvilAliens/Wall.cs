using System;
using System.Collections.Generic;
using System.IO;
using EvilAliens.Constants;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using EvilAliensWeb.Compat;

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
	// Shafts are REAL 3D geometry: the side faces of every visible tower go out in ONE buffered
	// DrawUserIndexedPrimitives (SpriteBatchWrapper.DrawGeometry3D), which is why they cost ~0.4ms
	// over drawing no towers at all where the old stacked-sprite-slice extrusion cost ~3.8ms. The
	// "3D is unviable on WebGL" reading of Quad.cs was about three immediate-mode draws PER BEAM,
	// each forcing a batch flush -- a batching pathology, not 3D throughput.
	//
	// The Default* values below are MIRRORED as literals by the eaWalls slider panel in
	// wwwroot/index.html (it seeds its sliders from them, then pushes them in). Re-bake one here
	// and update the panel's literal too, or ?wallsonly / ?walltune will render the stale value.
	private const float VanishX = 400f;

	private const float VanishY = 300f;

	internal const float DefaultDepth = 0.66f;

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

	// How far the shaft is fogged at its base (1 = fully the floor colour, 0 = fog off). Now that the
	// haze colour IS the floor (a mid, saturated blue below the shaft's brightness, not the old
	// near-white that lit the base UP), a strong fade darkens the base into the floor as intended.
	internal const float DefaultFog = 0.55f;

	// FALLBACK haze colour. In play the shaft actually fogs toward the LIVE floor colour
	// (`oracle.AlienBaseFloorColor`, which tracks the five Level-3 floor switches); this is only used
	// for the first floor and off-level (e.g. the harness). It is the initial floor: 756 plus its two
	// additive 2331-v5 fog layers, measured RGB(46,125,201).
	//
	// This is now REAL DISTANCE FOG (BasicEffect.FogEnabled), not a tint, and that inverts what the
	// colour has to be. A sprite Color tint MULTIPLIES -- it can only ever scale the wall texture DOWN,
	// never paint it up to a colour -- so the slice path needed a high-value blue-white (158,199,242)
	// sitting ABOVE DefaultSideDark just to lift the base toward the haze, and leaned on the alpha
	// dissolve to finish the fade. Fog LERPS, so that same bright colour now overshoots the other way
	// and makes the base BRIGHTER than the shaft (the "bottom lightens, looks off" bug). Fogging toward
	// the real floor colour instead -- darker and more saturated than the shaft's lit mid-body -- makes
	// the base recede into the floor it melts into, which is what a shaft dropping into shadow does.
	private static readonly Color DefaultFogColor = new Color(46, 125, 201);

	// Brightness of the shaft at its cap (1 = as bright as the top face). Sides are darker than the
	// lit top, as they would be under a top-down light. Dialed up from the slice path's 0.55 now that
	// the fog no longer has to brighten the shaft to fake the haze.
	internal const float DefaultSideDark = 0.7f;

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
	// Dialed to 140 (from the lower left), which reads best against the alien-base floor's own
	// lighting; 225 would be the usual upper-left convention.
	internal const float DefaultFaceAngle = 140f;

	// How much of DefaultFaceLight the directional term gets. Small: the orientation contrast above is
	// what makes corners legible, and a strong directional term would cancel it in some quadrant.
	private const float FaceDirWeight = 0.35f;

	// Alpha of the additive fog wisps drawn ACROSS the shafts (0 = off) and their scroll speed
	// relative to the wall. 0.8 matches the near fog background layer, which sits inside the
	// shaft's 0.66..1.0 depth band -- so the wisps parallax correctly against the slices.
	internal const float DefaultWispAlpha = 0.15f;

	internal const float DefaultWispSpeed = 0.8f;

	// Bottom fraction of the shaft that alpha-dissolves into the floor. The fog takes the shaft's
	// COLOUR to the haze; this takes its COVERAGE to zero, so it melts into the floor art rather
	// than ending on a hard (if correctly-coloured) edge.
	private const float DissolveFraction = 0.18f;

	// --- Real 3D tower geometry (Trello a66fc73e, plans/spike-wall3d.md) ------------------
	// Distance from the eye to the gameplay plane, in the 3D pass's world units. Arbitrary --
	// only the RATIO to the tower height matters, and that is pinned by DefaultDepth. Together
	// with ShaftHeight below it reproduces Project() exactly, so the towers land on the same pixels
	// the top faces do.
	private const float EyeDistance = 600f;

	// The near plane sits slightly in FRONT of the gameplay plane, and the frustum's near-plane
	// extents shrink to match, so the projection is unchanged. Without this the top ring of shaft
	// vertices would lie exactly ON the near plane and could clip out on float error.
	private const float NearFrac = 0.9f;

	// Vertical strips a side face is tessellated into. NOT a slice stack -- each band is a real
	// textured quad, and the geometry would be exact at 1. The bands exist only to resolve the
	// SMOOTHSTEP bottom dissolve, which is carried as per-vertex alpha and interpolated linearly
	// between bands; at 1 the dissolve degrades to a straight linear fade. The fog needs no bands
	// at all: it is linear in world z, so interpolating it is exact.
	private const int DefaultBands = 4;

	private VertexPositionColorTexture[] towerVerts;

	private int[] towerIndices;

	// Visible blocks for the 3D pass, packed as i * width + j, painter-sorted each frame.
	private readonly List<int> towerOrder = new List<int>();

	// World z at which the projection's scale factor E/(E+z) equals `d` -- i.e. the height whose
	// footprint is exactly Project(top, d). Negative above the gameplay plane (d > 1, a lifted cap).
	private static float ZAtDepth(float d) => EyeDistance * (1f / d - 1f);

	// Distance from the eye to depth `d`. Falls straight out of the same relation (E + z == E/d) and
	// is what the fog is keyed on, so the haze is a function of a tower's real depth.
	private static float EyeDistanceAtDepth(float d) => EyeDistance / d;

	// Shaft coverage at height fraction `f` (0 = the cap, 1 = the ground): opaque until the bottom
	// DissolveFraction, then smoothstepped away so the base melts into the floor art.
	private static float ShaftAlpha(float f)
	{
		float t = 1f - f;
		return (t < DissolveFraction) ? MathHelper.SmoothStep(0f, 1f, t / DissolveFraction) : 1f;
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

	private bool[,] blocks;

	// TEMP (?walltrace) per-instance diagnostic state; see DebugFlags.WallTrace.
	private static int traceIdCounter;
	private int traceId = -1;
	private int traceFrame;
	private bool traceLoggedShaft;
	private bool traceLoggedTop;
	private int traceShaftQuads;

	// TEMP (?walltrace): last frame's visible-block set, for the mid-screen pop detector.
	// Non-null only while tracing (allocated in Setup/SetupFromFile).
	private System.Collections.Generic.HashSet<int> traceShaftPrev;

	private Texture2D line;

	private CollisionLevelMap collisionMap;

	// The grid variation passed to Setup, retained so a co-op client puppet can rebuild the
	// exact same grid (Compat/Net, card 11.2). -1 until Setup runs / for the debug file path.
	private int netVariation = -1;

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
		line = content.Load<Texture2D>("GFX/Base/black_line_lalalal");
		fog = content.Load<Texture2D>("GFX/Base/2331-v5");
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
		netVariation = variation;
		collisionMap = null;
		traceId = -1;
		traceFrame = 0;
		traceLoggedShaft = false;
		traceLoggedTop = false;
		traceShaftQuads = 0;
		// Empty (not null) so anything visible on the very first frame logs as a POP IN.
		traceShaftPrev = EvilAliensWeb.Compat.DebugFlags.WallTrace
			? new System.Collections.Generic.HashSet<int>() : null;
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
				using (StreamReader streamReader = OpenLevelGrid("level3.txt"))
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
		scale = 800f / (float)(texture.LogicalWidth() * width);
		float num3 = (float)texture.LogicalHeight() * scale;
		base.Position = new Vector2(0f, (0f - num3) * (float)height - EntryLead());
		base.Direction = (float)Math.PI / 2f;
		Vector2 backgroundSpeed = oracle.BackgroundSpeed;
		base.Speed = (backgroundSpeed).Length() * 1f;
	}

	// How far ABOVE the flat spawn point (-rowH*height, bottom row's top face at the screen edge)
	// the wall must start so NOTHING is visible on its first frame. A block's projected base leads
	// its cap by VanishY*(1/depth - 1) px of scroll -- the same geometry that defers DeathY past
	// 600 on the way out -- so a grid with blocks in its bottom row would otherwise materialise its
	// shafts ~150px INTO the screen at spawn (the "towers pop in as the section starts" bug;
	// grids with empty bottom rows entered smoothly, which is why it only happened sometimes).
	// 0 with the towers off, so ?walltowers=0 spawns exactly as the flat original.
	private static float EntryLead()
	{
		if (!EvilAliensWeb.Compat.DebugFlags.WallTowers)
		{
			return 0f;
		}
		float depth = MathHelper.Clamp(EvilAliensWeb.Compat.DebugFlags.WallDepth ?? DefaultDepth, 0.05f, 1f);
		return VanishY * (1f / depth - 1f);
	}

	// Bundled level grids live in wwwroot/Content/levels (lowercase under Content/, the live-host
	// case-sensitivity rule). A plain new StreamReader(path) reads the WASM in-memory FS, which
	// never contains wwwroot content (it's only served over HTTP) -- TitleContainer.OpenStream is
	// the web-safe read, same as LandedOffsets/BrainBossOverlays.
	private static StreamReader OpenLevelGrid(string file)
	{
		return new StreamReader(TitleContainer.OpenStream(General.Path + "levels/" + file));
	}

	// Debug (?wallpoptest): build a wall from an arbitrary grid file under Content/levels, same
	// format as level3.txt (width=N header, X/space rows, an `end` line). Used by
	// Level3.PopulateWallPopTest to chain several SMALL sections so the entry "pop" can be watched
	// in isolation. No difficulty halving (the poptest grids are already sized for ~2 screens).
	public void SetupFromFile(string relPath)
	{
		collisionMap = null;
		traceId = -1;
		traceFrame = 0;
		traceLoggedShaft = false;
		traceLoggedTop = false;
		traceShaftQuads = 0;
		// Empty (not null) so anything visible on the very first frame logs as a POP IN.
		traceShaftPrev = EvilAliensWeb.Compat.DebugFlags.WallTrace
			? new System.Collections.Generic.HashSet<int>() : null;
		System.Collections.Generic.List<string> list = new System.Collections.Generic.List<string>();
		int num;
		using (StreamReader streamReader = OpenLevelGrid(relPath))
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
				blocks[i, j] = j < list[i].Length && list[i][j] != ' ';
			}
		}
		scale = 800f / (float)(texture.LogicalWidth() * width);
		float rowH = (float)texture.LogicalHeight() * scale;
		base.Position = new Vector2(0f, (0f - rowH) * (float)height - EntryLead());
		base.Direction = (float)Math.PI / 2f;
		base.Speed = oracle.BackgroundSpeed.Length();
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

	// The four face multipliers, (north, south, east, west), used as each wall quad's flat vertex
	// colour. DARKEN-ONLY (all <= 1) so a wall is never brighter than the lit top face it hangs
	// from. Two terms:
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
	private int DrawTowerShafts3D(float topD)
	{
		float depth = EvilAliensWeb.Compat.DebugFlags.WallDepth ?? DefaultDepth;
		float fogAmount = EvilAliensWeb.Compat.DebugFlags.WallFog ?? DefaultFog;
		float sideDark = EvilAliensWeb.Compat.DebugFlags.WallSideDark ?? DefaultSideDark;
		float faceLight = EvilAliensWeb.Compat.DebugFlags.WallFaceLight ?? DefaultFaceLight;
		float faceAngle = MathHelper.ToRadians(EvilAliensWeb.Compat.DebugFlags.WallFaceAngle ?? DefaultFaceAngle);
		// Haze colour: an explicit ?wallfogcolor wins; otherwise the LIVE alien-base floor colour, so a
		// shaft's base recedes into whatever floor is currently scrolling under it (it switches five
		// times across Level 3); DefaultFogColor is only the fallback for the first floor / off-level.
		Color fogColorFlag = EvilAliensWeb.Compat.DebugFlags.WallFogColor ?? oracle.AlienBaseFloorColor ?? DefaultFogColor;
		int bands = EvilAliensWeb.Compat.DebugFlags.Wall3DBands ?? DefaultBands;
		float blockW = (float)texture.LogicalWidth() * scale;
		float blockH = (float)texture.LogicalHeight() * scale;

		// Collect the visible blocks, then painter-sort them FAR-from-VP first so nearer-VP
		// towers paint over the ones leaning across them.
		towerOrder.Clear();
		for (int i = 0; i < height; i++)
		{
			float topY = blockH * (float)i + base.Position.Y;
			if (!RowShaftVisible(topY, blockH, depth, topD))
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
		if (EvilAliensWeb.Compat.DebugFlags.WallTrace)
		{
			TraceShaftSetDiff(depth, topD, blockH);
		}
		if (towerOrder.Count == 0)
		{
			traceShaftQuads = 0;
			return 0;
		}
		int w = width;
		Vector2 pos = base.Position;
		towerOrder.Sort((a, b) => BlockVpDistanceSq(b, w, blockW, blockH, pos)
			.CompareTo(BlockVpDistanceSq(a, w, blockW, blockH, pos)));

		// Worst case is 2 visible faces per block (a block straddling the VP on an axis shows
		// fewer), each tessellated into `bands` quads.
		EnsureTowerBuffers(towerOrder.Count * 2 * bands);

		// The shaft spans the cap (topD, above the plane when lifted) down to the ground (depth).
		float zCap = ZAtDepth(topD);
		float zBase = ZAtDepth(depth);
		// Per-face brightness, (north, south, east, west). The old slice path had to fake this with a
		// dedicated shader reading a per-sprite face mask, because a stack of axis-aligned sprites has
		// no notion of which wall it belongs to. Real geometry knows: each quad IS one wall, so its
		// shade is just its vertex colour.
		Vector4 face = FaceFactors(faceLight, faceAngle);
		int cw = texture.LogicalWidth() / 8;
		int ch = texture.LogicalHeight() / 8;

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
			// UV denominators are the ACTUAL (padded) texture size: cw/ch above are the logical
			// content cell (content stays top-left), but these are GPU texcoords, so a content pixel
			// x maps to UV x/paddedW. (No pad in ship — 756-v1 is mult-of-4 — but correct under pad.)
			float u0 = (float)(j % 8 * cw) / (float)texture.Width;
			float u1 = (float)((j % 8 + 1) * cw) / (float)texture.Width;
			float v0 = (float)(i % 8 * ch) / (float)texture.Height;
			float v1 = (float)((i % 8 + 1) * ch) / (float)texture.Height;

			// A wall is emitted only when it is BOTH an outer edge of the wall and turned toward
			// the eye.
			//
			// OUTER EDGE (isfree): a side shared with a neighbouring block is interior to the solid
			// -- two coplanar, coincident quads that should not exist at all. The old slice path could
			// not skip them (a slice is a whole block, not a face), so it passed a per-sprite face mask
			// to a shader just to avoid SHADING them, which is what mitred a dark wedge into every
			// interior corner. Real geometry simply doesn't build them.
			//
			// FACING THE EYE: the base projects toward the VP, so a block right of it shows its west
			// wall. A block straddling the VP on an axis shows neither of that axis's walls.
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
			// down range reverses between the west wall (u0 -> u1) and the east one (u1 -> u0).
			//
			// Every tower spans the same world z, so spending one whole cell down the shaft is
			// uniform for every block, however long the shaft happens to look on screen.
			if (x0 > VanishX && isfree(j - 1, i))
				AddFace(ref nv, ref quads, x0, y0, x0, y1, v0, v1, u0, u1, alongIsX: false, bands, zCap, zBase, sideDark * face.W);
			if (x1 < VanishX && isfree(j + 1, i))
				AddFace(ref nv, ref quads, x1, y1, x1, y0, v1, v0, u1, u0, alongIsX: false, bands, zCap, zBase, sideDark * face.Z);
			if (y0 > VanishY && isfree(j, i - 1))
				AddFace(ref nv, ref quads, x1, y0, x0, y0, u1, u0, v0, v1, alongIsX: true, bands, zCap, zBase, sideDark * face.X);
			if (y1 < VanishY && isfree(j, i + 1))
				AddFace(ref nv, ref quads, x0, y1, x1, y1, u0, u1, v1, v0, alongIsX: true, bands, zCap, zBase, sideDark * face.Y);
		}
		if (quads == 0)
		{
			traceShaftQuads = 0;
			return towerOrder.Count;
		}

		// Eye at the VP, `e` in front of the gameplay plane, looking down -Z (XNA is right-handed).
		// z = 0 is the gameplay plane (where the tower tops sit when unlifted), z = zBase the
		// alien-base ground; design y runs down, hence the y flip. A vertex at (x, y, z) lands at
		// VP + (xy - VP) * e/(e+z) -- which is Project(xy, d) with d = e/(e+z), so d == depth
		// exactly at z == zBase and d == topD at z == zCap. Verified against Project() to ~1e-13 px
		// by tools/walls/preview_wall3d.py's matrix check.
		float e = EyeDistance;
		Matrix view = Matrix.CreateTranslation(0f - VanishX, 0f - VanishY, 0f)
			* Matrix.CreateScale(1f, -1f, -1f)
			* Matrix.CreateTranslation(0f, 0f, 0f - e);
		Matrix projection = Matrix.CreatePerspectiveOffCenter(
			-400f * NearFrac, 400f * NearFrac, -300f * NearFrac, 300f * NearFrac, e * NearFrac, e + zBase + 1f);

		// REAL DISTANCE FOG, which is what genuine 3D buys here. The shaft's haze is now a function
		// of how far a texel is from the eye, evaluated by the fixed-function fog in BasicEffect,
		// rather than a colour the CPU bakes into each vertex. Two things follow:
		//   * It LERPS toward fogColor instead of multiplying by it, so the base actually converges
		//     on the haze colour. A sprite tint can only ever scale the texture (see DefaultFogColor).
		//   * The fog factor is linear in world z, so interpolating it across a band is exact -- the
		//     bands exist only for the dissolve, and adding more does not smooth the fog.
		// Distance to depth d is e/d (EyeDistanceAtDepth), so fog starts at the cap and reaches
		// `fogAmount` at the ground. Extending FogEnd past the ground by 1/fogAmount is what makes a
		// partial `?wallfog` land exactly on fogAmount at the base rather than clipping to fully hazed.
		float fogStart = EyeDistanceAtDepth(topD);
		float fogEnd = EyeDistanceAtDepth(depth);
		bool fogOn = fogAmount > 0.001f;
		if (fogOn && fogAmount < 1f)
		{
			fogEnd = fogStart + (fogEnd - fogStart) / fogAmount;
		}

		// The wrapper owns the shared BasicEffect + the batch, and hands the device back after the
		// one buffered draw. BlendMode is AlphaBlend here (set at the top of Draw) -> straight alpha.
		// Fog touches rgb only, so the bottom dissolve carried in vertex alpha survives it.
		traceShaftQuads = quads;
		spriteBatch.DrawGeometry3D(texture, towerVerts, nv, towerIndices, quads * 2, view, projection,
			fogOn, fogColorFlag.ToVector3(), fogStart, fogEnd);
		return towerOrder.Count;
	}

	// TEMP (?walltrace): the mid-screen pop detector. The wall only ever scrolls, so a block's
	// shaft should START drawing while it straddles a screen edge and STOP the same way -- a
	// visible-set transition whose whole shaft interval is well inside the screen is a POP the eye
	// can see, and means a cull/spawn assumption is broken. Compares this frame's row-culled block
	// set against last frame's and logs any mid-screen transition with the exact geometry.
	private void TraceShaftSetDiff(float depth, float topD, float blockH)
	{
		var cur = new System.Collections.Generic.HashSet<int>(towerOrder);
		if (traceShaftPrev != null)
		{
			foreach (int packed in cur)
			{
				if (!traceShaftPrev.Contains(packed))
				{
					TraceShaftTransition("POP IN", packed, depth, topD, blockH);
				}
			}
			foreach (int packed in traceShaftPrev)
			{
				if (!cur.Contains(packed))
				{
					TraceShaftTransition("POP OUT", packed, depth, topD, blockH);
				}
			}
		}
		traceShaftPrev = cur;
	}

	private void TraceShaftTransition(string kind, int packed, float depth, float topD, float blockH)
	{
		int i = packed / width;
		int j = packed % width;
		float topY = blockH * (float)i + base.Position.Y;
		float baseY = VanishY + (topY - VanishY) * depth;
		float capY = (topD == 1f) ? topY : VanishY + (topY - VanishY) * topD;
		float lo = Math.Min(capY, baseY);
		float hi = Math.Max(capY + blockH * topD, baseY + blockH * depth);
		// Entering/leaving through an edge is the normal, smooth case -- only log when the whole
		// shaft interval is comfortably inside the screen at the moment it (dis)appears.
		if (lo > 30f && hi < 570f)
		{
			System.Console.WriteLine($"[walltrace] wall #{traceId} {kind} block r{i} c{j} at frame {traceFrame} posY={base.Position.Y:F0} shaftY=[{lo:F0}..{hi:F0}]");
		}
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

	// One side face: the top edge (ax,ay)->(bx,by) swept from the cap (zCap) down to the ground
	// (zBase), cut into `bands` vertical strips so the bottom dissolve survives as interpolated
	// vertex alpha. `shade` is this wall's flat brightness (sideDark x its face factor) -- the haze
	// is real fog now, applied by the effect, so the colour does not vary down the shaft.
	// `alongA`/`alongB` are the along-edge texture coordinate at each end of the top edge;
	// `down0`/`down1` are the down-the-shaft one at the cap and the base. `alongIsX` says which
	// texture channel each belongs to -- see the UV note in DrawTowerShafts3D.
	private void AddFace(ref int nv, ref int quads, float ax, float ay, float bx, float by,
		float alongA, float alongB, float down0, float down1, bool alongIsX, int bands,
		float zCap, float zBase, float shade)
	{
		for (int k = 0; k < bands; k++)
		{
			float fTop = (float)k / (float)bands;
			float fBot = (float)(k + 1) / (float)bands;
			float zTop = MathHelper.Lerp(zCap, zBase, fTop);
			float zBot = MathHelper.Lerp(zCap, zBase, fBot);
			Color cTop = new Color(new Vector4(shade, shade, shade, ShaftAlpha(fTop)));
			Color cBot = new Color(new Vector4(shade, shade, shade, ShaftAlpha(fBot)));
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
		float tileX = (float)fog.LogicalWidth();
		float tileY = (float)fog.LogicalHeight();
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
			// Timed so the tower cost can be weighed against the flat path with a number rather
			// than an argument. Zero cost unless eaWallPerf is on.
			long perf = EvilAliensWeb.Compat.WallProfiler.Begin();
			DrawFogWisps(DrawTowerShafts3D(topD));
			EvilAliensWeb.Compat.WallProfiler.EndTowers(perf);
		}
		Vector2 val2 = default(Vector2);
		Color val3 = default(Color);
		Color val4 = default(Color);
		// Edge-line draw scale (card a54cc13a): `line` ("black_line_lalalal") is a SEPARATE, fixed-
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
		float lineScale = scale * (float)texture.LogicalWidth() / (float)line.LogicalWidth();
		int traceTopFaces = 0;
		for (int i = 0; i < height; i++)
		{
			if (!((float)texture.LogicalHeight() * scale * (float)i + base.Position.Y > (float)(-texture.LogicalHeight()) * scale) || !((float)texture.LogicalHeight() * scale * (float)i + base.Position.Y <= 600f))
			{
				continue;
			}
			for (int j = 0; j < width; j++)
			{
				if (blocks[i, j])
				{
					traceTopFaces++;
					Vector2 val = default(Vector2);
					val.X = (float)texture.LogicalWidth() * scale * (float)j;
					val.Y = (float)texture.LogicalHeight() * scale * (float)i;
					int num = 0;
					int num2 = j % 8;
					num = i % 8;
					// The top-face cap and its edge lines ride the lift together: projecting at
					// topD > 1 scales them away from the VP, so a cap grows and slides outward
					// exactly as its shaft's topmost slice does. Collision is unaffected -- it
					// reads `blocks` + Position, never these draw positions.
					Vector2 topLeft = val + base.Position;
					spriteBatch.Draw(texture, new Rectangle(num2 * texture.LogicalWidth() / 8, num * texture.LogicalHeight() / 8, texture.LogicalWidth() / 8, texture.LogicalHeight() / 8), lifted ? Project(topLeft, topD) : topLeft, 0f, scale * 8f * topD, center: false);
					(val2) = new Vector2((float)texture.LogicalWidth() * scale / 2f);
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

		// TEMP (?walltrace): log the first frame this wall's top faces appear vs the first frame its
		// shafts appear, plus a coarse per-entry sample -- to settle the "top slides in before its
		// pillar" report with numbers rather than reasoning.
		if (EvilAliensWeb.Compat.DebugFlags.WallTrace)
		{
			if (traceId < 0)
			{
				traceId = traceIdCounter++;
				System.Console.WriteLine($"[walltrace] wall #{traceId} spawned at posY={base.Position.Y:F0} (h={height} w={width})");
			}
			traceFrame++;
			if (!traceLoggedTop && traceTopFaces > 0)
			{
				traceLoggedTop = true;
				System.Console.WriteLine($"[walltrace] wall #{traceId} FIRST TOP FACE at frame {traceFrame} posY={base.Position.Y:F0} (topFaces={traceTopFaces}, shaftQuads={traceShaftQuads})");
			}
			if (!traceLoggedShaft && traceShaftQuads > 0)
			{
				traceLoggedShaft = true;
				System.Console.WriteLine($"[walltrace] wall #{traceId} FIRST SHAFT at frame {traceFrame} posY={base.Position.Y:F0} (topFaces={traceTopFaces}, shaftQuads={traceShaftQuads})");
			}
			if (traceFrame <= 40 && traceFrame % 8 == 0)
			{
				System.Console.WriteLine($"[walltrace] wall #{traceId} f{traceFrame} posY={base.Position.Y:F0} top={traceTopFaces} shaft={traceShaftQuads}");
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
	// have gone.
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

	// ---- Online co-op replication seam (Compat/Net, card 11.2) ---------------------------
	// The grid variation is the only caller-chosen construction input; everything else the
	// frozen Draw + CollisionLevelMap need follows from base.Position (the driver-scrolled
	// offset) plus the reconstructed grid.
	internal int NetVariation => netVariation;
}
