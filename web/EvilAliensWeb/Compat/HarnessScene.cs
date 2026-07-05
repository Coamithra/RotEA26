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

        // Cast "Brain Spawn" viewer (?castbrain). The end-credits Cast screen is only reachable
        // after beating Level 3 on Hard, so this parks a CastDisplayer on its braineroid entry —
        // the one that now shows the animated brainanimated sheet + glow — for viewing/tuning
        // (?castbrainscale=/?castbrainfps=). It's a full CastDisplayer (not a single sprite), so
        // it's kept separate from the registry `obj` path below.
        private CastDisplayer castBrain;

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

        // Spider jump-cycle visualiser (?harness=spiderjump). Like the blast, the grounded Mars
        // Spider's whole crawl -> launch -> arc -> land cycle is driven by logic the frozen harness
        // never runs, so we LOOP a phase through Spider.HarnessApplyPhase (which sets its
        // Position/curframe/rotation/hasJumped) and overlay a shadow (via a low-order drawer so it
        // sits UNDER the sprite, like Floor) + jump-X/ground markers + a readout. Lets the shadow
        // position, jump-start X and land-anim resume frame be aligned by eye (?spider* flags).
        private Spider harnessSpider;
        private float spiderPhase;
        private Spider.JumpVizState spiderState;
        private Texture2D shadowTex;
        private Texture2D pixelTex;
        private SpiderShadowDrawer spiderShadowDrawer;

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

            // ?castbrain: show the end-credits Cast screen parked on the animated Brain Spawn
            // entry (see the field comment). Its own DrawOrder (1000) paints over the background;
            // Esc still exits via the normal handler below. Nothing else in this scene applies.
            if (DebugFlags.CastBrain)
            {
                castBrain = new CastDisplayer(base.Game);
                castBrain.owner = (GameComponent)(object)this;
                castBrain.BrainShowcase = true;
                ((Collection<IGameComponent>)(object)base.Game.Components).Add((IGameComponent)(object)castBrain);
                label = "cast: Brain Spawn   (?castbrainscale= ?castbrainfps=)";
                return;
            }

            // ?cast: the FULL end-credits Cast screen (every member), run through the real
            // CastDisplayer state machine. Enter advances to the next member (each asplodes as
            // in the real cast); Esc exits to the menu via the normal handler below. Reuses the
            // castBrain field purely as the holder so the cleanup at the bottom removes it.
            if (DebugFlags.CastShow)
            {
                castBrain = new CastDisplayer(base.Game);
                castBrain.owner = (GameComponent)(object)this;
                castBrain.CastShowcase = true;
                ((Collection<IGameComponent>)(object)base.Game.Components).Add((IGameComponent)(object)castBrain);
                label = "cast (full)   Enter: next member   Esc: menu";
                return;
            }

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
            // Optional fps override (?fps=<n>). Slowing the animation down makes the frame-interpolation
            // shader do all the visible work between frames, so ?harness=eyeattract&play&fps=2 proves the
            // eye's rotating/attract sheet is smoothly tweened rather than stepping. null => the sheet's
            // authored fps, so an un-flagged harness is unchanged.
            if (DebugFlags.HarnessFps.HasValue)
            {
                obj.fps = DebugFlags.HarnessFps.Value;
            }
            obj.Enabled = false;   // freeze: no gameplay Update
            obj.Visible = true;    // but keep drawing itself

            // A Blast's appearance lives entirely in its lifetime curve (which the freeze stops),
            // so loop a phase through it instead and build the collision-ring overlay texture.
            harnessBlast = obj as Blast;
            if (harnessBlast != null)
            {
                blastPhase = 0f;
            }
            // Ring overlay texture. The blast drives it through its lifetime scrubber; any other
            // object exposing a circular hitbox gets a static ring at its collision radius, so a
            // sprite-vs-hitbox size mismatch (the supersample bug class — Blast / PlasmaBall,
            // whose hand-rolled radius forgot DrawScale) is obvious at a glance. Only CIRCULAR
            // hitboxes get a ring; box-hitbox members of the class (e.g. Braineroid) show none.
            ringTex = BuildRingTexture();

            // ?harness=spiderjump: loop the full jump cycle (the "spider" key stays a plain frozen view).
            if (obj is Spider sp && string.Equals(DebugFlags.Harness, "spiderjump", StringComparison.OrdinalIgnoreCase))
            {
                harnessSpider = sp;
                spiderPhase = 0f;
                shadowTex = ServiceHelper.Get<IContentManagerService>().ContentManager.Load<Texture2D>("GFX/Sprites/shadow");
                pixelTex = BuildPixelTexture();
                spiderShadowDrawer = new SpiderShadowDrawer(base.Game, this);
                ((Collection<IGameComponent>)(object)base.Game.Components).Add((IGameComponent)(object)spiderShadowDrawer);
            }

            label = BuildLabel();
        }

        private Texture2D BuildPixelTexture()
        {
            var tex = new Texture2D(base.GraphicsDevice, 1, 1);
            tex.SetData(new[] { Color.White });
            return tex;
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
                // Keep it frozen every frame (defensive; Enabled=false already stops its own Update).
                obj.Enabled = false;

                if (harnessBlast != null)
                {
                    // Loop the blast through its lifetime so the growth/fade/active window animate
                    // (its own Update stays frozen; we scrub the same curve via HarnessApplyPhase).
                    float loop = Math.Max(0.25f, DebugFlags.BlastLoopSeconds);
                    blastPhase = (blastPhase + (float)gameTime.ElapsedGameTime.TotalSeconds / loop) % 1f;
                    harnessBlast.HarnessApplyPhase(blastPhase, DebugFlags.HarnessScale);
                    harnessBlast.Position = objPos;
                }
                else if (harnessSpider != null)
                {
                    // Loop the whole crawl -> launch -> arc -> land cycle. The sim OWNS
                    // Position/curframe/rotation/hasJumped, so don't re-park it afterward.
                    // ?spiderphase= freezes it at one point for a deterministic screenshot.
                    if (DebugFlags.SpiderPhase.HasValue)
                    {
                        spiderPhase = DebugFlags.SpiderPhase.Value;
                    }
                    else
                    {
                        float loop = Math.Max(0.5f, DebugFlags.SpiderLoopSeconds);
                        spiderPhase = (spiderPhase + (float)gameTime.ElapsedGameTime.TotalSeconds / loop) % 1f;
                    }
                    spiderState = harnessSpider.HarnessApplyPhase(spiderPhase);
                }
                else
                {
                    // Keep it parked; in ?play mode step the animation in place.
                    obj.Position = objPos;
                    if (DebugFlags.HarnessPlay)
                    {
                        // Wrap over the animation's [FirstFrame, ActiveLastFrame) sub-range the
                        // same way the engine's own Update does (AlienDrawableGameComponent) so a
                        // registered object that loops a sub-range (e.g. flyingspider = spider_sheet2
                        // frames 22..30) plays only those frames, not the whole sheet. LastFrame<=
                        // FirstFrame means "whole sheet" (mirrors the private ActiveLastFrame).
                        int activeLast = (obj.LastFrame > obj.FirstFrame) ? obj.LastFrame : obj.rows * obj.columns;
                        float span = activeLast - obj.FirstFrame;
                        if (span <= 0f)
                        {
                            span = 1f;
                        }
                        obj.curframe += obj.fps * (float)gameTime.ElapsedGameTime.TotalSeconds;
                        obj.curframe = obj.FirstFrame + ((obj.curframe - obj.FirstFrame) % span + span) % span;
                    }
                    else
                    {
                        obj.curframe = frozenFrame;
                    }
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
            if (castBrain != null)
            {
                Collection.Remove((GameComponent)(object)castBrain);
                castBrain = null;
            }
            harnessBlast = null;
            harnessSpider = null;
            if (spiderShadowDrawer != null)
            {
                ((Collection<IGameComponent>)(object)base.Game.Components).Remove((IGameComponent)(object)spiderShadowDrawer);
                spiderShadowDrawer = null;
            }
            if (pixelTex != null)
            {
                pixelTex.Dispose();
                pixelTex = null;
            }
            shadowTex = null;   // content-managed; do not dispose
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
            if (harnessBlast != null)
            {
                DrawBlastOverlay();
            }
            else if (harnessSpider != null)
            {
                DrawSpiderOverlay();
            }
            else
            {
                DrawCircleCollisionOverlay();
            }
            DrawColorizeReadout();
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

        // Generic hitbox viz for any parked object whose CollisionType is a circle: draw the real
        // collision ring at its radius over the sprite so a sprite-vs-hitbox size mismatch (the
        // supersample bug class — a re/downscaled sheet whose hand-rolled radius forgot DrawScale)
        // is visible by eye. Objects with a box hitbox (most enemies) simply show no ring. Scale is
        // ratio-preserving, so ?objscale up (e.g. a plasmaball's tiny 0.025 entry scale) to inspect.
        private void DrawCircleCollisionOverlay()
        {
            if (obj == null || ringTex == null || !(obj.CollisionType is CollisionSimpleCircle circle))
            {
                return;
            }

            float radius = circle.Radius;
            if (radius > 1f)
            {
                base.SpriteBatch.BlendMode = SpriteBlendMode.Additive;
                base.SpriteBatch.Draw(ringTex, objPos, 0f, radius / 64f, center: true, new Color(0.35f, 1f, 0.45f), (SpriteEffects)0);
                base.SpriteBatch.BlendMode = (SpriteBlendMode)1;
            }

            string line = "hitbox r " + radius.ToString("0", CultureInfo.InvariantCulture) + "px"
                + "   scale " + obj.scale.ToString("0.000", CultureInfo.InvariantCulture);
            base.SpriteBatch.DrawString(line, new Vector2(16f, 40f), new Color(Color.White, 0.85f), 0f, centered: false, 0.45f, (SpriteEffects)0, 0f);
        }

        // Spider jump-cycle overlay (?harness=spiderjump): markers for the jump-start X + the ground
        // baseline + the shadow baseline, and a readout of the derived numbers (scroll speed, the
        // back-calculated entry frame, the current frame, the tunable jump/land frames + shadow
        // offset). Drawn ON TOP (scene DrawOrder 2000); the shadow itself is drawn UNDER the sprite
        // by SpiderShadowDrawer. The whole point of the card: see the jump line up as you tune.
        private void DrawSpiderOverlay()
        {
            if (harnessSpider == null || pixelTex == null)
            {
                return;
            }
            base.SpriteBatch.BlendMode = (SpriteBlendMode)1;

            // Markers come from stable values, NOT spiderState, so they're correct even on the very
            // first Draw before Update has populated the struct (same reasoning as DrawSpiderShadow).
            int jumpX = (int)(DebugFlags.SpiderJumpX ?? 400f);
            int groundY = (int)Spider.GroundY;
            int feet = (int)(Spider.GroundY + 40f + DebugFlags.SpiderShadowY);

            // vertical jump-start marker, ground baseline, shadow (feet) baseline
            base.SpriteBatch.Draw(pixelTex, new Rectangle(jumpX - 1, 30, 2, 540), new Color(1f, 0.85f, 0.2f, 0.5f));
            base.SpriteBatch.Draw(pixelTex, new Rectangle(0, groundY, 800, 2), new Color(0.4f, 0.8f, 1f, 0.3f));
            base.SpriteBatch.Draw(pixelTex, new Rectangle(0, feet, 800, 1), new Color(1f, 1f, 1f, 0.22f));

            string l1 = "spider jump viz   loop " + DebugFlags.SpiderLoopSeconds.ToString("0.0", CultureInfo.InvariantCulture)
                + "s   scroll " + spiderState.ScrollPxPerSec.ToString("0", CultureInfo.InvariantCulture) + "px/s";
            string l2 = "jumpFrame " + spiderState.JumpFrame.ToString("0.0", CultureInfo.InvariantCulture) + " (?spiderjumpframe=)"
                + "   landFrame " + spiderState.LandFrameOut.ToString("0.0", CultureInfo.InvariantCulture) + " (?spiderlandframe=)";
            string l3 = "entryFrame " + spiderState.EntryFrame.ToString("0.0", CultureInfo.InvariantCulture)
                + "   curframe " + spiderState.CurFrame.ToString("0.0", CultureInfo.InvariantCulture)
                + (spiderState.Airborne ? "   [AIRBORNE]" : "   [ground]");
            string l4 = "jumpX " + spiderState.JumpX.ToString("0", CultureInfo.InvariantCulture) + " (?spiderjumpx=)"
                + "   shadow +(" + DebugFlags.SpiderShadowX.ToString("0", CultureInfo.InvariantCulture) + ","
                + DebugFlags.SpiderShadowY.ToString("0", CultureInfo.InvariantCulture) + ") x"
                + DebugFlags.SpiderShadowScale.ToString("0.00", CultureInfo.InvariantCulture) + " (?spidershadow*)";

            base.SpriteBatch.DrawString(l1, new Vector2(16f, 40f), new Color(Color.White, 0.85f), 0f, centered: false, 0.45f, (SpriteEffects)0, 0f);
            base.SpriteBatch.DrawString(l2, new Vector2(16f, 62f), new Color(Color.White, 0.85f), 0f, centered: false, 0.45f, (SpriteEffects)0, 0f);
            base.SpriteBatch.DrawString(l3, new Vector2(16f, 84f), spiderState.Airborne ? new Color(1f, 0.9f, 0.4f, 0.95f) : new Color(0.6f, 1f, 0.7f, 0.9f), 0f, centered: false, 0.45f, (SpriteEffects)0, 0f);
            base.SpriteBatch.DrawString(l4, new Vector2(16f, 106f), new Color(Color.White, 0.7f), 0f, centered: false, 0.4f, (SpriteEffects)0, 0f);
        }

        // The shadow, drawn UNDER the sprite by the low-DrawOrder SpiderShadowDrawer (like Floor).
        // Tracks the spider's X, sits on the tunable feet baseline, and shrinks + fades with jump
        // height — so the user aligns it under the feet at rest and watches it detach as it jumps.
        internal void DrawSpiderShadow()
        {
            if (harnessSpider == null || shadowTex == null)
            {
                return;
            }
            // Use the GroundY constant + live Position, NOT spiderState: this drawer is a separate
            // Game.Components component whose Draw can run before HarnessScene.Update refreshes the
            // struct on a frame (which parked the shadow at y~40 = the stale zeroed struct).
            float sx = harnessSpider.Position.X + DebugFlags.SpiderShadowX;
            float baseline = Spider.GroundY + 40f + DebugFlags.SpiderShadowY;
            float height = MathHelper.Max(0f, Spider.GroundY - harnessSpider.Position.Y);
            float hf = MathHelper.Clamp(height / 50f, 0f, 1f);

            float widthPx = 60f;
            if (harnessSpider.CollisionType is CollisionBox box)
            {
                widthPx = box.Right - box.Left;
            }
            float scale = widthPx / (float)shadowTex.Width * DebugFlags.SpiderShadowScale * MathHelper.Lerp(1f, 0.55f, hf);
            float alpha = MathHelper.Lerp(0.55f, 0.18f, hf);

            base.SpriteBatch.BlendMode = (SpriteBlendMode)1;
            base.SpriteBatch.Draw(shadowTex, new Vector2(sx, baseline), 0f, scale, center: true, new Color(new Vector4(1f, 1f, 1f, alpha)));
        }

        // Low-DrawOrder drawable so the spider's shadow composites UNDER the sprite (DrawOrder 20)
        // but over the background — the harness's own Draw (order 2000) is too late for that.
        private sealed class SpiderShadowDrawer : DrawableGameComponent
        {
            private readonly HarnessScene owner;

            public SpiderShadowDrawer(Game game, HarnessScene owner)
                : base(game)
            {
                this.owner = owner;
                base.DrawOrder = 15;
            }

            public override void Draw(GameTime gameTime)
            {
                owner.DrawSpiderShadow();
            }
        }

        // Colorize (hue-remap) readout for the alienboss "lightbulb" boss (?harness=battleskull
        // with ?huestart/?hueend/?huetarget/?huecycle). Shows the live band + target the shader
        // is using so the recolour can be tuned by eye — the whole point of the card. The numbers
        // come from HarnessColorize, which the BattleSkull's Draw feeds every frame; drawn only
        // when a colorize override is actually active (else the harness view is unchanged).
        private void DrawColorizeReadout()
        {
            if (!HarnessColorize.IsActive)
            {
                return;
            }
            string desc = HarnessColorize.Describe();
            base.SpriteBatch.DrawString("colorize viz (?huestart= ?hueend= ?huetarget= ?huecycle)",
                new Vector2(16f, 40f), new Color(Color.White, 0.7f), 0f, centered: false, 0.4f, (SpriteEffects)0, 0f);
            base.SpriteBatch.DrawString(desc,
                new Vector2(16f, 60f), new Color(0.5f, 1f, 0.8f, 0.95f), 0f, centered: false, 0.45f, (SpriteEffects)0, 0f);
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
