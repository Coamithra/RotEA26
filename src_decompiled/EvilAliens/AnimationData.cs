namespace EvilAliens;

public struct AnimationData
{
	public string TextureName;

	public int rows;

	public int columns;

	public int separatingspace;

	public float fps;

	public AnimationData(string fileName, int rows, int columns, int separatingspace, float fps)
	{
		TextureName = fileName;
		this.rows = rows;
		this.columns = columns;
		this.separatingspace = separatingspace;
		this.fps = fps;
	}

	public AnimationData(string fileName)
	{
		TextureName = fileName;
		rows = 1;
		columns = 1;
		separatingspace = 0;
		fps = 1f;
	}
}
