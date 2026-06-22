namespace EvilAliens;

public struct AnimationData
{
	public string TextureName;

	public int rows;

	public int columns;

	public int separatingspace;

	public float fps;

	// Optional play/loop sub-range within the sheet, as the half-open interval
	// [FirstFrame, LastFrame) (LastFrame exclusive). 0/0 means "whole sheet": LastFrame
	// resolves to rows*columns. Lets a sheet carry a non-grid frame count (loop stops short of
	// the padded cells) or a consumer loop just part of a longer animation (e.g. the
	// FlyingSpider plays only the "reared" sub-range of the shared spider sheet).
	public int FirstFrame;

	public int LastFrame;

	public AnimationData(string fileName, int rows, int columns, int separatingspace, float fps, int firstFrame = 0, int lastFrame = 0)
	{
		TextureName = fileName;
		this.rows = rows;
		this.columns = columns;
		this.separatingspace = separatingspace;
		this.fps = fps;
		FirstFrame = firstFrame;
		LastFrame = lastFrame;
	}

	public AnimationData(string fileName)
	{
		TextureName = fileName;
		rows = 1;
		columns = 1;
		separatingspace = 0;
		fps = 1f;
		FirstFrame = 0;
		LastFrame = 0;
	}
}
