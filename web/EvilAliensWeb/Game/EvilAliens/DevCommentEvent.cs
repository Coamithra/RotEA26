using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace EvilAliens;

internal class DevCommentEvent : GameEvent
{
	public enum CommentVersion
	{
		level1_1,
		level1_2,
		level1_3,
		level1_4
	}

	private List<string> texts;

	private bool finished;

	private int i;

	public DevCommentEvent(Game game, CommentVersion ver)
		: base(game, 0f)
	{
		texts = new List<string>();
		switch (ver)
		{
		case CommentVersion.level1_1:
			texts.Add("Hello. My name is $TTSNAME. I do the voice acting for\nRevenge of the Evil Aliens.\n\nLet me start off by saying that this has been a\ngreat project to be a part of, and that I have\ngrown a lot as an actor while working on it.");
			texts.Add("I have with me some notes written by the game's\ndesigner, Harald Maassen, that examine some of\nthe more interesting details of the game.\n\nWhile you play, I will read these notes as a form of\nrunning commentary.");
			texts.Add("Hello and thank you for playing my game.\n\nI hope you enjoy it, I've tried my best to make it a\nchallenging experience for any level of skill.");
			texts.Add("As we're flying over the Earth you'll see two of my\nmain sources of graphics: firstly there's the sprites\nused for the main characters such as the player's\nship and the UFO's.\n\nThose were all made by Danny Holten, a fellow\nstudent who these days is working on his\ndoctorate in Computer Graphics.\n\nFor the backdrops we stay in the brainy sector, as\nthose are NASA footage of actual stars and\nplanets.");
			texts.Add("The music you're hearing is from UNKLE, and I\nthink it fits nicely with the pacing and setting of\nthe game.\n\nI've always thought it a shame that many games\nhave subpar music, and I enjoyed being able to put\nmy favorite MP3s in this.");
			texts.Add("I guess technically I'm stealing this music, but I\ndon't have any moral qualms about it. I'm not\nmaking any money on this, and I doubt people will\ndownload a game just to get a low quality and\nedited version of a song.");
			texts.Add("Cheesy warnings, like the one before the asteroid\nfield, are something I wanted to put in the game\nfrom the very beginning. Arcade games always\nseem to be full of them, and that's the sort of\natmosphere I've tried to recreate.\n\nThe same could be said about the scoring: the\nactual score doesn't even do anything, but it's fun\nseeing those big numbers float across the screen.");
			texts.Add("Speaking of the combo system. I found that it was\na bit disappointing that the game didn't really\nreward big combos in a meaningful way, so that's\nhow I came up with the power-bar idea.\n\nI already had a few powerups and I thought: what\nif you could store these powerups and reuse them,\nand the effect got bigger if you had a big combo\ngoing on.");
			texts.Add("Every space shooter needs disembodied brains.\n\nI found the picture on Google and added a glow\nafter seeing a Futurama episode.\n\nThe inspiration for what these do is obviously\nAsteroids. They even wrap around the screen,\nwhich doesn't make a lot of sense considering\nnormal enemies don't.");
			texts.Add("The sounds are all from random free sound effect\nsites.\n\nI think the splatter sound made by these Brain\nSpawns is a guy eating scrunchies, and the\nexplosions are from an old desktop theme.");
			break;
		case CommentVersion.level1_2:
			texts.Add("And here we encounter one of the problems of the\nhobbyist: It's hard to find good artwork.\n\nI had to resort to scaling the original UFO sprite to\ncreate these lazer-UFO's. The result looks\npixelated, but it does somewhat fit in the cheesy\ntheme we have going.\n\nThe boss UFO is even bigger, and it looks terrible,\nbut I couldn't find anything useful to replace it\nwith.");
			texts.Add("The boss started life as an experiment. Just a way\nto see if I could get a big monster in the game that\nhalted progression for a while.\n\nIt sort of grew on me so I put it back into the\ngame, thus starting the two-bosses-per-level setup\nthat all the levels have.");
			break;
		case CommentVersion.level1_3:
			texts.Add("The final wave of the level is always very hard.\n\nYou may have noticed that the game slowly\nincreases its difficulty as you play, so even if you\narrive at this point with a lot of powerups, it will\nbe difficult to survive.\n\nWhen you die, though, the difficulty resets to its\ndefault level.\n\nWhich is still pretty hard on Hard Mode.");
			texts.Add("A problem with difficulty is that I am my main\nplaystester.\n\nI have played this game over and over to test new\nmonsters, bugs, etcetera, and as a result I've\nbecome quite the pro.");
			texts.Add("The game is balanced around me being able to\nfinish it fairly easily on Very Hard. Monsters\nspawn 100%, shoot 100% of the time and move at\n100% speed when on Very Hard.\n\nThen I added an extra difficulty level Inzane which\nis 120% that even I have a hard time beating.");
			break;
		case CommentVersion.level1_4:
			texts.Add("The final boss is an old design that I created with a\nfriend for an online contest. The idea is that he's\nvery modular and can reappear in multiple levels.\nIn this game he reappears as the subboss for level\n3 where he sucks Death Stars instead of asteroids.");
			texts.Add("This boss has undergone a lot of tweaking. He\nstarted off being very hard, and has been made\neasier multiple times.\n\nI still think he's probably the hardest boss in the\ngame, especially his incarnation on level 3.");
			break;
		}
	}

	public override void Reset()
	{
		base.Reset();
		finished = true;
		i = -1;
	}

	public override void Update(GameTime gameTime)
	{
		if (!Settings.GetInstance().DevComments)
		{
			Terminate();
			return;
		}
		if (finished)
		{
			i++;
			finished = false;
			if (i == texts.Count)
			{
				Terminate();
			}
			else
			{
				AnimatedMessage animatedMessage = AnimatedMessage.NewAnimatedMessage(collectionHelper, game);
				SoundManager soundManager = ServiceHelper.Get<ISoundManagerService>().SoundManager;
				animatedMessage.Setup(texts[i].Replace("$TTSNAME", soundManager.GetTTSName()), SoundManager.Texts.Nothing, AnimatedMessage.MessageType.devcomment);
				collectionHelper.Add((GameComponent)(object)animatedMessage);
				animatedMessage.OnFinished += m_OnFinished;
			}
		}
		base.Update(gameTime);
	}

	private void m_OnFinished(object sender)
	{
		finished = true;
	}
}
