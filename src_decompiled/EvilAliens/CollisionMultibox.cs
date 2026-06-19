using System.Collections.Generic;

namespace EvilAliens;

public class CollisionMultibox : ICollisionType
{
	public List<CollisionBox> Items = new List<CollisionBox>();

	public bool TestCollision(ICollisionType other)
	{
		bool flag = false;
		foreach (CollisionBox item in Items)
		{
			flag |= item.TestCollision(other);
		}
		return flag;
	}
}
