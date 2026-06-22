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
            obj.scale *= DebugFlags.HarnessScale;
            obj.rotation = MathHelper.ToRadians(DebugFlags.HarnessRot);
            obj.curframe = frozenFrame;
            obj.Enabled = false;   // freeze: no gameplay Update
            obj.Visible = true;    // but keep drawing itself

            label = DebugFlags.Harness.ToLowerInvariant()
                + "   frame " + (int)frozenFrame + "/" + total
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
            base.SpriteBatch.DrawString(label, new Vector2(16f, 12f), new Color(Color.White, 0.85f), 0f, centered: false, 0.55f, (SpriteEffects)0, 0f);
            base.SpriteBatch.DrawString("Esc: menu", new Vector2(16f, 574f), new Color(Color.White, 0.5f), 0f, centered: false, 0.45f, (SpriteEffects)0, 0f);
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
