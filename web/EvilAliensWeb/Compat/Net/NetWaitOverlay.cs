using EvilAliens;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliensWeb.Compat.Net
{
    // Shown while the peer's ship stream has gone quiet but the match has NOT been called
    // yet -- the grace window between PeerStallMs and the drop verdict (card 11.5).
    //
    // Deliberately NOT the NetPauseOverlay shape: a remote PAUSE is a definite "here but
    // frozen" state that freezes the world behind a dimming curtain, whereas a stall is a
    // maybe -- the world keeps running (host authority does not depend on the client, and a
    // client dead-reckons), the peer usually comes back within a second, and dimming a live
    // playfield the player is still dodging in would be worse than the hiccup. So this is a
    // banner only: no dim, no freeze, no input capture.
    //
    // The common cause is benign and self-healing: a backgrounded tab drops to ~1 Hz rAF
    // (the sim falls back to a ~30 Hz setTimeout), so the stream arrives in bursts.
    public sealed class NetWaitOverlay : DrawableGameComponent
    {
        // Slot-keyed raster cache (score HUD owns 0..15, TutorialMessage 100).
        private const int BannerCacheKey = 101;

        private const string BannerText = "WAITING FOR OTHER PLAYER...";
        private const float BannerScale = 0.55f;

        private SpriteFont font;

        public NetWaitOverlay(Game game)
            : base(game)
        {
            DrawOrder = 1850;
        }

        protected override void LoadContent()
        {
            base.LoadContent();
            ContentManager contentManager = ServiceHelper.Get<IContentManagerService>().ContentManager;
            font = contentManager.Load<SpriteFont>("GFX/Menu/menufont");
        }

        public override void Draw(GameTime gameTime)
        {
            base.Draw(gameTime);
            SpriteBatchWrapper batch = ServiceHelper.Get<ISpriteBatchWrapperService>().SpriteBatchWrapper;
            Vector2 half = font.MeasureString(BannerText) * BannerScale / 2f;
            half.Y = 0f;
            // Pulse so it reads as a live "still trying", not a frozen error banner. REAL
            // time, like the rest of the net layer -- a local hit-stop or slow-mo must not
            // stall the one indicator that says the link is still being retried.
            float a = 0.55f + 0.45f * (float)System.Math.Abs(System.Math.Sin(System.Environment.TickCount64 * 0.003));
            // Flattened shadow+text: the banner rides a live playfield at a VARYING alpha, the
            // exact case where two straight-alpha layers bleed the shadow through the glyphs.
            // Cached by slot -- the text is constant, so only the composite runs per frame.
            batch.DrawShadowStringCached(BannerCacheKey, BannerText, new Vector2(400f, 92f) - half, BannerScale,
                new Color(0f, 0f, 0f), new Color(1f, 0.85f, 0.4f), new Vector2(2f, 2f), a, metal: false, 0f);
        }
    }
}
