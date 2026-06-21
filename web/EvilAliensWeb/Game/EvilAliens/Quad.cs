using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliens;

public class Quad
{
	private bool alreadyloaded;

	private Game game;

	private Texture2D middle;

	private Texture2D glow;

	private Vector3 origin;

	private Vector3 upperLeft;

	private Vector3 lowerLeft;

	private Vector3 upperRight;

	private Vector3 lowerRight;

	private Vector3 direction;

	private Vector3 left;

	public float width;

	public float height;

	public float lead;

	private static Vector3 normal = Vector3.Backward;

	// --- Protoss-style beam FX (see Draw) -----------------------------------------
	private const float GlowWidthScale = 2.6f;   // blue glow halo width vs core width
	private const float TipFlareScale = 3.0f;    // leading-tip bloom diameter vs core width
	private const float MuzzleFlareScale = 2.0f; // muzzle bloom diameter vs core width
	private const float ArcThickness = 2.0f;     // electric tendril core thickness (design px)
	private const int ArcLevels = 3;             // midpoint-displacement subdivisions per tendril
	private static readonly Color CoreColor = new Color(210, 235, 255);   // white-hot beam core
	private static readonly Color GlowColor = new Color(35, 110, 235);    // electric-blue beam glow
	private static readonly Color FlareColor = new Color(150, 215, 255);  // cyan-white bloom
	private static readonly Color ArcColor = new Color(195, 235, 255);    // tendril hot core
	private static readonly Color ArcGlowColor = new Color(45, 120, 235); // tendril blue glow
	// FX-only RNG, kept separate from the gameplay RandomHelper so render-time jitter
	// can't desync a future lockstep co-op session (Stage 11).
	private static readonly Random fxr = new Random();
	// Per-beam stable seed: each tendril is a deterministic function of (seed, time), so it
	// writhes smoothly with the clock instead of being re-randomised (= strobing) each frame.
	private readonly float fxPhase = RandF(0f, 1000f);
	// Reusable midpoint-displacement scratch buffers (no per-frame allocation in Draw).
	private static readonly Vector2[] boltA = new Vector2[64];
	private static readonly Vector2[] boltB = new Vector2[64];

	public void LoadContent()
	{
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_009a: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a4: Expected O, but got Unknown
		//IL_00aa: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ba: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ec: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f6: Expected O, but got Unknown
		//IL_00fc: Unknown result type (might be due to invalid IL or missing references)
		//IL_010c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0118: Unknown result type (might be due to invalid IL or missing references)
		//IL_013e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0148: Expected O, but got Unknown
		//IL_014e: Unknown result type (might be due to invalid IL or missing references)
		//IL_015e: Unknown result type (might be due to invalid IL or missing references)
		//IL_016a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0194: Unknown result type (might be due to invalid IL or missing references)
		//IL_019e: Expected O, but got Unknown
		if (!alreadyloaded)
		{
			alreadyloaded = true;
			ContentManager contentManager = ServiceHelper.Get<IContentManagerService>().ContentManager;
			middle = contentManager.Load<Texture2D>("GFX/Sprites/lazermiddle");
			glow = contentManager.Load<Texture2D>("GFX/Sprites/singleconnectorglow");
		}
	}

	public void UnloadGraphics()
	{
		alreadyloaded = false;
	}

	public Quad(Game game, Vector2 origin, float direction, float width, float height, float lead)
	{
		//IL_005d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0062: Unknown result type (might be due to invalid IL or missing references)
		//IL_0082: Unknown result type (might be due to invalid IL or missing references)
		//IL_0087: Unknown result type (might be due to invalid IL or missing references)
		this.game = game;
		this.origin = new Vector3(origin.X - 400f, 300f - origin.Y, 0f);
		this.height = height;
		this.lead = lead;
		this.width = width;
		this.direction = convertToVector3(direction);
		calculatePoints();
	}

	// Web port: the original beam was three textured 3D quads pushed with BasicEffect via
	// DrawUserIndexedPrimitives -- on WebGL each is a marshalled WASM->JS GL call (vertex-
	// buffer upload + effect apply + draw) and the leading SpriteBatch Flush() shattered the
	// scene's sprite batch once per laser: cheap on Xbox/native, brutal in the browser. It now
	// draws as a handful of additive sprites through the batching wrapper (no flush, no
	// immediate-mode uploads), and the flat white bolt got a Protoss-style glow-up: a wide blue
	// glow + a white-hot core (each ONE continuous sprite, so there's no segment-seam crack) +
	// round flares blooming at the muzzle and leading tip + electric tendrils crackling off it.
	public void Draw(float time)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		SpriteBatchWrapper sb = ServiceHelper.Get<ISpriteBatchWrapperService>().SpriteBatchWrapper;
		// Beam axis in screen space (y-down): texture +Y runs along the beam, +X across it.
		Vector2 dirScreen = new Vector2(direction.X, 0f - direction.Y);
		float rotation = (float)Math.Atan2(dirScreen.X, 0f - dirScreen.Y);

