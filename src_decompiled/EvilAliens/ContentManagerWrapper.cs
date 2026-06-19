using Microsoft.Xna.Framework.Content;

namespace EvilAliens;

public class ContentManagerWrapper : IContentManagerService
{
	private ContentManager _content;

	public ContentManager ContentManager => _content;

	public ContentManagerWrapper(ContentManager content)
	{
		_content = content;
	}
}
