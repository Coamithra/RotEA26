namespace EvilAliens;

public interface ICollidable
{
	bool DetectCollision(ICollidable other);

	ICollisionType GetCollisionType();

	void CollidesWith(ICollidable other);
}