		Vector2 tip = ToScreen((upperLeft + upperRight) * 0.5f);
		Vector2 tail = ToScreen((lowerLeft + lowerRight) * 0.5f);
		Vector2 bodyCenter = (tip + tail) * 0.5f;
		float bodyLen = Vector2.Distance(tip, tail);
		Vector2 axis = (bodyLen > 0.001f) ? (tip - tail) / bodyLen : new Vector2(0f, -1f);
		Vector2 perp = new Vector2(0f - axis.Y, axis.X);

		SpriteBlendMode oldMode = sb.BlendMode;
		sb.BlendMode = SpriteBlendMode.Additive;
		// wide soft blue glow, then the bright hot core -- each a single continuous sprite, so
		// the old core/cap rasterisation crack can't form.
		DrawBeam(sb, middle, bodyCenter, rotation, width * GlowWidthScale, bodyLen + width, GlowColor);
		DrawBeam(sb, middle, bodyCenter, rotation, width, bodyLen, CoreColor);
		// electric tendrils crackling off the beam (smooth, time-driven -- see DrawArcs)
		DrawArcs(sb, tail, axis, perp, bodyLen, time);
		// round flares blooming at the leading tip (gently pulsing) and the muzzle
		float pulse = 1f + 0.12f * (float)Math.Sin(time * 9f + fxPhase);
		DrawFlare(sb, tip, width * TipFlareScale * pulse, FlareColor);
		DrawFlare(sb, tail, width * MuzzleFlareScale, FlareColor);
		sb.BlendMode = oldMode;
	}

	// Quad world space is centred + y-up (origin at screen centre); convert to screen pixels.
	private static Vector2 ToScreen(Vector3 p)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		return new Vector2(p.X + 400f, 300f - p.Y);
	}

	// Stretches the soft beam strip to acrossPx x alongPx, centred and rotated about `center`.
	private void DrawBeam(SpriteBatchWrapper sb, Texture2D tex, Vector2 center, float rotation, float acrossPx, float alongPx, Color color)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		Vector2 scale = new Vector2(acrossPx / (float)tex.Width, alongPx / (float)tex.Height);
		sb.Draw(tex, center, rotation, scale, center: true, color);
	}

	// Blooms the round glow texture to ~diameterPx, centred (it's radial, so rotation is moot).
	private void DrawFlare(SpriteBatchWrapper sb, Vector2 center, float diameterPx, Color color)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		float s = diameterPx / (float)glow.Width;
		sb.Draw(glow, center, 0f, new Vector2(s, s), center: true, color);
	}

	// Draws a thin glowing line p0->p1 as one stretched strip -- a single electric tendril edge.
	private void DrawLine(SpriteBatchWrapper sb, Vector2 p0, Vector2 p1, float thickness, Color color)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		Vector2 d = p1 - p0;
		float len = d.Length();
		if (len < 0.5f)
		{
			return;
		}
		float rot = (float)Math.Atan2(0f - d.X, d.Y);
		DrawBeam(sb, middle, (p0 + p1) * 0.5f, rot, thickness, len, color);
	}

	// Electric tendrils crackling off the beam. Each is a midpoint-displacement bolt (fractal
	// jaggedness, the offset halving every subdivision) whose displacements are driven by smooth
	// time functions rather than fresh RNG -- so the tendrils WRITHE instead of strobing (the fix
	// for the "spastic" look; see the /research notes). Drawn as a wide dim glow pass + a thin hot
	// core, both fading toward the free end.
	private void DrawArcs(SpriteBatchWrapper sb, Vector2 tailPt, Vector2 axis, Vector2 perp, float bodyLen, float time)
	{
		if (bodyLen < width)
		{
			return;
		}
		int count = (int)(bodyLen / 90f);
		if (count < 1) count = 1;
		if (count > 3) count = 3;
		for (int i = 0; i < count; i++)
		{
			float key = fxPhase + (float)i * 101.7f;
			// anchor sits at a stable spot along the beam; the tendril sweeps out to the side,
			// its reach and lean animated by smooth wiggles so the whole arc slithers.
			float ap = 0.12f + 0.76f * Frac(key * 0.013f);
			Vector2 anchor = tailPt + axis * (bodyLen * ap);
			float side = ((i & 1) == 0) ? 1f : -1f;
			float reach = width * (1.4f + 1.0f * Wiggle(time, key));
			float lean = width * 1.6f * Wiggle(time, key * 1.7f + 3.1f);
			Vector2 endPt = anchor + perp * (side * reach) + axis * lean;

			Vector2 d = endPt - anchor;
			float len = d.Length();
			if (len < 1f)
			{
				continue;
			}
			Vector2 bperp = new Vector2(0f - d.Y, d.X) / len;
			float amp = Math.Min(len, reach) * 0.55f;
			int n = BuildBolt(anchor, endPt, bperp, amp, time, key);
			// glow pass (wide, dim) then core pass (thin, hot), each fading toward the free end
			for (int pass = 0; pass < 2; pass++)
			{
				float thick = (pass == 0) ? ArcThickness * 2.6f : ArcThickness;
				Color col = (pass == 0) ? ArcGlowColor : ArcColor;
				for (int k = 0; k < n - 1; k++)
				{
					float fade = 1f - 0.6f * ((float)k / (float)(n - 1));
					DrawLine(sb, boltA[k], boltA[k + 1], thick, col * fade);
				}
			}
		}
	}

	// Midpoint-displacement subdivision into boltA[0..return). Each level inserts a displaced
	// midpoint between every pair and HALVES the displacement amplitude, giving smooth fractal
	// jaggedness. Displacement uses a time-driven wiggle (deterministic per seed), so the bolt
	// animates smoothly frame to frame instead of being re-rolled.
	private int BuildBolt(Vector2 start, Vector2 end, Vector2 perpUnit, float amp, float time, float seed)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		Vector2[] cur = boltA;
		Vector2[] nxt = boltB;
		cur[0] = start;
		cur[1] = end;
		int n = 2;
		float a = amp;
		for (int lvl = 0; lvl < ArcLevels; lvl++)
		{
			int m = 0;
			for (int i = 0; i < n - 1; i++)
			{
				nxt[m++] = cur[i];
				Vector2 mid = (cur[i] + cur[i + 1]) * 0.5f;
				mid += perpUnit * (a * Wiggle(time, seed + (float)(lvl * 31 + i) * 2.39f));
				nxt[m++] = mid;
			}
			nxt[m++] = cur[n - 1];
			n = m;
			Vector2[] tmp = cur;
			cur = nxt;
			nxt = tmp;
			a *= 0.5f;
		}
		if (cur != boltA)
		{
			Array.Copy(cur, boltA, n);
		}
		return n;
	}

	// Smooth, deterministic [-1,1] wiggle: two out-of-phase sines so the motion looks organic
	// instead of one obvious oscillation. Driven by time, so it animates without any RNG.
	private static float Wiggle(float time, float seed)
	{
		return 0.6f * (float)Math.Sin(time * 5.5f + seed) + 0.4f * (float)Math.Sin(time * 2.3f + seed * 1.7f);
	}

	private static float Frac(float v)
	{
		return v - (float)Math.Floor(v);
	}

	private static float RandF(float min, float max)
	{
		return (float)(fxr.NextDouble() * (max - min)) + min;
	}

	public void SetProperties(Vector2 position, float direction, float length, float lead)
	{
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		origin = new Vector3(position.X - 400f, 300f - position.Y, 0f);
		this.direction = convertToVector3(direction);
		height = length;
		this.lead = lead;
		calculatePoints();
	}

	public void SetLead(float lead)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		Vector3 val = direction * (height - lead);
		this.lead = lead;
		lowerLeft = upperLeft - val;
		lowerRight = upperRight - val;
	}

	private void calculatePoints()
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_0036: Unknown result type (might be due to invalid IL or missing references)
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		//IL_004b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0050: Unknown result type (might be due to invalid IL or missing references)
		//IL_0055: Unknown result type (might be due to invalid IL or missing references)
		//IL_005b: Unknown result type (might be due to invalid IL or missing references)
		//IL_005d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0068: Unknown result type (might be due to invalid IL or missing references)
		//IL_0072: Unknown result type (might be due to invalid IL or missing references)
		//IL_0077: Unknown result type (might be due to invalid IL or missing references)
		//IL_007c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0083: Unknown result type (might be due to invalid IL or missing references)
		//IL_0089: Unknown result type (might be due to invalid IL or missing references)
		//IL_009b: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ac: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ce: Unknown result type (might be due to invalid IL or missing references)
		left = Vector3.Cross(normal, direction);
		Vector3 val = direction * height + origin;
		upperLeft = val + left * width / 2f;
		upperRight = val - left * width / 2f;
		lowerLeft = upperLeft - direction * (height - lead);
		lowerRight = upperRight - direction * (height - lead);
	}

	public void SetLength(float length)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		Vector3 val = direction * (length - height);
		height = length;
		upperLeft += val;
		upperRight += val;
	}

	public void MoveTo(Vector2 position)
	{
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		origin = new Vector3(position.X - 400f, 300f - position.Y, 0f);
		calculatePoints();
	}

	public void AimAt(float direction)
	{
		//IL_0003: Unknown result type (might be due to invalid IL or missing references)
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		this.direction = convertToVector3(direction);
		calculatePoints();
	}

	private Vector3 convertToVector3(float direction)
	{
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		Vector2 val = default(Vector2);
		(val) = new Vector2(Convert.ToSingle(Math.Cos(direction)), -1f * Convert.ToSingle(Math.Sin(direction)));
		return new Vector3(val, 0f);
	}
}
