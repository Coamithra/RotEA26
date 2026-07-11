// ---------------------------------------------------------------------------
// TextShowcaseScene — a FROZEN reference grid for the flattened HUD text
// (card 37c4ccca: "the 20X combo counter / POWER UP pop looks jaggy, like it
// renders no transparency"). Everything the in-game HUD draws through
// SpriteBatchWrapper.DrawShadowString is laid out statically on the space
// background at the EXACT scales/alphas/colours the live animations use —
// score digits, the Combo! readout, and the "POWER UP!" / "10X" pop at three
// fixed points of its scale-up/fade life — in BOTH variants (plain flatten and
// the ?metalscore chrome). Nothing is Update-driven (chrome rows use a PARKED
// glint clock), so ONE screenshot at any moment shows the whole matrix
// pixel-reliably — no chasing a live pop mid-flight (the CLAUDE.md "don't
// screenshot moving targets" rule).
//
// Opt in with ?textshot (boot flag, OUT of a normal boot path — byte-identical
// without it). Esc drops back to the menu, same contract as ?lazershot /
// ?bulletshot. Template: Compat/LazerShowcaseScene.cs.
// ---------------------------------------------------------------------------
using System;
using System.Collections.ObjectModel;
using EvilAliens;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliensWeb.Compat
{
    internal class TextShowcaseScene : Scene
    {
        public delegate void ExitEvent();

        public ExitEvent OnExitToMenu;

        private Background background;

        // Chrome rows: park the glint mid-rest (same value ScoreVisualiser.ParkedGlint uses) so
        // the moving glint streak is OFF and the frame is time-independent.
        private static float ParkedGlint => SpriteBatchWrapper.MetalSweepPeriod * 0.5f;

        public TextShowcaseScene(Game game)
            : base(game)
        {
            base.DrawOrder = 2000; // on top of everything (bloom is at 950)
            background = new Background(game);
        }

        public override void Initialize()
        {
            base.Initialize();
            Oracle oracle = ServiceHelper.Get<IOracleService>().Oracle;
            oracle.ResetPlayers();
            oracle.AddPlayer(ControlDevice.Keyboard);
            background.SetSpace();
            ((Collection<IGameComponent>)(object)base.Game.Components).Add((IGameComponent)(object)background);
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (base.InputHandler.Pressed(MyKeys.Esc) && OnExitToMenu != null)
            {
                OnExitToMenu();
            }
        }

        public void Teardown()
        {
            Collection.Remove((GameComponent)(object)background);
        }

        // ScoreVisualiser.DrawStr's two-tone colours for a player colour (shadow = base hue,
        // text = brightened) — duplicated here so the showcase shows the REAL score palette.
        private static void ScoreColours(Color player, out Color shadow, out Color text)
        {
            shadow = new Color(player.ToVector3());
            text = new Color(player.ToVector3() + new Vector3(0.65f, 0.65f, 0.65f));
        }

        // One "POWER UP!"/combo pop sample at a fixed point of its life. Mirrors
        // FloatingText.Draw's pop branch exactly: num = the smoothstepped life fraction
        // (1 = birth, 0 = dead), popscale/alpha/colours/offset are the live math.
        private void DrawPopSample(string text, float baseScale, float num, Vector2 topLeft)
        {
            float popscale = (2f + 1.2f * (1f - num)) * baseScale;
            float alpha = 225f / 255f * num;
            Color textColor = new Color(byte.MaxValue, byte.MaxValue, (byte)128, byte.MaxValue);
            Color shadowColor = new Color((byte)118, (byte)118, (byte)21, byte.MaxValue);
            base.SpriteBatch.DrawShadowString(text, topLeft, popscale, shadowColor, textColor, new Vector2(3f, 3f), alpha, metal: false);
        }

        private void Label(string text, Vector2 pos)
        {
            base.SpriteBatch.DrawString(text, pos, new Color(Color.White, 0.55f), 0f, centered: false, 0.38f, (SpriteEffects)0, 0f);
        }

        public override void Draw(GameTime gameTime)
        {
            base.Draw(gameTime);
            base.SpriteBatch.BlendMode = (SpriteBlendMode)1;

            base.SpriteBatch.DrawString("textshot   flattened HUD text (DrawShadowString) reference grid",
                new Vector2(16f, 10f), new Color(Color.White, 0.85f), 0f, centered: false, 0.5f, (SpriteEffects)0, 0f);
            base.SpriteBatch.DrawString("Esc: menu", new Vector2(16f, 578f), new Color(Color.White, 0.5f), 0f, centered: false, 0.4f, (SpriteEffects)0, 0f);

            ScoreColours(Color.Blue, out Color shadowBlue, out Color textBlue);

            // --- Score / combo HUD rows: exact DrawStr values (scale 0.9 / 0.6 / 1.0; plain
            // opacity = alpha*0.55, chrome = alpha*0.7; shadow offset (2,2)). Chrome row = the
            // shipped default (MetalScore true, restored by card 16dad393); plain row = the
            // ?metalscore=0 look, parked glint.
            Label("score plain (?metalscore=0: scale 0.9, a 0.55)", new Vector2(30f, 46f));
            base.SpriteBatch.DrawShadowString("1234560", new Vector2(30f, 62f), 0.9f, shadowBlue, textBlue, new Vector2(2f, 2f), 0.55f, metal: false, ParkedGlint);
            Label("combo plain (Combo! 0.6 / 20x 1.0)", new Vector2(300f, 46f));
            base.SpriteBatch.DrawShadowString("Combo!", new Vector2(300f, 62f), 0.6f, shadowBlue, textBlue, new Vector2(2f, 2f), 0.55f, metal: false, ParkedGlint);
            base.SpriteBatch.DrawShadowString("20x", new Vector2(310f, 80f), 1f, shadowBlue, textBlue, new Vector2(2f, 2f), 0.55f, metal: false, ParkedGlint);
            Label("Press Start plain (a 0.6*0.55)", new Vector2(560f, 46f));
            base.SpriteBatch.DrawShadowString("Press Start", new Vector2(560f, 62f), 0.9f, shadowBlue, textBlue, new Vector2(2f, 2f), 0.6f * 0.55f, metal: false, ParkedGlint);

            Label("score chrome (shipped: a 0.7, parked glint)", new Vector2(30f, 116f));
            base.SpriteBatch.DrawShadowString("1234560", new Vector2(30f, 132f), 0.9f, shadowBlue, textBlue, new Vector2(2f, 2f), 0.7f, metal: true, ParkedGlint);
            Label("combo chrome", new Vector2(420f, 116f));
            base.SpriteBatch.DrawShadowString("Combo!", new Vector2(420f, 132f), 0.6f, shadowBlue, textBlue, new Vector2(2f, 2f), 0.7f, metal: true, ParkedGlint);
            base.SpriteBatch.DrawShadowString("20x", new Vector2(430f, 150f), 1f, shadowBlue, textBlue, new Vector2(2f, 2f), 0.7f, metal: true, ParkedGlint);

            // --- Floating-pop rows: the pop's life runs num 1 -> 0 (scale grows 2x -> 3.2x base,
            // alpha fades 0.88 -> 0). Three fixed phases; "POWER UP!" is the amount-100 pop
            // (base scale 0.6), "10X" the every-10th-combo pop (amount 10, base scale 0.4).
            Label("pop birth (num 1.0: scale x2.0, a 0.88)", new Vector2(30f, 208f));
            DrawPopSample("Power Up!", 0.6f, 1f, new Vector2(30f, 226f));
            DrawPopSample("10X", 0.4f, 1f, new Vector2(360f, 226f));

            Label("pop mid (num 0.5: scale x2.6, a 0.44)", new Vector2(30f, 318f));
            DrawPopSample("Power Up!", 0.6f, 0.5f, new Vector2(30f, 336f));
            DrawPopSample("10X", 0.4f, 0.5f, new Vector2(360f, 336f));

            Label("pop late (num 0.15: scale x3.02, a 0.13)", new Vector2(30f, 438f));
            DrawPopSample("Power Up!", 0.6f, 0.15f, new Vector2(30f, 456f));
            DrawPopSample("10X", 0.4f, 0.15f, new Vector2(360f, 456f));

            // Reference: raw un-flattened DrawString at the pop colours, to separate flatten
            // artefacts from plain font-atlas rendering at the same size.
            Label("raw DrawString ref (no flatten)", new Vector2(560f, 208f));
            base.SpriteBatch.DrawString("10X", new Vector2(560f, 226f), new Color((byte)255, (byte)255, (byte)128, (byte)225), 0f, centered: false, 0.8f, (SpriteEffects)0, 0f);
        }
    }
}
