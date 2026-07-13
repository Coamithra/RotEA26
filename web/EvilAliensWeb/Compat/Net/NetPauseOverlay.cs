using EvilAliens;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace EvilAliensWeb.Compat.Net
{
    // Shown while the REMOTE peer holds the pause (card 11.3): the world is frozen via
    // ComponentBin.Push exactly like a local pause, but there is no interactive menu --
    // you can't navigate the other player's pause screen for them. Added after the Push,
    // so it stays enabled/drawn like the local pause menu does.
    public sealed class NetPauseOverlay : DrawableGameComponent
    {
        private Texture2D black;
        private SpriteFont font;

        public NetPauseOverlay(Game game)
            : base(game)
        {
            DrawOrder = 1850;
        }

        protected override void LoadContent()
        {
            base.LoadContent();
            ContentManager contentManager = ServiceHelper.Get<IContentManagerService>().ContentManager;
            black = contentManager.Load<Texture2D>("GFX/Menu/blank");
            font = contentManager.Load<SpriteFont>("GFX/Menu/menufont");
        }

        public override void Draw(GameTime gameTime)
        {
            base.Draw(gameTime);
            SpriteBatchWrapper batch = ServiceHelper.Get<ISpriteBatchWrapperService>().SpriteBatchWrapper;
            batch.Draw(black, new Rectangle(0, 0, 800, 600), new Color(new Vector4(0f, 0f, 0f, 0.5f)));
            const string text = "OTHER PLAYER PAUSED";
            Vector2 size = font.MeasureString(text);
            batch.DrawString(font, text, new Vector2(400f, 300f) - size * 0.5f, Color.White, 0f, Vector2.Zero, 1f, SpriteEffects.None, 0f);
        }
    }
}
