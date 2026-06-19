using Microsoft.Xna.Framework;

namespace EvilAliens;

public interface IComponentWatcher
{
	void OnComponentRemoved(GameComponentCollectionEventArgs e);

	void OnComponentAdded(GameComponentCollectionEventArgs e);
}
