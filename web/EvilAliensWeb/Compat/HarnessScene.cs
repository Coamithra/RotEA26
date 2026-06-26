// ---------------------------------------------------------------------------
// HarnessScene — the sprite harness (see Compat/DebugFlags.cs "?harness=" and
// Compat/HarnessRegistry.cs).
//
// Boots straight onto a (real) Background showing ONE game object, drawn by its
// OWN Draw() through the OWN game pipeline (the same SpriteBatchWrapper, RenderScale,
// blend mapping, bloom and gamma the live game uses). The point is to make a
// *deterministic* target for iterating on drawing code:
//
//   * The object is FROZEN — added to the component list so it draws itself, but
//     Enabled=false so its gameplay Update never runs (no movement, no jumping off
//     screen, no Die). The harness sets its Position / curframe / scale / rotation
//     directly. Because nothing changes between frames, a screenshot at ANY moment
//     is identical: no fighting game timing to catch a frame.
//   * ?play instead lets the harness step the animation in place (curframe advances
//     at the object's own fps) — gameplay logic still doesn't run.
//
// This is 1:1 with in-game rendering precisely because it reuses the object's real
// construction (HarnessRegistry calls each type's NewXxx + Setup) and the real draw
// path — the harness only freezes time and parks the object on screen.
// ---------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using EvilAliens;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliensWeb.Compat
{
    internal class HarnessScene : Scene
    {
        public delegate void ExitEvent();

        // Fired when the user presses Esc — Game1 tears the harness down and returns
        // to the menu (handy from the harness.html picker; agents just reload the URL).
        public ExitEvent OnExitToMenu;

        private Background background;
        private AlienDrawableGameComponent obj;

        private Vector2 objPos = new Vector2(400f, 300f);
        private float frozenFrame;
        private string label = "";
        private bool error;
        private List<string> errorLines;

        // Blast lifetime visualiser (?harness=blast). The Blast's look + hit window are driven by
        // its lifetime curve, which the frozen harness never runs — so for a Blast we instead LOOP
        // an elapsed-fraction phase (0..1) and feed it to Blast.HarnessApplyPhase, drawing the real
        // collision ring + a live readout on top. Lets the bomb's fade/active window be tuned by eye
        // (?blastactive=/?blasthit=) — the card this was built for. Non-blast objects ignore all this.
        private Blast harnessBlast;
        private float blastPhase;
        private Texture2D ringTex;

        public HarnessScene(Game game)
            : base(game)
        {
            // Draw the caption on top of everything (incl. bloom at 950).
            base.DrawOrder = 2000;
            background = new Background(game);
        }

        public override void Initialize()
        {
            base.Initialize();

            // A keyboard "player" so any Setup/Draw that peeks at player state has one
            // (no actual ship is spawned). Mirrors Game1.LaunchLevelDirect.
            Oracle oracle = ServiceHelper.Get<IOracleService>().Oracle;
            oracle.ResetPlayers();
            oracle.AddPlayer(ControlDevice.Keyboard);

            ApplyBackground(DebugFlags.HarnessBg);
            ((Collection<IGameComponent>)(object)base.Game.Components).Add((IGameComponent)(object)background);

            objPos = new Vector2(DebugFlags.HarnessX ?? 400f, DebugFlags.HarnessY ?? 300f);

            if (!HarnessRegistry.TryGet(DebugFlags.Harness, out var factory))
            {
                error = true;
                errorLines = BuildUnknownMessage(DebugFlags.Harness);
                return;
            }

            try
            {
                obj = factory(Collection, base.Game, objPos);
            }
            catch (Exception ex)
            {
                error = true;
                errorLines = new List<string>
                {
                    "Harness object '" + DebugFlags.Harness + "' failed to spawn:",
                    ex.GetType().Name + ": " + ex.Message
                };
                obj = null;
            }

            if (obj == null)
            {
                return;
            }

            // Add directly to the component list (like GameScene does for Background):
            // this triggers Initialize + LoadContent synchronously, so the overrides below
            // land AFTER the object has set itself up.
            ((Collection<IGameComponent>)(object)base.Game.Components).Add((IGameComponent)(object)obj);

            int total = Math.Max(1, obj.rows * obj.columns);
            frozenFrame = ((DebugFlags.HarnessFrame % total) + total) % total;

            obj.Position = objPos;
            obj.scale *= DebugFlags.HarnessScale;   // for a blast this is overwritten every frame by
                                                    // HarnessApplyPhase (which re-applies HarnessScale itself)
            obj.rotation = MathHelper.ToRadians(DebugFlags.HarnessRot);
            obj.curframe = frozenFrame;
            obj.Enabled = false;   // freeze: no gameplay Update
            obj.Visible = true;    // but keep drawing itself

            // A Blast's appearance lives entirely in its lifetime curve (which the freeze stops),
            // so loop a phase through it instead and build the collision-ring overlay texture.
            harnessBlast = obj as Blast;
            if (harnessBlast != null)
            {
                blastPhase = 0f;
                ringTex = BuildRingTexture();
            }

            label = BuildLabel();
        }

        // A 128x128 ring (annulus) with a smooth band near the outer edge, transparent elsewhere.
        // Drawn additively over the blast at the live collision radius so the hit boundary is
        // visible against the sprite. White; the draw tints it per active/idle state.
        private Texture2D BuildRingTexture()
        {
            const int size = 128;
            const float half = size / 2f;
            const float inner = 0.92f;   // band spans normalised radius 0.92..1.0 so its bright peak hugs
                                         // the outer edge (= the true hit radius), not a few % inside it
            var data = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float r = (float)Math.Sqrt(dx * dx + dy * dy);
                    float a = 0f;
                    if (r >= inner && r <= 1f)
                    {
                        float t = (r - inner) / (1f - inner);      // 0..1 across the band
                        a = (float)Math.Sin(t * Math.PI) * 0.8f;   // smooth bump, peak ~0.94 radius
                    }
                    data[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            var tex = new Texture2D(base.GraphicsDevice, size, size);
            tex.SetData(data);
            return tex;
        }

        // The caption, rebuilt each frame so ?play's frame counter tracks the live
        // curframe (it's a field only so Draw can read the latest build).
        private string BuildLabel()
        {
            if (obj == null)
            {
                return label;
            }
            int total = Math.Max(1, obj.rows * obj.columns);
            return DebugFlags.Harness.ToLowerInvariant()
                + "   frame " + (int)obj.curframe + "/" + total
                + (DebugFlags.HarnessPlay ? "  (playing)" : "")
                + (obj.texturename != null ? "   " + obj.texturename : "");
        }

        private void ApplyBackground(string name)
        {
            switch ((name ?? "space").ToLowerInvariant())
            {
                case "spaceclassic":
                case "space2":
                    background.SetSpaceClassic();
                    break;
                case "holodeck":
                case "simplespace":
                    background.SetSimpleSpace();
                    break;
                case "mars":
                    background.SetMars();
                    break;
                case "base":
                case "alienbase":
                    background.SetAlienBase();
                    break;
                case "basedark":
                    background.SetAlienBaseDark();
                    break;
                default:
                    background.SetSpace();
                    break;
            }
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (obj != null)
            {
                // Keep it parked + frozen every frame (defensive; Enabled=false already
                // stops its own Update). In ?play mode, step the animation in place.
                obj.Enabled = false;
                obj.Position = objPos;
                if (DebugFlags.HarnessPlay)
                {
                    int total = Math.Max(1, obj.rows * obj.columns);
                    obj.curframe = (obj.curframe + obj.fps * (float)gameTime.ElapsedGameTime.TotalSeconds) % total;
                }
                else
                {
                    obj.curframe = frozenFrame;
                }

                // Loop the blast through its lifetime so the growth/fade/active window animate
                // (its own Update stays frozen; we scrub the same curve via HarnessApplyPhase).
                if (harnessBlast != null)
                {
                    float loop = Math.Max(0.25f, DebugFlags.BlastLoopSeconds);
                    blastPhase = (blastPhase + (float)gameTime.ElapsedGameTime.TotalSeconds / loop) % 1f;
                    harnessBlast.HarnessApplyPhase(blastPhase, DebugFlags.HarnessScale);
                    harnessBlast.Position = objPos;
                }

                label = BuildLabel();
            }

            if (base.InputHandler.Pressed(MyKeys.Esc) && OnExitToMenu != null)
            {
                OnExitToMenu();
            }
        }

        // Remove the object + background. Deferred through the ComponentBin so it's safe
        // to call from within the update loop. Game1 then drops the scene + shows the menu.
        public void Teardown()
        {
            if (obj != null)
            {
                Collection.Remove((GameComponent)(object)obj);
                obj = null;
            }
            harnessBlast = null;
            if (ringTex != null)
            {
                ringTex.Dispose();
                ringTex = null;
            }
            Collection.Remove((GameComponent)(object)background);
        }

        public override void Draw(GameTime gameTime)
        {
            base.Draw(gameTime);
            base.SpriteBatch.BlendMode = (SpriteBlendMode)1;
            if (error)
            {
                float y = 60f;
                foreach (string line in errorLines)
                {
                    base.SpriteBatch.DrawString(line, new Vector2(40f, y), Color.OrangeRed, 0f, centered: false, 0.6f, (SpriteEffects)0, 0f);
                    y += 26f;
                }
                return;
            }
            DrawBlastOverlay();
            base.SpriteBatch.DrawString(label, new Vector2(16f, 12f), new Color(Color.White, 0.85f), 0f, centered: false, 0.55f, (SpriteEffects)0, 0f);
            base.SpriteBatch.DrawString("Esc: menu", new Vector2(16f, 574f), new Color(Color.White, 0.5f), 0f, centered: false, 0.45f, (SpriteEffects)0, 0f);
        }

        // Blast viz: draw the real collision ring over the sprite (green = dealing damage, red =
        // inert) plus a live readout of the lifetime curve + the tunable params. The whole point of
        // the card — see at a glance that "dangerous" matches "visible" in both time and area.
        private void DrawBlastOverlay()
        {
            if (harnessBlast == null || ringTex == null)
            {
                return;
            }

            float fade = harnessBlast.CurrentFadeAlpha;   // live value the curve set, not a copy of it
            bool active = harnessBlast.Collides;
            float radius = (harnessBlast.CollisionType is CollisionSimpleCircle circle) ? circle.Radius : 0f;

            // Ring at the live hit radius (texture half = 64px maps to the ring's outer edge).
            if (radius > 1f)
            {
                Color tint = active ? new Color(0.35f, 1f, 0.45f) : new Color(1f, 0.4f, 0.3f);
                base.SpriteBatch.BlendMode = SpriteBlendMode.Additive;
                base.SpriteBatch.Draw(ringTex, objPos, 0f, radius / 64f, center: true, tint, (SpriteEffects)0);
                base.SpriteBatch.BlendMode = (SpriteBlendMode)1;
            }

            string r2 = radius.ToString("0", CultureInfo.InvariantCulture);
            float activeAlpha = DebugFlags.BlastActiveAlpha ?? 0.5f;
            float hitFactor = DebugFlags.BlastHitFactor ?? 0.8f;
            string l1 = "blast lifetime viz   loop " + DebugFlags.BlastLoopSeconds.ToString("0.0", CultureInfo.InvariantCulture) + "s";
            string l2 = "phase " + blastPhase.ToString("0.00", CultureInfo.InvariantCulture)
                + "   alpha " + fade.ToString("0.00", CultureInfo.InvariantCulture)
                + "   scale " + harnessBlast.scale.ToString("0.00", CultureInfo.InvariantCulture)
                + "   hit r " + r2 + "px";
            string l3 = active ? "ACTIVE (dealing damage)" : "idle (sprite still fading)";
            string l4 = "activeAlpha " + activeAlpha.ToString("0.00", CultureInfo.InvariantCulture) + " (?blastactive=)"
                + "   hit " + hitFactor.ToString("0.00", CultureInfo.InvariantCulture) + " (?blasthit=)";

            base.SpriteBatch.DrawString(l1, new Vector2(16f, 40f), new Color(Color.White, 0.85f), 0f, centered: false, 0.45f, (SpriteEffects)0, 0f);
            base.SpriteBatch.DrawString(l2, new Vector2(16f, 62f), new Color(Color.White, 0.85f), 0f, centered: false, 0.45f, (SpriteEffects)0, 0f);
            base.SpriteBatch.DrawString(l3, new Vector2(16f, 84f), active ? new Color(0.5f, 1f, 0.55f, 0.95f) : new Color(1f, 0.6f, 0.5f, 0.85f), 0f, centered: false, 0.45f, (SpriteEffects)0, 0f);
            base.SpriteBatch.DrawString(l4, new Vector2(16f, 106f), new Color(Color.White, 0.7f), 0f, centered: false, 0.4f, (SpriteEffects)0, 0f);
        }

        private static List<string> BuildUnknownMessage(string requested)
        {
            var lines = new List<string>
            {
                "Unknown harness object: '" + (requested ?? "") + "'",
                "Available (see HarnessRegistry.cs / harness.html):"
            };
            // Wrap the registry names a few per line so they fit the screen.
            var names = HarnessRegistry.Names;
            string row = "";
            foreach (string n in names)
            {
                string next = row.Length == 0 ? n : row + ", " + n;
                if (next.Length > 46)
                {
                    lines.Add(row);
                    row = n;
                }
                else
                {
                    row = next;
                }
            }
            if (row.Length > 0)
            {
                lines.Add(row);
            }
            return lines;
        }
    }
}
