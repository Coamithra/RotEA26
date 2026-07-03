namespace EvilAliens;

public enum Levels
{
	Tutorial,
	Braineroids,
	SpaceDodge,
	OwnLevel,
	Level1,
	Level2,
	Level3,
	ClassicAliens,
	InsaneBossI,
	TeamChallenge,
	Demo1,
	Demo2,
	Demo3,
	CrazyGame,
	Paratrooper,
	// Web-port addition: the webcam challenge ("I Made This!" — the remake of the
	// 2004 webcam game the splash meme is from). Appended LAST on purpose: the
	// XmlSerializer saves key on enum NAMES, so existing saves stay valid, and
	// Achievements.checkData backfills the new key instead of wiping progress.
	WebcamAliens
}
